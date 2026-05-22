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
        testButton.Click += (_, _) => TestPrint();
        printerPanel.Controls.Add(_printerCombo, 0, 0);
        printerPanel.Controls.Add(saveButton, 1, 0);
        printerPanel.Controls.Add(testButton, 2, 0);
        root.Controls.Add(printerPanel);

        _startupCheck.Text = "Iniciar com o Windows";
        _startupCheck.Dock = DockStyle.Fill;
        _startupCheck.CheckedChanged += (_, _) => _configStore.Update(new ConfigPatch { StartWithWindows = _startupCheck.Checked });
        root.Controls.Add(_startupCheck);

        var pairPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pairPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pairPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _pairCodeLabel.Dock = DockStyle.Fill;
        _pairCodeLabel.Font = new Font("Consolas", 24f, FontStyle.Bold);
        var newCodeButton = new Button { Text = "Novo código", Dock = DockStyle.Fill };
        newCodeButton.Click += (_, _) => LoadPairingCode();
        pairPanel.Controls.Add(_pairCodeLabel, 0, 0);
        pairPanel.Controls.Add(newCodeButton, 1, 0);
        root.Controls.Add(pairPanel);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var logsButton = new Button { Text = "Abrir logs", Width = 120 };
        logsButton.Click += (_, _) => Process.Start("explorer.exe", _configStore.LogsDir);
        var restartButton = new Button { Text = "Reiniciar conexão local", Width = 180 };
        restartButton.Click += async (_, _) =>
        {
            await _apiServer.RestartAsync();
            SetMessage("Conexão local reiniciada.");
            LoadState();
        };
        actions.Controls.Add(logsButton);
        actions.Controls.Add(restartButton);
        root.Controls.Add(actions);

        _messageLabel.Dock = DockStyle.Fill;
        _messageLabel.ForeColor = Color.DarkGreen;
        root.Controls.Add(_messageLabel);
    }

    private void LoadState()
    {
        var cfg = _configStore.Get();
        _startupCheck.Checked = cfg.StartWithWindows;
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
    }

    private void LoadPairingCode()
    {
        var pairing = _pairingService.GetCode();
        _pairCodeLabel.Text = pairing.Code;
    }

    private void SaveConfig()
    {
        if (_printerCombo.SelectedItem is not PrinterInfo printer) return;
        _configStore.Update(new ConfigPatch
        {
            SelectedPrinterId = printer.Id,
            SelectedPrinterName = printer.Name
        });
        SetMessage("Configuração salva.");
    }

    private void TestPrint()
    {
        try
        {
            var selected = _printerCombo.SelectedItem as PrinterInfo;
            _printService.TestPrint(selected?.Id);
            SetMessage("Teste enviado para a impressora.");
        }
        catch
        {
            SetMessage("Não conseguimos acessar a impressora selecionada. Verifique se ela está ligada e conectada.", true);
        }
    }

    private void SetMessage(string message, bool error = false)
    {
        _messageLabel.Text = message;
        _messageLabel.ForeColor = error ? Color.DarkRed : Color.DarkGreen;
    }
}
