using System.Drawing;
using System.Drawing.Printing;
using System.Text;
using System.Text.RegularExpressions;

namespace ZeloImpressao;

internal sealed class PrintService
{
    private readonly PrinterManager _printerManager;

    public PrintService(PrinterManager printerManager)
    {
        _printerManager = printerManager;
    }

    public PrinterInfo Print(PrintJob job)
    {
        ValidateJob(job);
        var requested = job.PrinterId ?? job.PrinterName;
        var printer = _printerManager.ResolvePrinter(requested) ?? throw new InvalidOperationException("Nenhuma impressora instalada foi encontrada no Windows.");
        if (printer.IsOffline) throw new InvalidOperationException("A impressora selecionada está offline.");

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

        return printer;
    }

    public PrinterInfo TestPrint(string? printerId)
    {
        var printer = _printerManager.ResolvePrinter(printerId) ?? throw new InvalidOperationException("Nenhuma impressora instalada foi encontrada no Windows.");
        if (printer.IsOffline) throw new InvalidOperationException("A impressora selecionada está offline.");
        RawPrinter.SendBytes(printer.Name, EscPosBuilder.BuildTestReceipt());
        return printer;
    }

    private static void ValidateJob(PrintJob job)
    {
        if (job.Source is not ("zelopdv" or "zelochat")) throw new InvalidOperationException("Origem inválida.");
        if (job.Type is not ("receipt" or "kitchen_order" or "test" or "raw_escpos")) throw new InvalidOperationException("Tipo de impressão inválido.");
        if (string.IsNullOrWhiteSpace(job.Content.Format)) throw new InvalidOperationException("Conteúdo de impressão ausente.");
    }

    private static void PrintTextViaDriver(string printerName, string text)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printerName;
        doc.DocumentName = AppConstants.ProductName;

        var lines = NormalizeText(text)
            .Split(Environment.NewLine, StringSplitOptions.None);
        var currentLine = 0;
        using var font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

        doc.PrintPage += (_, e) =>
        {
            var bounds = e.MarginBounds;
            bounds.X = 5;
            bounds.Y = 5;
            bounds.Width = Math.Max(200, e.PageBounds.Width - 10);
            bounds.Height = Math.Max(200, e.PageBounds.Height - 10);

            var lineHeight = font.GetHeight(e.Graphics);
            var maxLines = Math.Max(1, (int)Math.Floor(bounds.Height / lineHeight));
            var pageLines = lines
                .Skip(currentLine)
                .Take(maxLines)
                .ToArray();

            e.Graphics.DrawString(
                string.Join(Environment.NewLine, pageLines),
                font,
                Brushes.Black,
                bounds
            );

            currentLine += pageLines.Length;
            e.HasMorePages = currentLine < lines.Length;
        };

        doc.Print();
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
