using System.Drawing;
using System.Drawing.Printing;
using System.Text;
using System.Text.RegularExpressions;

namespace ZeloImpressao;

internal sealed class PrintService
{
    private readonly PrinterManager _printerManager;
    private readonly PrintDispatcher _dispatcher;

    public PrintService(PrinterManager printerManager, ConfigStore configStore, Func<PrintJob, PrinterInfo>? print = null)
    {
        _printerManager = printerManager;
        _dispatcher = new PrintDispatcher(print ?? PrintCore, () => configStore.Get().PreferredAutoPrintSource, configStore.DataDir, () => configStore.Get().PrintHistoryCapacity);
    }

    public Task<PrintDispatchResult> PrintAsync(PrintJob job) => _dispatcher.SubmitAsync(job);

    private PrinterInfo PrintCore(PrintJob job)
    {
        ValidateJob(job);
        var requested = job.PrinterId ?? job.PrinterName;
        var printer = _printerManager.ResolvePrinter(requested) ?? throw new PrintRequestException("A impressora selecionada não foi encontrada no Windows.", "PRINTER_UNAVAILABLE", 503);
        if (printer.IsOffline) throw new PrintRequestException("A impressora selecionada está offline.", "PRINTER_UNAVAILABLE", 503);

        try
        {
        switch (job.Content.Format)
        {
            case "raw_escpos_base64":
                var raw = Convert.FromBase64String(job.Content.Base64 ?? "");
                RawPrinter.SendBytes(printer.Name, raw);
                break;

            case "text":
                PrintTextViaDriver(printer.Name, job.Content.Text ?? "");
                break;

            case "html":
                PrintTextViaDriver(printer.Name, HtmlToText(job.Content.Html ?? ""));
                break;

            default:
                throw new InvalidOperationException("Formato de impressão inválido.");
        }
        }
        catch (Exception error)
        {
            throw new PrintRequestException("Não foi possível confirmar a impressão. Confira a saída antes de tentar novamente.", "PRINT_OUTCOME_UNKNOWN", 503, retrySafe: false, inner: error);
        }

        return printer;
    }

    public Task<PrintDispatchResult> TestPrintAsync(string? printerId)
    {
        return PrintAsync(new PrintJob
        {
            Source = "zelopdv", Type = "test", PrinterId = printerId,
            Content = new PrintContent { Format = "raw_escpos_base64", Base64 = Convert.ToBase64String(EscPosBuilder.BuildTestReceipt()) }
        });
    }

    internal static void ValidateJob(PrintJob job)
    {
        if (job.Source is not ("zelopdv" or "zelochat")) throw new PrintRequestException("Origem inválida.", "INVALID_JOB");
        if (job.Type is not ("receipt" or "kitchen_order" or "test" or "raw_escpos")) throw new PrintRequestException("Tipo de impressão inválido.", "INVALID_JOB");
        if (job.JobId?.Length > 128 || job.CompanyStoreId?.Length > 256) throw new PrintRequestException("Identificador de impressão inválido.", "INVALID_JOB");
        if (job.Intent is not null && job.Intent.Mode is not ("automatic" or "manual"))
            throw new PrintRequestException("Intenção de impressão inválida.", "INVALID_JOB");
        if (job.Intent?.Mode == "automatic" && (!Guid.TryParse(job.CompanyStoreId, out _) || !Guid.TryParse(job.Intent.OrderId, out _) || job.Intent.Purpose != "order_ticket"))
            throw new PrintRequestException("Impressão automática exige o dono da loja, pedido e finalidade canônicos.", "INVALID_JOB");
        var content = job.Content;
        var valid = content?.Format switch
        {
            "text" => !string.IsNullOrWhiteSpace(content.Text),
            "html" => !string.IsNullOrWhiteSpace(content.Html),
            "raw_escpos_base64" => IsValidBase64(content.Base64),
            _ => false
        };
        if (!valid) throw new PrintRequestException("Conteúdo de impressão inválido ou vazio.", "INVALID_JOB");
    }

    private static bool IsValidBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return Convert.FromBase64String(value).Length > 0; }
        catch (FormatException) { return false; }
    }

    private static void PrintTextViaDriver(string printerName, string text)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printerName;
        doc.DocumentName = AppConstants.ProductName;
        doc.PrintController = new StandardPrintController();

        var remainingText = NormalizeText(text);
        using var font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

        doc.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics ?? throw new InvalidOperationException("O driver não forneceu uma superfície para impressão.");
            var bounds = e.MarginBounds;
            bounds.X = 5;
            bounds.Y = 5;
            bounds.Width = Math.Max(200, e.PageBounds.Width - 10);
            bounds.Height = Math.Max(200, e.PageBounds.Height - 10);

            var printedCharacters = DrawTextPage(graphics, font, bounds, remainingText);
            remainingText = remainingText[printedCharacters..];
            e.HasMorePages = remainingText.Length > 0;
        };

        doc.Print();
    }

    internal static int DrawTextPage(Graphics graphics, Font font, Rectangle bounds, string text)
    {
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.LineLimit;
        graphics.MeasureString(text, font, bounds.Size, format, out var charactersFitted, out _);
        if (charactersFitted <= 0)
            throw new InvalidOperationException("O papel configurado não tem espaço para imprimir o conteúdo.");
        graphics.DrawString(text[..charactersFitted], font, Brushes.Black, bounds, format);
        return charactersFitted;
    }

    private static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(p|div|tr|li|h[1-6])>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(text);
    }

    private static string NormalizeText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "\n" : text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
