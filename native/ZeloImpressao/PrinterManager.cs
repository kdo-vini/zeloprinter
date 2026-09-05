using System.Drawing.Printing;
using System.Management;

namespace ZeloImpressao;

internal sealed class PrinterManager
{
    private readonly ConfigStore _configStore;

    public PrinterManager(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public List<PrinterInfo> ListPrinters()
    {
        var result = new List<PrinterInfo>();
        var defaultPrinter = new PrinterSettings().PrinterName;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, Default, WorkOffline, PrinterStatus, PortName, DriverName FROM Win32_Printer");
            using var collection = searcher.Get();
            foreach (ManagementObject row in collection)
            {
                using (row)
                {
                    var name = Convert.ToString(row["Name"]) ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    result.Add(new PrinterInfo
                    {
                        Id = Convert.ToString(row["DeviceID"]) ?? name,
                        Name = name,
                        IsDefault = Convert.ToBoolean(row["Default"] ?? string.Equals(defaultPrinter, name, StringComparison.OrdinalIgnoreCase)),
                        IsOffline = Convert.ToBoolean(row["WorkOffline"] ?? false),
                        Status = NormalizeStatus(row["PrinterStatus"]),
                        PortName = Convert.ToString(row["PortName"]),
                        DriverName = Convert.ToString(row["DriverName"])
                    });
                }
            }
        }
        catch (Exception error)
        {
            _configStore.Log("printer_enumeration_fallback", new { error = error.GetType().Name });
            result.Clear();
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                result.Add(new PrinterInfo
                {
                    Id = name,
                    Name = name,
                    IsDefault = string.Equals(defaultPrinter, name, StringComparison.OrdinalIgnoreCase),
                    IsOffline = false,
                    Status = "unknown"
                });
            }
        }

        return result.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToList();
    }

    public PrinterInfo? ResolvePrinter(string? idOrName)
    {
        return ResolvePrinter(ListPrinters(), _configStore.Get(), idOrName);
    }

    internal static PrinterInfo? ResolvePrinter(List<PrinterInfo> printers, AgentConfig cfg, string? idOrName)
    {
        if (!string.IsNullOrWhiteSpace(idOrName))
        {
            var match = printers.FirstOrDefault(p =>
                string.Equals(p.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase));
            return match;
        }

        if (!string.IsNullOrWhiteSpace(cfg.SelectedPrinterId) || !string.IsNullOrWhiteSpace(cfg.SelectedPrinterName))
        {
            var match = printers.FirstOrDefault(p =>
                string.Equals(p.Id, cfg.SelectedPrinterId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, cfg.SelectedPrinterName, StringComparison.OrdinalIgnoreCase));
            return match;
        }

        return printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
    }

    private static string NormalizeStatus(object? value)
    {
        var status = Convert.ToInt32(value ?? 0);
        return status switch
        {
            3 => "ready",
            4 => "printing",
            5 => "warming_up",
            7 => "offline",
            _ => status > 0 ? $"status_{status}" : "unknown"
        };
    }
}
