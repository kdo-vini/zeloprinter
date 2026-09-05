using System.Text.Json.Serialization;

namespace ZeloImpressao;

internal sealed class AgentConfig
{
    public string? SelectedPrinterId { get; set; }
    public string? SelectedPrinterName { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool RequirePairing { get; set; } = true;
    public bool AutoConnectEnabled { get; set; } = true;
    public string PreferredAutoPrintSource { get; set; } = "zelopdv";
    public int PrintHistoryCapacity { get; set; } = PrintJournal.MaxEntries;
    public string? TokenHash { get; set; }
    public List<string> TokenHashes { get; set; } = [];
    public List<string> AllowedOrigins { get; set; } = AppConstants.DefaultAllowedOrigins.ToList();
}

internal sealed class PrinterInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsOffline { get; set; }
    public string Status { get; set; } = "unknown";
    public string? DriverName { get; set; }
    public string? PortName { get; set; }

    public override string ToString()
    {
        var suffix = IsDefault ? " (padrão)" : "";
        var offline = IsOffline ? " - offline" : "";
        return $"{Name}{suffix}{offline}";
    }
}

internal sealed class PrintJob
{
    public string? JobId { get; set; }
    public string Source { get; set; } = "";
    public string? CompanyStoreId { get; set; }
    public string Type { get; set; } = "";
    public string? PrinterId { get; set; }
    public string? PrinterName { get; set; }
    public string Timestamp { get; set; } = "";
    public PrintContent Content { get; set; } = new();
    public PrintIntent? Intent { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
}

internal sealed class PrintIntent
{
    public string Mode { get; set; } = "manual";
    public string? OrderId { get; set; }
    public string? Purpose { get; set; }
}

internal sealed record PrintDispatchResult(PrinterInfo? Printer, string Source, string Mode, bool Duplicate = false);

internal sealed class PrintContent
{
    public string Format { get; set; } = "";
    public string? Text { get; set; }
    public string? Html { get; set; }
    public string? Base64 { get; set; }
}

internal sealed class ConfigPatch
{
    public string? SelectedPrinterId { get; set; }
    public string? SelectedPrinterName { get; set; }
    public bool? StartWithWindows { get; set; }
    public bool? RequirePairing { get; set; }
    public bool? AutoConnectEnabled { get; set; }
    public string? PreferredAutoPrintSource { get; set; }
    public int? PrintHistoryCapacity { get; set; }
}

internal sealed class PairRequest
{
    public string? Code { get; set; }
}

internal sealed class TestPrintRequest
{
    public string? PrinterId { get; set; }
}

internal sealed class ApiError
{
    public bool Ok { get; init; } = false;
    public string Message { get; init; } = "";
    public string? Code { get; init; }
    public bool RetrySafe { get; init; } = true;
}

internal sealed class PrintRequestException(string message, string code, int statusCode = 400, bool retrySafe = true, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public bool RetrySafe { get; } = retrySafe;
}
