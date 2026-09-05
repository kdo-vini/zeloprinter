using System.IO.Pipes;

namespace ZeloImpressao;

// A second launch can only request that the current user's settings be shown.
internal static class InstanceSignal
{
    internal const string PipeName = "Techne_Zelo_Impressao_Show";

    public static async Task NotifyAsync(string pipeName = PipeName, CancellationToken cancellationToken = default)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(1500, cancellationToken).ConfigureAwait(false);
        await client.WriteAsync(new byte[] { 1 }, cancellationToken).ConfigureAwait(false);
    }

    public static async Task ListenAsync(string pipeName, Action showSettings, Action<Exception> failed, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(1));
                var command = new byte[1];
                if (await server.ReadAsync(command, requestTimeout.Token).ConfigureAwait(false) == 1 && command[0] == 1)
                    showSettings();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                failed(error);
                try { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }
}
