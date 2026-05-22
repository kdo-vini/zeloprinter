namespace ZeloImpressao;

internal sealed class ApiConfigView
{
    public string? SelectedPrinterId { get; init; }
    public string? SelectedPrinterName { get; init; }
    public bool StartWithWindows { get; init; }
    public bool RequirePairing { get; init; }
    public List<string> AllowedOrigins { get; init; } = [];
}
