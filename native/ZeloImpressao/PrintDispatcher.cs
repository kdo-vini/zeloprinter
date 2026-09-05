using System.Security.Cryptography;
using System.Text.Json;

namespace ZeloImpressao;

// One spooler submission at a time; automatic identities cross application boundaries.
internal sealed class PrintDispatcher
{
    internal const int MaxPending = 16;
    internal const int MaxRemembered = 1000;
    internal const int PreferenceGraceMs = 1500;
    private readonly Func<PrintJob, PrinterInfo> _print;
    private readonly Func<string> _preferredSource;
    private readonly PrintJournal? _journal;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _spooler = new(1, 1);
    private readonly Dictionary<string, Entry> _jobs = new();
    private int _pending;

    public PrintDispatcher(Func<PrintJob, PrinterInfo> print, Func<string>? preferredSource = null, string? historyDirectory = null, Func<int>? historyCapacity = null)
    {
        _print = print;
        _preferredSource = preferredSource ?? (() => "zelopdv");
        _journal = historyDirectory is null ? null : new PrintJournal(historyDirectory, historyCapacity);
    }

    private sealed class Entry(PrintJob job, string? key, string fingerprint, string preferredSource)
    {
        public PrintJob Job = job;
        public readonly string? Key = key;
        public readonly string Fingerprint = fingerprint;
        public readonly string PreferredSource = preferredSource;
        public readonly DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
        public Guid Winner = Guid.NewGuid();
        public PrintJob? Alternative;
        public Guid AlternativeWinner;
        public bool Started;
        public readonly TaskCompletionSource<bool> PreferredAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<PrintDispatchResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<PrintDispatchResult> SubmitAsync(PrintJob job)
    {
        PrintService.ValidateJob(job);
        var automatic = job.Intent?.Mode == "automatic";
        var identity = automatic
            ? new[] { "automatic", Guid.Parse(job.CompanyStoreId!).ToString("D"), Guid.Parse(job.Intent!.OrderId!).ToString("D"), job.Intent.Purpose }
            : string.IsNullOrWhiteSpace(job.JobId) ? null : new[] { "manual", job.Source, job.CompanyStoreId, job.JobId };
        var key = identity is null ? null : Hash(identity);
        var fingerprint = Hash(new { job.Type, job.PrinterId, job.PrinterName, job.Content });
        lock (_lock)
        {
            var expiry = DateTimeOffset.UtcNow.AddHours(-1);
            foreach (var expired in _jobs.Where(pair => pair.Value.Completion.Task.IsCompleted && pair.Value.CreatedAt < expiry).Select(pair => pair.Key).ToList())
                _jobs.Remove(expired);
            if (key is not null && _jobs.TryGetValue(key, out var existing))
            {
                if (!automatic && existing.Fingerprint != fingerprint)
                    throw new PrintRequestException("O identificador já foi usado para outro conteúdo de impressão.", "JOB_ID_CONFLICT", 409);
                var contender = Guid.NewGuid();
                if (automatic && !existing.Started && job.Source == existing.PreferredSource && existing.Job.Source != existing.PreferredSource)
                {
                    existing.Alternative = existing.Job;
                    existing.AlternativeWinner = existing.Winner;
                    existing.Job = job;
                    existing.Winner = contender;
                    existing.PreferredAvailable.TrySetResult(true);
                }
                else if (automatic && existing.Job.Source != job.Source && existing.Alternative is null)
                {
                    existing.Alternative = job;
                    existing.AlternativeWinner = contender;
                }
                return ResponseAsync(existing, contender);
            }
            if (key is not null && _journal?.Find(key) is { } remembered)
            {
                if (!automatic && remembered.Fingerprint != fingerprint)
                    throw new PrintRequestException("O identificador já foi usado para outro conteúdo de impressão.", "JOB_ID_CONFLICT", 409);
                if (remembered.State != "spooled")
                    throw new PrintRequestException("O pedido pode ter sido impresso antes de a conexão terminar. Confira a saída antes de emitir outra via.", "PRINT_OUTCOME_UNKNOWN", 503, false);
                return Task.FromResult(new PrintDispatchResult(null, remembered.Source, remembered.Mode, true));
            }
            if (_pending >= MaxPending)
                throw new PrintRequestException("A fila de impressão está cheia. Aguarde antes de enviar novamente.", "PRINT_QUEUE_FULL", 503);
            if (key is not null && _jobs.Count >= MaxRemembered)
            {
                var oldest = _jobs.Where(pair => pair.Value.Completion.Task.IsCompleted).MinBy(pair => pair.Value.CreatedAt);
                _jobs.Remove(oldest.Key);
            }
            var entry = new Entry(job, key, fingerprint, _preferredSource());
            _pending++;
            if (key is not null) _jobs.Add(key, entry);
            _ = Task.Run(() => ExecuteAsync(entry));
            return ResponseAsync(entry, entry.Winner);
        }
    }

    private static async Task<PrintDispatchResult> ResponseAsync(Entry entry, Guid caller)
        => (await entry.Completion.Task.ConfigureAwait(false)) with { Duplicate = entry.Winner != caller };

    private async Task ExecuteAsync(Entry entry)
    {
        if (entry.Job.Intent?.Mode == "automatic" && entry.Job.Source != entry.PreferredSource)
            await Task.WhenAny(Task.Delay(PreferenceGraceMs), entry.PreferredAvailable.Task).ConfigureAwait(false);
        await _spooler.WaitAsync().ConfigureAwait(false);
        var reserved = false;
        try
        {
            var switched = false;
            while (true)
            {
                lock (_lock) entry.Started = true;
                var job = entry.Job;
                var mode = job.Content.Format == "raw_escpos_base64" ? "raw" : "driver";
                if (entry.Key is not null && _journal is not null)
                {
                    _journal.Reserve(entry.Key, entry.Fingerprint, job.Source, mode);
                    reserved = true;
                }
                try
                {
                    var printer = _print(job);
                    if (reserved) _journal!.Complete(entry.Key!);
                    entry.Completion.TrySetResult(new PrintDispatchResult(printer, job.Source, mode));
                    break;
                }
                catch (PrintRequestException error) when (error.RetrySafe)
                {
                    if (reserved) { _journal!.Release(entry.Key!); reserved = false; }
                    lock (_lock)
                    {
                        if (!switched && entry.Alternative is not null)
                        {
                            entry.Job = entry.Alternative;
                            entry.Winner = entry.AlternativeWinner;
                            entry.Alternative = null;
                            switched = true;
                            continue;
                        }
                    }
                    throw;
                }
            }
        }
        catch (Exception error)
        {
            var outcome = error;
            var safe = error is PrintRequestException { RetrySafe: true };
            if (safe && reserved)
            {
                try { _journal!.Release(entry.Key!); }
                catch (Exception journalError) { outcome = journalError; safe = false; }
            }
            if ((safe || (_journal is not null && !reserved)) && entry.Key is not null)
                lock (_lock) _jobs.Remove(entry.Key);
            entry.Completion.TrySetException(outcome);
        }
        finally
        {
            _spooler.Release();
            lock (_lock) _pending--;
        }
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
