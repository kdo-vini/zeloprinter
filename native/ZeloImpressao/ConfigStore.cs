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
    private AgentConfig _config;

    public ConfigStore()
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zelo Impressao");
        _configPath = Path.Combine(_dataDir, "config.json");
        Directory.CreateDirectory(_dataDir);
        _config = Load();
        ApplyStartup(_config.StartWithWindows);
    }

    public string DataDir => _dataDir;
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
            if (patch.SelectedPrinterId is not null) _config.SelectedPrinterId = patch.SelectedPrinterId;
            if (patch.SelectedPrinterName is not null) _config.SelectedPrinterName = patch.SelectedPrinterName;
            if (patch.StartWithWindows.HasValue) _config.StartWithWindows = patch.StartWithWindows.Value;
            if (patch.RequirePairing.HasValue) _config.RequirePairing = patch.RequirePairing.Value;
            Save(_config);
            ApplyStartup(_config.StartWithWindows);
            return Clone(_config);
        }
    }

    public string IssueToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        lock (_lock)
        {
            _config.TokenHash = HashToken(token);
            Save(_config);
        }
        return token;
    }

    public bool VerifyToken(string? token)
    {
        var cfg = Get();
        if (!cfg.RequirePairing) return true;
        if (string.IsNullOrWhiteSpace(cfg.TokenHash) || string.IsNullOrWhiteSpace(token)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(cfg.TokenHash),
            Encoding.UTF8.GetBytes(HashToken(token))
        );
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
                ts = DateTimeOffset.Now,
                message,
                data
            });
            File.AppendAllText(Path.Combine(LogsDir, "zelo-impressao.log"), line + Environment.NewLine, Encoding.UTF8);
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
            return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(_configPath, Encoding.UTF8)) ?? new AgentConfig();
        }
        catch
        {
            return new AgentConfig();
        }
    }

    private void Save(AgentConfig config)
    {
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static AgentConfig Clone(AgentConfig config)
    {
        return new AgentConfig
        {
            SelectedPrinterId = config.SelectedPrinterId,
            SelectedPrinterName = config.SelectedPrinterName,
            StartWithWindows = config.StartWithWindows,
            RequirePairing = config.RequirePairing,
            TokenHash = config.TokenHash,
            AllowedOrigins = config.AllowedOrigins.ToList()
        };
    }

    private static void ApplyStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enabled)
            {
                key.SetValue("Zelo Impressao", $"\"{Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue("Zelo Impressao", throwOnMissingValue: false);
            }
        }
        catch
        {
            // Startup is a convenience setting; API/printing must keep running.
        }
    }
}
