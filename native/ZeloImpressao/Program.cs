namespace ZeloImpressao;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, AppConstants.MutexName, out var createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();

        var configStore = new ConfigStore();
        var pairingService = new PairingService(configStore);
        var printerManager = new PrinterManager(configStore);
        var printService = new PrintService(printerManager);
        var apiServer = new LocalApiServer(configStore, pairingService, printerManager, printService);

        try
        {
            apiServer.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            configStore.Log("api_start_failed", ex.Message);
            MessageBox.Show(
                "Não foi possível iniciar a conexão local do Zelo Impressão. Verifique se outro programa já está usando a porta local.",
                AppConstants.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }

        var context = new TrayAppContext(configStore, pairingService, printerManager, printService, apiServer);
        if (args.Contains("--show", StringComparer.OrdinalIgnoreCase)) context.ShowSettings();
        Application.Run(context);
    }
}
