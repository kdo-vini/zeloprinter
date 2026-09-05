namespace ZeloImpressao;

internal sealed class ApiConfigView
{
    public string? SelectedPrinterId { get; init; }
    public string? SelectedPrinterName { get; init; }
    public bool StartWithWindows { get; init; }
    public bool RequirePairing { get; init; }
    public bool AutoConnectEnabled { get; init; }
    public string PreferredAutoPrintSource { get; init; } = "zelopdv";
    public int PrintHistoryCapacity { get; init; } = PrintJournal.MaxEntries;
    public List<string> AllowedOrigins { get; init; } = [];
}
