using System.Text;
using System.Text.Json;

namespace ZeloImpressao;

// No ticket text, customer data, raw store/order ids or printer names are stored.
// A flushed reservation precedes every spool call. Incomplete attempts stay unknown.
internal sealed class PrintJournal
{
    internal const int RetentionSeconds = 7 * 24 * 60 * 60;
    internal const int MaxEntries = 10000;
    internal const int MaxCapacity = 50000;
    private readonly object _lock = new();
    private readonly string _path;
    private readonly Func<int> _capacity;
    private long _compactionSize = 8 * 1024 * 1024;
    private readonly Dictionary<string, Record> _records = new();
    private Exception? _failure;
    private DateTimeOffset _nextPrune;
    internal sealed record Record(string Key, string Fingerprint, string State, string Source, string Mode, DateTimeOffset CreatedAt);

    public PrintJournal(string directory, Func<int>? capacity = null)
    {
        _path = Path.Combine(directory, "print-history.jsonl");
        _capacity = capacity ?? (() => MaxEntries);
        try
        {
            Directory.CreateDirectory(directory);
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 64 * 1024 * 1024) throw new IOException("Print history file exceeds its safe size limit.");
            var text = File.ReadAllText(_path);
            var endsWithNewline = text.EndsWith('\n');
            var lines = text.Split('\n');
            var truncated = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                Record record;
                try { record = JsonSerializer.Deserialize<Record>(lines[i]) ?? throw new JsonException(); }
                catch (JsonException) when (i == lines.Length - 1 && !endsWithNewline)
                {
                    // A torn final append never replaces an earlier reservation.
                    truncated = true;
                    break;
                }
                Validate(record);
                if (record.State == "released") _records.Remove(record.Key);
                else _records[record.Key] = record;
            }
            Prune();
            if (_records.Count > MaxCapacity) throw new IOException("Print history exceeds capacity.");
            if (truncated || !endsWithNewline || new FileInfo(_path).Length >= _compactionSize) Compact();
        }
        catch (Exception error) { _failure = error; }
    }

    public Record? Find(string key)
    {
        lock (_lock)
        {
            EnsureAvailable();
            Prune();
            return _records.GetValueOrDefault(key);
        }
    }

    public void Reserve(string key, string fingerprint, string source, string mode)
    {
        lock (_lock)
        {
            EnsureAvailable();
            Prune();
            if (_records.Count >= Math.Clamp(_capacity(), MaxEntries, MaxCapacity))
                throw new PrintRequestException("Histórico de impressão cheio. Amplie a capacidade nas configurações locais; novas impressões foram interrompidas para evitar duplicações.", "PRINT_HISTORY_FULL", 503, false);
            Append(new Record(key, fingerprint, "reserved", source, mode, DateTimeOffset.UtcNow));
        }
    }

    public void Complete(string key) { lock (_lock) Append(_records[key] with { State = "spooled" }); }
    public void Release(string key) { lock (_lock) Append(_records[key] with { State = "released" }); }

    private void Append(Record record)
    {
        EnsureAvailable();
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > _compactionSize) Compact();
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            if (record.State == "released") _records.Remove(record.Key);
            else _records[record.Key] = record;
        }
        catch (Exception error)
        {
            _failure = error;
            throw Unavailable(error);
        }
    }

    private void Compact()
    {
        var temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var record in _records.Values)
                stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n"));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _path, overwrite: true);
        _compactionSize = Math.Max(8 * 1024 * 1024, new FileInfo(_path).Length * 2);
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextPrune) return;
        var expiry = now.AddSeconds(-RetentionSeconds);
        foreach (var key in _records.Where(pair => pair.Value.CreatedAt < expiry).Select(pair => pair.Key).ToList())
            _records.Remove(key);
        _nextPrune = now.AddMinutes(1);
    }

    private static void Validate(Record record)
    {
        if (record.Key.Length != 64 || record.Fingerprint.Length != 64 || record.State is not ("reserved" or "spooled" or "released") || record.Source is not ("zelopdv" or "zelochat") || record.Mode is not ("raw" or "driver"))
            throw new JsonException("Invalid print history record.");
    }

    private void EnsureAvailable() { if (_failure is not null) throw Unavailable(_failure); }
    private static PrintRequestException Unavailable(Exception error)
        => new("Não foi possível verificar o histórico de impressão. Confira a saída e os logs antes de emitir outra via.", "PRINT_HISTORY_UNAVAILABLE", 503, false, error);
}
