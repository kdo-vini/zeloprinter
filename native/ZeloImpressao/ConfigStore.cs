using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZeloImpressao;

internal sealed class ConfigStore
{
    private readonly string _dataDir;
    private readonly string _configPath;
    private readonly object _lock = new();
    private readonly object _logLock = new();
    internal const int MaxPairedBrowsers = 50;
    private AgentConfig _config;
    private readonly bool _manageStartup;

    public ConfigStore(string? dataDir = null)
    {
        _manageStartup = dataDir is null;
        _dataDir = dataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zelo Impressao");
        _configPath = Path.Combine(_dataDir, "config.json");
        Directory.CreateDirectory(_dataDir);
        _config = Load();
        if (_manageStartup) ApplyStartup(_config.StartWithWindows);
    }

    public string DataDir => _dataDir;
    public string? StartupError { get; private set; }
    public string LogsDir
    {
        get
        {
            var path = Path.Combine(_dataDir, "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public AgentConfig Get()
    {
        lock (_lock)
        {
            return Clone(_config);
        }
    }

    public AgentConfig Update(ConfigPatch patch)
    {
        lock (_lock)
        {
            var next = Clone(_config);
            if (patch.SelectedPrinterId is not null) next.SelectedPrinterId = patch.SelectedPrinterId;
            if (patch.SelectedPrinterName is not null) next.SelectedPrinterName = patch.SelectedPrinterName;
            if (patch.StartWithWindows.HasValue) next.StartWithWindows = patch.StartWithWindows.Value;
            if (patch.RequirePairing.HasValue) next.RequirePairing = patch.RequirePairing.Value;
            if (patch.AutoConnectEnabled.HasValue) next.AutoConnectEnabled = patch.AutoConnectEnabled.Value;
            if (patch.PreferredAutoPrintSource is not null)
            {
                if (patch.PreferredAutoPrintSource is not ("zelopdv" or "zelochat"))
                    throw new PrintRequestException("Aplicativo preferido inválido.", "INVALID_CONFIG");
                next.PreferredAutoPrintSource = patch.PreferredAutoPrintSource;
            }
            if (patch.PrintHistoryCapacity.HasValue)
            {
                if (patch.PrintHistoryCapacity < PrintJournal.MaxEntries || patch.PrintHistoryCapacity > PrintJournal.MaxCapacity)
                    throw new PrintRequestException("Capacidade do histórico deve estar entre 10000 e 50000 registros.", "INVALID_CONFIG");
                next.PrintHistoryCapacity = patch.PrintHistoryCapacity.Value;
            }
            Save(next);
            _config = next;
            if (_manageStartup) ApplyStartup(_config.StartWithWindows);
            if (patch.StartWithWindows.HasValue && StartupError is not null)
                throw new PrintRequestException(StartupError, "STARTUP_SETTING_FAILED", 503);
            return Clone(_config);
        }
    }

    public string IssueToken(bool automatic = false)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        lock (_lock)
        {
            if (automatic && !_config.AutoConnectEnabled)
                throw new PrintRequestException("A conexão automática está desativada. Use o código exibido no aplicativo local.", "AUTO_CONNECT_DISABLED", 403);
            var next = Clone(_config);
            if (next.TokenHashes.Count >= MaxPairedBrowsers)
                throw new PrintRequestException("Limite de navegadores atingido. Desconecte os navegadores nas configurações locais antes de parear novamente.", "PAIRING_LIMIT", 409);
            next.TokenHashes.Add(HashToken(token));
            Save(next);
            _config = next;
        }
        return token;
    }

    public bool VerifyToken(string? token)
    {
        var cfg = Get();
        if (!cfg.RequirePairing) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var candidate = Encoding.UTF8.GetBytes(HashToken(token));
        return cfg.TokenHashes.Any(hash => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), candidate));
    }

    public void RevokeTokens()
    {
        lock (_lock)
        {
            var next = Clone(_config);
            next.TokenHash = null;
            next.TokenHashes.Clear();
            next.RequirePairing = true;
            next.AutoConnectEnabled = false;
            Save(next);
            _config = next;
        }
    }

    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Log(string message, object? data = null)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                ts = AppClock.UtcNow,
                message,
                data
            });
            lock (_logLock)
            {
                var path = Path.Combine(LogsDir, "zelo-impressao.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 5 * 1024 * 1024)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never block printing.
        }
    }

    private AgentConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath)) return new AgentConfig();
            var config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(_configPath, Encoding.UTF8)) ?? new AgentConfig();
            config.TokenHashes = (config.TokenHashes ?? []).Prepend(config.TokenHash ?? "")
                .Where(hash => !string.IsNullOrWhiteSpace(hash)).Select(hash => hash.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal).ToList();
            config.TokenHash = null;
            config.AllowedOrigins = (config.AllowedOrigins ?? []).Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Select(origin => origin.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (config.AllowedOrigins.Count == 0) config.AllowedOrigins = AppConstants.DefaultAllowedOrigins.ToList();
            if (config.PreferredAutoPrintSource is not ("zelopdv" or "zelochat")) config.PreferredAutoPrintSource = "zelopdv";
            config.PrintHistoryCapacity = Math.Clamp(config.PrintHistoryCapacity, PrintJournal.MaxEntries, PrintJournal.MaxCapacity);
            return config;
        }
        catch (Exception error)
        {
            Log("config_load_failed", new { error = error.GetType().Name });
            return new AgentConfig();
        }
    }

    private void Save(AgentConfig config)
    {
        Directory.CreateDirectory(_dataDir);
        var temporaryPath = _configPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(temporaryPath, _configPath, overwrite: true);
    }

    private static AgentConfig Clone(AgentConfig config)
    {
        return new AgentConfig
        {
            SelectedPrinterId = config.SelectedPrinterId,
            SelectedPrinterName = config.SelectedPrinterName,
            StartWithWindows = config.StartWithWindows,
            RequirePairing = config.RequirePairing,
            AutoConnectEnabled = config.AutoConnectEnabled,
            PreferredAutoPrintSource = config.PreferredAutoPrintSource,
            PrintHistoryCapacity = config.PrintHistoryCapacity,
            TokenHash = config.TokenHash,
            TokenHashes = config.TokenHashes.ToList(),
            AllowedOrigins = config.AllowedOrigins.ToList()
        };
    }

    public static ApiConfigView ToApiView(AgentConfig config) => new()
    {
        SelectedPrinterId = config.SelectedPrinterId,
        SelectedPrinterName = config.SelectedPrinterName,
        StartWithWindows = config.StartWithWindows,
        RequirePairing = config.RequirePairing,
        AutoConnectEnabled = config.AutoConnectEnabled,
        PreferredAutoPrintSource = config.PreferredAutoPrintSource,
        PrintHistoryCapacity = config.PrintHistoryCapacity,
        AllowedOrigins = config.AllowedOrigins.ToList()
    };

    private void ApplyStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) throw new IOException("Could not open the Windows startup registry key.");
            if (enabled)
            {
                key.SetValue("Zelo Impressao", $"\"{Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue("Zelo Impressao", throwOnMissingValue: false);
            }
            StartupError = null;
        }
        catch (Exception error)
        {
            StartupError = "O Windows não permitiu alterar a inicialização automática. Verifique as permissões do usuário.";
            Log("startup_update_failed", new { enabled, error = error.GetType().Name });
        }
    }
}
