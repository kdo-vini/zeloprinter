namespace ZeloImpressao;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, AppConstants.MutexName, out var createdNew);
        if (!createdNew)
        {
            try { InstanceSignal.NotifyAsync().GetAwaiter().GetResult(); }
            catch (Exception) { /* The existing tray remains available if IPC is unavailable. */ }
            return;
        }

        ApplicationConfiguration.Initialize();

        var configStore = new ConfigStore();
        var pairingService = new PairingService(configStore);
        var printerManager = new PrinterManager(configStore);
        var printService = new PrintService(printerManager, configStore);
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

        using var context = new TrayAppContext(configStore, pairingService, printerManager, printService, apiServer);
        using var uiDispatcher = new Control();
        _ = uiDispatcher.Handle;
        using var signals = new CancellationTokenSource();
        var signalTask = InstanceSignal.ListenAsync(InstanceSignal.PipeName,
            () => uiDispatcher.BeginInvoke(new Action(context.ShowSettings)),
            error => configStore.Log("instance_signal_failed", new { error = error.GetType().Name }), signals.Token);
        if (args.Contains("--show", StringComparer.OrdinalIgnoreCase)) context.ShowSettings();
        try { Application.Run(context); }
        finally
        {
            signals.Cancel();
            signalTask.GetAwaiter().GetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { apiServer.StopAsync(timeout.Token).GetAwaiter().GetResult(); }
            catch (Exception error) { configStore.Log("api_shutdown_failed", new { error = error.GetType().Name }); }
        }
    }
}
