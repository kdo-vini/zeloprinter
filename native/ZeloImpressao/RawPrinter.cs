using System.Runtime.InteropServices;

namespace ZeloImpressao;

internal static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName = AppConstants.ProductName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr printerDefaults);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool ClosePrinter(IntPtr printerHandle);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool StartDocPrinter(IntPtr printerHandle, int level, [In] DocInfo docInfo);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndDocPrinter(IntPtr printerHandle);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool StartPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool WritePrinter(IntPtr printerHandle, byte[] bytes, int count, out int written);

    public static void SendBytes(string printerName, byte[] bytes)
    {
        if (!OpenPrinter(printerName.Normalize(), out var handle, IntPtr.Zero))
        {
            ThrowWin32("Não conseguimos abrir a impressora selecionada.");
        }

        try
        {
            var doc = new DocInfo();
            if (!StartDocPrinter(handle, 1, doc)) ThrowWin32("Não conseguimos iniciar a impressão.");
            try
            {
                if (!StartPagePrinter(handle)) ThrowWin32("Não conseguimos iniciar a página de impressão.");
                try
                {
                    if (!WritePrinter(handle, bytes, bytes.Length, out var written)) ThrowWin32("Não conseguimos enviar dados para a impressora.");
                    if (written != bytes.Length) throw new InvalidOperationException("Nem todos os dados foram enviados para a impressora.");
                }
                finally
                {
                    EndPagePrinter(handle);
                }
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    private static void ThrowWin32(string prefix)
    {
        var error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException($"{prefix} Código Windows: {error}.");
    }
}
