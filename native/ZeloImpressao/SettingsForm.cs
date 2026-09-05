using System.Diagnostics;

namespace ZeloImpressao;

internal sealed class SettingsForm : Form
{
    private readonly ConfigStore _configStore;
    private readonly PairingService _pairingService;
    private readonly PrinterManager _printerManager;
    private readonly PrintService _printService;
    private readonly LocalApiServer _apiServer;

    private readonly Label _statusLabel = new();
    private readonly ComboBox _printerCombo = new();
    private readonly CheckBox _startupCheck = new();
    private readonly CheckBox _autoConnectCheck = new();
    private readonly ComboBox _autoPrintSourceCombo = new();
    private bool _loadingState;
    private readonly Label _pairCodeLabel = new();
    private readonly Label _messageLabel = new();

    public SettingsForm(ConfigStore configStore, PairingService pairingService, PrinterManager printerManager, PrintService printService, LocalApiServer apiServer)
    {
        _configStore = configStore;
        _pairingService = pairingService;
        _printerManager = printerManager;
        _printService = printService;
        _apiServer = apiServer;

        Text = AppConstants.ProductName;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Width = 760;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        BuildUi();
        LoadState();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 7,
            ColumnCount = 1
        };
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var title = new Label
        {
            Text = $"{AppConstants.ProductName}\n{AppConstants.ProductDescription}",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold)
        };
        root.Controls.Add(title);

        _statusLabel.Dock = DockStyle.Fill;
        root.Controls.Add(_statusLabel);

        var printerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        printerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        printerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        printerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        _printerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _printerCombo.Dock = DockStyle.Fill;
        var saveButton = new Button { Text = "Salvar", Dock = DockStyle.Fill };
        saveButton.Click += (_, _) => SaveConfig();
        var testButton = new Button { Text = "Teste", Dock = DockStyle.Fill };
        testButton.Click += async (_, _) =>
        {
            testButton.Enabled = false;
            try { await TestPrintAsync(); }
            finally { testButton.Enabled = true; }
        };
        printerPanel.Controls.Add(_printerCombo, 0, 0);
        printerPanel.Controls.Add(saveButton, 1, 0);
        printerPanel.Controls.Add(testButton, 2, 0);
        root.Controls.Add(printerPanel);

        _startupCheck.Text = "Iniciar com o Windows";
        _startupCheck.Width = 190;
        _startupCheck.CheckedChanged += (_, _) => { if (!_loadingState) SavePreference(new ConfigPatch { StartWithWindows = _startupCheck.Checked }); };
        var preferences = new FlowLayoutPanel { Dock = DockStyle.Fill };
        preferences.Controls.Add(_startupCheck);
        preferences.Controls.Add(new Label { Text = "Preferir impressão automática por:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        _autoPrintSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _autoPrintSourceCombo.Items.AddRange(["ZeloPDV", "ZeloChat"]);
        _autoPrintSourceCombo.Width = 120;
        _autoPrintSourceCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingState) SavePreference(new ConfigPatch { PreferredAutoPrintSource = _autoPrintSourceCombo.SelectedIndex == 1 ? "zelochat" : "zelopdv" });
        };
        preferences.Controls.Add(_autoPrintSourceCombo);
        preferences.SetFlowBreak(_autoPrintSourceCombo, true);
        _autoConnectCheck.Text = "Permitir conexão automática dos aplicativos Zelo";
        _autoConnectCheck.AutoSize = true;
        _autoConnectCheck.CheckedChanged += (_, _) => { if (!_loadingState) SavePreference(new ConfigPatch { AutoConnectEnabled = _autoConnectCheck.Checked }); };
        preferences.Controls.Add(_autoConnectCheck);
        root.Controls.Add(preferences);

        var pairPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pairPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pairPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _pairCodeLabel.Dock = DockStyle.Fill;
        _pairCodeLabel.Font = new Font("Consolas", 24f, FontStyle.Bold);
        var newCodeButton = new Button { Text = "Novo código", Dock = DockStyle.Fill };
        newCodeButton.Click += (_, _) => LoadPairingCode(renew: true);
        pairPanel.Controls.Add(_pairCodeLabel, 0, 0);
        pairPanel.Controls.Add(newCodeButton, 1, 0);
        root.Controls.Add(pairPanel);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var logsButton = new Button { Text = "Abrir logs", Width = 120 };
        logsButton.Click += (_, _) => Process.Start("explorer.exe", _configStore.LogsDir);
        var restartButton = new Button { Text = "Reiniciar conexão local", Width = 180 };
        restartButton.Click += async (_, _) =>
        {
            restartButton.Enabled = false;
            try
            {
                await _apiServer.RestartAsync();
                SetMessage("Conexão local reiniciada.");
                LoadState();
            }
            catch (Exception error)
            {
                _configStore.Log("api_restart_failed", new { error = error.GetType().Name });
                SetMessage("Não foi possível reiniciar a conexão local. Verifique os logs.", true);
            }
            finally { restartButton.Enabled = true; }
        };
        var revokeButton = new Button { Text = "Desconectar navegadores", Width = 180 };
        revokeButton.Click += (_, _) =>
        {
            try
            {
                _configStore.RevokeTokens();
                _loadingState = true;
                _autoConnectCheck.Checked = false;
                _loadingState = false;
                LoadPairingCode(renew: true);
                SetMessage("Navegadores desconectados e conexão automática desativada. Use o novo código para conectar novamente.");
            }
            catch (Exception error)
            {
                _loadingState = false;
                _configStore.Log("pairing_revoke_failed", new { error = error.GetType().Name });
                SetMessage("Não foi possível desconectar os navegadores. Verifique os logs.", true);
            }
        };
        actions.Controls.Add(logsButton);
        actions.Controls.Add(restartButton);
        actions.Controls.Add(revokeButton);
        var historyButton = new Button { Text = "Ampliar histórico", Width = 135 };
        historyButton.Click += (_, _) =>
        {
            var capacity = _configStore.Get().PrintHistoryCapacity;
            if (capacity >= PrintJournal.MaxCapacity) { SetMessage("O histórico já está na capacidade máxima. Os registros expiram após sete dias; não apague o arquivo para liberar impressões.", true); return; }
            SavePreference(new ConfigPatch { PrintHistoryCapacity = Math.Min(capacity + 10000, PrintJournal.MaxCapacity) });
        };
        actions.Controls.Add(historyButton);
        root.Controls.Add(actions);

        _messageLabel.Dock = DockStyle.Fill;
        _messageLabel.ForeColor = Color.DarkGreen;
        root.Controls.Add(_messageLabel);
    }

    private void LoadState()
    {
        var cfg = _configStore.Get();
        _loadingState = true;
        _startupCheck.Checked = cfg.StartWithWindows;
        _autoConnectCheck.Checked = cfg.AutoConnectEnabled;
        _autoPrintSourceCombo.SelectedIndex = cfg.PreferredAutoPrintSource == "zelochat" ? 1 : 0;
        _loadingState = false;
        _statusLabel.Text = $"Conectado neste computador · API local {(_apiServer.IsRunning ? "ativa" : "parada")} · Versão {AppConstants.Version}";
        _printerCombo.Items.Clear();

        foreach (var printer in _printerManager.ListPrinters())
        {
            _printerCombo.Items.Add(printer);
            if (printer.Id == cfg.SelectedPrinterId || printer.Name == cfg.SelectedPrinterName)
            {
                _printerCombo.SelectedItem = printer;
            }
        }

        if (_printerCombo.SelectedIndex < 0 && _printerCombo.Items.Count > 0)
        {
            _printerCombo.SelectedIndex = 0;
        }

        LoadPairingCode();
        if (_configStore.StartupError is not null) SetMessage(_configStore.StartupError, true);
    }

    private void LoadPairingCode(bool renew = false)
    {
        var pairing = _pairingService.GetCode(renew);
        _pairCodeLabel.Text = pairing.Code;
    }

    private void SaveConfig()
    {
        if (_printerCombo.SelectedItem is not PrinterInfo printer) return;
        SavePreference(new ConfigPatch
        {
            SelectedPrinterId = printer.Id,
            SelectedPrinterName = printer.Name
        });
    }

    private void SavePreference(ConfigPatch patch)
    {
        try { _configStore.Update(patch); SetMessage("Preferência salva."); }
        catch (Exception error)
        {
            _configStore.Log("preference_save_failed", new { error = error.GetType().Name });
            _loadingState = true;
            _autoConnectCheck.Checked = _configStore.Get().AutoConnectEnabled;
            _loadingState = false;
            SetMessage("Não foi possível salvar a preferência. Verifique os logs.", true);
        }
    }

    private async Task TestPrintAsync()
    {
        try
        {
            var selected = _printerCombo.SelectedItem as PrinterInfo;
            await _printService.TestPrintAsync(selected?.Id);
            SetMessage("Teste enviado para a impressora.");
        }
        catch (Exception error)
        {
            _configStore.Log("test_print_failed", new { error = error.GetType().Name });
            SetMessage(error.Message, true);
        }
    }

    private void SetMessage(string message, bool error = false)
    {
        _messageLabel.Text = message;
        _messageLabel.ForeColor = error ? Color.DarkRed : Color.DarkGreen;
    }
}
