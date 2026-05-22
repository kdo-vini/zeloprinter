namespace ZeloImpressao;

internal sealed class TrayAppContext : ApplicationContext
{
    private static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    private readonly ConfigStore _configStore;
    private readonly PairingService _pairingService;
    private readonly PrinterManager _printerManager;
    private readonly PrintService _printService;
    private readonly LocalApiServer _apiServer;
    private readonly NotifyIcon _notifyIcon;
    private SettingsForm? _settingsForm;

    public TrayAppContext(ConfigStore configStore, PairingService pairingService, PrinterManager printerManager, PrintService printService, LocalApiServer apiServer)
    {
        _configStore = configStore;
        _pairingService = pairingService;
        _printerManager = printerManager;
        _printService = printService;
        _apiServer = apiServer;

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon,
            Text = AppConstants.ProductName,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();
    }

    public void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Show();
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        try
        {
            _settingsForm = new SettingsForm(_configStore, _pairingService, _printerManager, _printService, _apiServer);
            _settingsForm.FormClosing += (_, args) =>
            {
                if (args.CloseReason == CloseReason.UserClosing)
                {
                    args.Cancel = true;
                    _settingsForm.Hide();
                }
            };
            _settingsForm.Show();
        }
        catch (Exception ex)
        {
            _configStore.Log("settings_open_failed", ex.ToString());
            MessageBox.Show(
                "Nao foi possivel abrir as configuracoes agora. Feche e abra o Zelo Impressao novamente.",
                AppConstants.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(AppConstants.ProductName).Enabled = false;
        menu.Items.Add("Configurações", null, (_, _) => ShowSettings());
        menu.Items.Add("Abrir logs", null, (_, _) => System.Diagnostics.Process.Start("explorer.exe", _configStore.LogsDir));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, async (_, _) =>
        {
            _notifyIcon.Visible = false;
            await _apiServer.StopAsync();
            Application.Exit();
        });
        return menu;
    }
}
