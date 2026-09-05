using ZeloImpressao;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Drawing;

Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "None");

var failures = 0;
var tests = 0;
async Task Check(string name, Func<Task> test)
{
    tests++;
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
}
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
string TempDirectory() => Path.Combine(Path.GetTempPath(), "zelo-impressao-tests", Guid.NewGuid().ToString("N"));

await Check("pairing PDV then Chat preserves both browser tokens after restart", () =>
{
    var directory = TempDirectory();
    var config = new ConfigStore(directory);
    var pdv = config.IssueToken();
    var chat = config.IssueToken();
    var reloaded = new ConfigStore(directory);
    Assert(reloaded.VerifyToken(pdv), "Pairing Chat revoked PDV token");
    Assert(reloaded.VerifyToken(chat), "Chat token invalid");
    Assert(!reloaded.VerifyToken("invalid"), "Invalid token accepted");
    return Task.CompletedTask;
});

await Check("legacy token migrates and local revocation invalidates all tokens", () =>
{
    var directory = TempDirectory();
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "config.json"), JsonSerializer.Serialize(new AgentConfig { TokenHash = ConfigStore.HashToken("legacy") }));
    var config = new ConfigStore(directory);
    Assert(config.VerifyToken("legacy"), "Legacy token lost");
    var second = config.IssueToken();
    config.RevokeTokens();
    Assert(!config.VerifyToken("legacy") && !config.VerifyToken(second), "Revocation did not invalidate all");
    return Task.CompletedTask;
});

await Check("browser token limit never silently revokes existing browsers", () =>
{
    var config = new ConfigStore(TempDirectory());
    var first = config.IssueToken();
    for (var i = 1; i < ConfigStore.MaxPairedBrowsers; i++) config.IssueToken();
    try { config.IssueToken(); throw new Exception("Limit not enforced"); }
    catch (PrintRequestException error) { Assert(error.Code == "PAIRING_LIMIT", "Wrong error"); }
    Assert(config.VerifyToken(first), "Oldest token revoked");
    return Task.CompletedTask;
});

await Check("pairing code is single use under concurrency and rate limited", async () =>
{
    var pairing = new PairingService(new ConfigStore(TempDirectory()));
    var code = pairing.GetCode().Code;
    var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => pairing.Confirm(code))));
    Assert(tokens.Count(token => token is not null) == 1, "Code was used multiple times");
    code = pairing.GetCode(true).Code;
    for (var i = 0; i < 5; i++) pairing.Confirm("invalid");
    Assert(pairing.Confirm(code) is null, "Five failed attempts did not lock code");
    code = pairing.GetCode(true).Code;
    Assert(pairing.Confirm(code) is not null, "Local renewal did not unlock code");
});

await Check("v0.1.4 token list migrates without silently discarding browsers", () =>
{
    var directory = TempDirectory();
    Directory.CreateDirectory(directory);
    var tokens = Enumerable.Range(0, 50).Select(i => "browser-" + i).ToArray();
    var hashes = tokens.Select(ConfigStore.HashToken).ToList();
    hashes.Add(hashes[0].ToUpperInvariant());
    File.WriteAllText(Path.Combine(directory, "config.json"), JsonSerializer.Serialize(new AgentConfig { TokenHashes = hashes }));
    var config = new ConfigStore(directory);
    Assert(config.Get().TokenHashes.Count == 50 && tokens.All(config.VerifyToken), "Published browser credentials lost during migration");
    try { config.IssueToken(); throw new Exception("Limit not enforced"); }
    catch (PrintRequestException error) { Assert(error.Code == "PAIRING_LIMIT", "Wrong error"); }
    return Task.CompletedTask;
});

PrintJob Job(string? id = "job-1", string text = "Cupom") => new()
{
    JobId = id, Source = "zelopdv", CompanyStoreId = "store-1", Type = "receipt",
    Content = new PrintContent { Format = "text", Text = text }
};

PrintJob AutomaticJob(string source, string id = "intent-1", string text = "Cupom") => JsonSerializer.Deserialize<PrintJob>(JsonSerializer.Serialize(new
{
    jobId = id, source, companyStoreId = "11111111-1111-4111-8111-111111111111", type = "receipt",
    intent = new { mode = "automatic", orderId = "22222222-2222-4222-8222-222222222222", purpose = "order_ticket" },
    content = new { format = "text", text }
}), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

await Check("automatic order is shared by PDV and Chat regardless of rendering or job id", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { count++; return new PrinterInfo(); });
    await dispatcher.SubmitAsync(AutomaticJob("zelopdv"));
    await dispatcher.SubmitAsync(AutomaticJob("zelochat", "chat-job", "Outra renderização"));
    Assert(count == 1, "The same order was submitted by both applications");
});

await Check("PDV takes ownership when Chat arrives first inside the grace window", async () =>
{
    var sources = new List<string>();
    var dispatcher = new PrintDispatcher(job => { sources.Add(job.Source); return new PrinterInfo(); });
    var chat = dispatcher.SubmitAsync(AutomaticJob("zelochat"));
    await Task.Delay(50);
    var pdv = dispatcher.SubmitAsync(AutomaticJob("zelopdv", "pdv-job"));
    await Task.WhenAll(chat, pdv);
    Assert(sources.SequenceEqual(new[] { "zelopdv" }), "Chat printed before the preferred application could claim the order");
});

await Check("one safe preferred refusal falls back to waiting Chat and records the real winner", async () =>
{
    var sources = new List<string>();
    var dispatcher = new PrintDispatcher(job =>
    {
        sources.Add(job.Source);
        if (job.Source == "zelopdv") throw new PrintRequestException("offline", "PRINTER_UNAVAILABLE", 503);
        return new PrinterInfo { Name = "Chat device" };
    }, historyDirectory: TempDirectory());
    var chat = dispatcher.SubmitAsync(AutomaticJob("zelochat"));
    var pdv = dispatcher.SubmitAsync(AutomaticJob("zelopdv", "pdv"));
    var duplicate = dispatcher.SubmitAsync(AutomaticJob("zelopdv", "pdv-tab-2"));
    var results = await Task.WhenAll(chat, pdv, duplicate);
    Assert(sources.SequenceEqual(new[] { "zelopdv", "zelochat" }), "Fallback did not use precisely one safe alternative");
    Assert(results.All(result => result.Source == "zelochat") && !results[0].Duplicate && results[1].Duplicate && results[2].Duplicate, "Request outcomes do not identify the actual winner");
});

await Check("manual second copy and another store remain independent from automatic order", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { count++; return new PrinterInfo(); });
    await dispatcher.SubmitAsync(AutomaticJob("zelopdv"));
    var other = AutomaticJob("zelopdv"); other.CompanyStoreId = "33333333-3333-4333-8333-333333333333";
    await dispatcher.SubmitAsync(other);
    var manual = AutomaticJob("zelopdv", "second-copy"); manual.Intent!.Mode = "manual";
    await dispatcher.SubmitAsync(manual);
    await dispatcher.SubmitAsync(manual);
    Assert(count == 3, "Manual copy/store isolation or retry deduplication failed");
});

await Check("accepted automatic order survives dispatcher restart without storing ticket content", async () =>
{
    var directory = TempDirectory();
    var count = 0;
    PrinterInfo Print(PrintJob _) { count++; return new PrinterInfo { Name = "Private printer" }; }
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(AutomaticJob("zelopdv", text: "Customer private note"));
    var replay = await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(AutomaticJob("zelochat", "retry"));
    Assert(count == 1 && replay.Duplicate, "Restart forgot accepted automatic order");
    var persisted = string.Join("", Directory.GetFiles(directory).Select(File.ReadAllText));
    Assert(!persisted.Contains("Customer") && !persisted.Contains("Private printer") && !persisted.Contains("11111111-1111") && !persisted.Contains("22222222-2222"), "History contains private ticket/identity data");
});

await Check("reservation is durable before spool and unfinished reservation blocks after restart", async () =>
{
    var directory = TempDirectory();
    var count = 0;
    var original = new PrintDispatcher(_ =>
    {
        count++;
        var restarted = new PrintDispatcher(_ => { count++; return new PrinterInfo(); }, historyDirectory: directory);
        try { restarted.SubmitAsync(AutomaticJob("zelopdv")).GetAwaiter().GetResult(); throw new Exception("Unfinished reservation was resubmitted"); }
        catch (PrintRequestException error) { Assert(error.Code == "PRINT_OUTCOME_UNKNOWN" && !error.RetrySafe, "Restart incorrectly allowed retry"); }
        throw new PrintRequestException("response lost", "PRINT_OUTCOME_UNKNOWN", 503, false);
    }, historyDirectory: directory);
    try { await original.SubmitAsync(AutomaticJob("zelopdv")); }
    catch (PrintRequestException error) { Assert(!error.RetrySafe, "Uncertain result became safe"); }
    Assert(count == 1, "Spooler called again after restart");
});

await Check("safe refusal releases durable reservation while manual retry remains idempotent", async () =>
{
    var directory = TempDirectory();
    var offline = new PrintDispatcher(_ => throw new PrintRequestException("offline", "PRINTER_UNAVAILABLE", 503), historyDirectory: directory);
    try { await offline.SubmitAsync(AutomaticJob("zelopdv")); } catch (PrintRequestException) { }
    var count = 0;
    PrinterInfo Print(PrintJob _) { count++; return new PrinterInfo(); }
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(AutomaticJob("zelopdv"));
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(Job("manual-copy"));
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(Job("manual-copy"));
    Assert(count == 2, "Safe recovery or durable manual retry failed");
});

await Check("Chat fallback waits for preference and configurable Chat priority survives config reload", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { count++; return new PrinterInfo(); });
    var pending = dispatcher.SubmitAsync(AutomaticJob("zelochat"));
    await Task.Delay(50);
    Assert(count == 0, "Non-preferred source skipped the grace window");
    Assert((await pending).Source == "zelochat" && count == 1, "Absent preferred source blocked fallback");
    var directory = TempDirectory();
    var config = new ConfigStore(directory);
    config.Update(new ConfigPatch { PreferredAutoPrintSource = "zelochat" });
    var reloaded = new ConfigStore(directory);
    var sources = new List<string>();
    var reverse = new PrintDispatcher(job => { sources.Add(job.Source); return new PrinterInfo(); }, () => reloaded.Get().PreferredAutoPrintSource);
    var pdv = reverse.SubmitAsync(AutomaticJob("zelopdv"));
    var chat = reverse.SubmitAsync(AutomaticJob("zelochat"));
    await Task.WhenAll(pdv, chat);
    Assert(sources.SequenceEqual(new[] { "zelochat" }), "Configured preference was ignored");
});

await Check("uncertain preferred submission never falls back to another queued rendering", async () =>
{
    var calls = 0;
    var dispatcher = new PrintDispatcher(_ => { calls++; throw new PrintRequestException("unknown", "PRINT_OUTCOME_UNKNOWN", 503, false); }, historyDirectory: TempDirectory());
    var pending = new[] {
        dispatcher.SubmitAsync(AutomaticJob("zelochat")), dispatcher.SubmitAsync(AutomaticJob("zelopdv", "pdv")),
        dispatcher.SubmitAsync(AutomaticJob("zelochat", "other-tab", "Different receipt"))
    };
    foreach (var task in pending)
    {
        try { await task; throw new Exception("Uncertain print reported success"); }
        catch (PrintRequestException error) { Assert(!error.RetrySafe, "Uncertain result became safe"); }
    }
    Assert(calls == 1, "Unknown outcome triggered another spool submission");
});

await Check("torn final journal append preserves earlier uncertain reservation", async () =>
{
    var directory = TempDirectory();
    var first = new PrintDispatcher(_ => throw new PrintRequestException("unknown", "PRINT_OUTCOME_UNKNOWN", 503, false), historyDirectory: directory);
    try { await first.SubmitAsync(AutomaticJob("zelopdv")); } catch (PrintRequestException) { }
    File.AppendAllText(Path.Combine(directory, "print-history.jsonl"), "{\"unfinished");
    var count = 0;
    var restarted = new PrintDispatcher(_ => { count++; return new PrinterInfo(); }, historyDirectory: directory);
    try { await restarted.SubmitAsync(AutomaticJob("zelopdv")); throw new Exception("Lost reservation"); }
    catch (PrintRequestException error) { Assert(error.Code == "PRINT_OUTCOME_UNKNOWN", "Incomplete append lost valid prior history"); }
    Assert(count == 0, "Torn append triggered printing");
});

await Check("corrupt history or failed durable write blocks before the spooler", async () =>
{
    foreach (var corrupt in new[] { true, false })
    {
        var directory = TempDirectory(); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "print-history.jsonl");
        if (corrupt) File.WriteAllText(path, "invalid record\n"); else Directory.CreateDirectory(path);
        var count = 0;
        var dispatcher = new PrintDispatcher(_ => { count++; return new PrinterInfo(); }, historyDirectory: directory);
        try { await dispatcher.SubmitAsync(AutomaticJob("zelopdv")); throw new Exception("Unsafe history accepted"); }
        catch (PrintRequestException error) { Assert(error.Code == "PRINT_HISTORY_UNAVAILABLE" && !error.RetrySafe, "History failure misclassified"); }
        Assert(count == 0, "Job printed before a durable reservation was possible");
    }
});

await Check("full durable history retains prior dedupe and refuses new automatic orders", async () =>
{
    var directory = TempDirectory();
    var count = 0;
    PrinterInfo Print(PrintJob _) { count++; return new PrinterInfo(); }
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(AutomaticJob("zelopdv"));
    var extra = new StringBuilder();
    for (var i = 0; i < PrintJournal.MaxEntries - 1; i++)
        extra.AppendLine(JsonSerializer.Serialize(new PrintJournal.Record(i.ToString("x64"), new string('A', 64), "spooled", "zelopdv", "driver", DateTimeOffset.UtcNow)));
    File.AppendAllText(Path.Combine(directory, "print-history.jsonl"), extra.ToString());
    var capacity = 10000;
    var restarted = new PrintDispatcher(Print, historyDirectory: directory, historyCapacity: () => capacity);
    Assert((await restarted.SubmitAsync(AutomaticJob("zelochat"))).Duplicate, "Full history evicted existing protection");
    var another = AutomaticJob("zelopdv"); another.Intent!.OrderId = "44444444-4444-4444-8444-444444444444";
    try { await restarted.SubmitAsync(another); throw new Exception("Full history silently evicted a job"); }
    catch (PrintRequestException error) { Assert(error.Code == "PRINT_HISTORY_FULL", "Wrong full-history error"); }
    Assert(count == 1, "History overflow reached spooler");
    capacity = 20000;
    await restarted.SubmitAsync(another);
    Assert((await restarted.SubmitAsync(AutomaticJob("zelopdv"))).Duplicate && count == 2, "Expanding capacity lost old dedupe or did not recover new printing");
});

await Check("expired persisted history releases capacity only after its retention window", async () =>
{
    var directory = TempDirectory(); var count = 0;
    PrinterInfo Print(PrintJob _) { count++; return new PrinterInfo(); }
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(AutomaticJob("zelopdv"));
    var path = Path.Combine(directory, "print-history.jsonl");
    var records = File.ReadAllLines(path).Select(line => JsonSerializer.Deserialize<PrintJournal.Record>(line)! with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-8) });
    File.WriteAllLines(path, records.Select(record => JsonSerializer.Serialize(record)));
    await new PrintDispatcher(Print, historyDirectory: directory).SubmitAsync(AutomaticJob("zelopdv"));
    Assert(count == 2, "Expired history blocked a new print indefinitely");
});

await Check("concurrent duplicate submissions invoke spooler once and reject changed content", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { Interlocked.Increment(ref count); return new PrinterInfo { Name = "fake" }; });
    var tasks = Enumerable.Range(0, 20).Select(_ => dispatcher.SubmitAsync(Job())).ToArray();
    await Task.WhenAll(tasks);
    Assert(count == 1, $"Spooler invoked {count} times");
    var replay = Job(); replay.Timestamp = "new timestamp";
    await dispatcher.SubmitAsync(replay);
    Assert(count == 1, "Timestamp broke deduplication");
    try { await dispatcher.SubmitAsync(Job(text: "Changed")); throw new Exception("Conflict accepted"); }
    catch (PrintRequestException error) { Assert(error.Code == "JOB_ID_CONFLICT", "Wrong conflict"); }
    var otherStore = Job(); otherStore.CompanyStoreId = "store-2";
    await dispatcher.SubmitAsync(otherStore);
    Assert(count == 2, "Other store's independent print suppressed");
});

await Check("uncertain native failure is remembered without retrying spooler", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { count++; throw new PrintRequestException("unknown", "PRINT_OUTCOME_UNKNOWN", 503, false); });
    for (var i = 0; i < 2; i++)
    {
        try { await dispatcher.SubmitAsync(Job()); throw new Exception("Failure swallowed"); }
        catch (PrintRequestException error) { Assert(!error.RetrySafe, "Uncertain outcome became retry-safe"); }
    }
    Assert(count == 1, "Uncertain job retried");
});

await Check("unexpected delegate exception is retained to avoid a second submission", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { count++; throw new IOException("lost spooler response"); });
    for (var i = 0; i < 2; i++)
    {
        try { await dispatcher.SubmitAsync(Job()); throw new Exception("Failure swallowed"); }
        catch (IOException) { }
    }
    Assert(count == 1, "Unexpected exception caused second submission");
});

await Check("safe refusal permits same job id after printer recovery", async () =>
{
    var attempts = 0;
    var dispatcher = new PrintDispatcher(_ =>
    {
        if (++attempts == 1) throw new PrintRequestException("offline", "PRINTER_UNAVAILABLE", 503);
        return new PrinterInfo();
    });
    try { await dispatcher.SubmitAsync(Job()); } catch (PrintRequestException) { }
    await dispatcher.SubmitAsync(Job());
    Assert(attempts == 2, "Safe refusal cached forever");
});

await Check("dedup cache stays bounded without stopping throughput", async () =>
{
    var count = 0;
    var dispatcher = new PrintDispatcher(_ => { count++; return new PrinterInfo(); });
    for (var i = 0; i <= PrintDispatcher.MaxRemembered; i++) await dispatcher.SubmitAsync(Job($"job-{i}"));
    Assert(count == PrintDispatcher.MaxRemembered + 1, "Cache capacity stopped new prints");
    await dispatcher.SubmitAsync(Job($"job-{PrintDispatcher.MaxRemembered}"));
    Assert(count == PrintDispatcher.MaxRemembered + 1, "Newest print not remembered");
});

await Check("spooler concurrency is one and queue refuses excess before submission", async () =>
{
    using var release = new ManualResetEventSlim();
    var active = 0; var maximum = 0; var printed = 0;
    var dispatcher = new PrintDispatcher(_ =>
    {
        maximum = Math.Max(maximum, Interlocked.Increment(ref active));
        release.Wait(TimeSpan.FromSeconds(10));
        Interlocked.Decrement(ref active); Interlocked.Increment(ref printed);
        return new PrinterInfo();
    });
    var tasks = Enumerable.Range(0, PrintDispatcher.MaxPending).Select(i => dispatcher.SubmitAsync(Job($"job-{i}"))).ToArray();
    try
    {
        try { await dispatcher.SubmitAsync(Job("overflow")); throw new Exception("Queue unbounded"); }
        catch (PrintRequestException error) { Assert(error.Code == "PRINT_QUEUE_FULL" && error.RetrySafe, "Wrong overflow response"); }
    }
    finally { release.Set(); await Task.WhenAll(tasks); }
    Assert(maximum == 1 && printed == PrintDispatcher.MaxPending, "Concurrent or dropped spooler calls");
});

await Check("missing selected or requested printer never falls back to another device", () =>
{
    var printers = new List<PrinterInfo> { new() { Id = "pdf", Name = "PDF", IsDefault = true } };
    Assert(PrinterManager.ResolvePrinter(printers, new AgentConfig(), "missing") is null, "Explicit printer fell back");
    Assert(PrinterManager.ResolvePrinter(printers, new AgentConfig { SelectedPrinterId = "missing" }, null) is null, "Saved printer fell back");
    Assert(PrinterManager.ResolvePrinter(printers, new AgentConfig(), null)?.Id == "pdf", "Default discovery broken");
    return Task.CompletedTask;
});

await Check("driver pagination preserves wrapped text beyond a page", () =>
{
    using var bitmap = new Bitmap(220, 120);
    using var graphics = Graphics.FromImage(bitmap);
    using var font = new Font("Consolas", 9f);
    var original = string.Join(" ", Enumerable.Repeat("Pedido comprido com observação", 100));
    var remaining = original;
    var pages = 0; var printed = 0;
    while (remaining.Length > 0)
    {
        var fitted = PrintService.DrawTextPage(graphics, font, new Rectangle(0, 0, 220, 100), remaining);
        Assert(fitted > 0 && fitted <= remaining.Length, "Pagination does not advance");
        printed += fitted; remaining = remaining[fitted..]; pages++;
        Assert(pages < 100, "Pagination loops");
    }
    Assert(pages > 1 && printed == original.Length, "Wrapped text was truncated");
    return Task.CompletedTask;
});

await Check("failed API startup remains stopped and can recover", async () =>
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var config = new ConfigStore(TempDirectory());
    var printers = new PrinterManager(config);
    var api = new LocalApiServer(config, new PairingService(config), printers, new PrintService(printers, config), $"http://127.0.0.1:{port}");
    try
    {
        try { await api.StartAsync(); throw new Exception("Port collision accepted"); }
        catch (IOException) { }
        Assert(!api.IsRunning, "Failed server reports running");
    }
    finally { listener.Stop(); }
    await api.StartAsync();
    Assert(api.IsRunning, "Failed server cannot restart");
    await api.StopAsync();
});

await Check("API validates auth, origins, payloads, chunked size and hides token hashes", async () =>
{
    var config = new ConfigStore(TempDirectory());
    var token = config.IssueToken();
    var printers = new PrinterManager(config);
    var api = new LocalApiServer(config, new PairingService(config), printers, new PrintService(printers, config), "http://127.0.0.1:0");
    await api.StartAsync();
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(api.ListeningUrl!), Timeout = TimeSpan.FromSeconds(10) };
        Assert((await client.GetAsync("/health")).IsSuccessStatusCode, "Health unavailable");
        Assert((await client.GetAsync("/config")).StatusCode == HttpStatusCode.Unauthorized, "Config unprotected");
        client.DefaultRequestHeaders.Add("X-Zelo-Impressao-Token", token);
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        Assert((await client.GetAsync("/config")).StatusCode == HttpStatusCode.Forbidden, "Untrusted origin accepted");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://zelopdv.com.br");
        using var configResponse = await client.PostAsJsonAsync("/config", new { selectedPrinterId = "test-missing" });
        var configJson = await configResponse.Content.ReadAsStringAsync();
        Assert(configResponse.IsSuccessStatusCode && !configJson.Contains("tokenHash", StringComparison.OrdinalIgnoreCase), "Configuration leaks token hash");
        Assert(configResponse.Headers.GetValues("Access-Control-Allow-Origin").Single() == "https://zelopdv.com.br", "Allowed origin not reflected");
        using var securityResponse = await client.PostAsJsonAsync("/config", new { requirePairing = false });
        Assert(securityResponse.StatusCode == HttpStatusCode.Forbidden, "Web client disabled auth");
        Assert(securityResponse.Headers.Contains("Access-Control-Allow-Origin"), "Errors lose CORS and hide safe refusal from browser");
        foreach (var payload in new[] { "{", "null", "{}", "{\"source\":\"zelopdv\",\"type\":\"receipt\",\"content\":null}", "{\"source\":\"zelopdv\",\"type\":\"raw_escpos\",\"content\":{\"format\":\"raw_escpos_base64\",\"base64\":\"not base64\"}}" })
            Assert((await client.PostAsync("/print", new StringContent(payload, Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "Invalid payload accepted or misclassified");
        var bytes = Encoding.UTF8.GetBytes("{\"code\":\"" + new string('x', AppConstants.MaxJsonBytes) + "\"}");
        using var oversized = new HttpRequestMessage(HttpMethod.Post, "/pair") { Content = new StreamContent(new MemoryStream(bytes)) };
        oversized.Headers.TransferEncodingChunked = true;
        oversized.Content.Headers.ContentType = new("application/json");
        Assert((await client.SendAsync(oversized)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "Chunked body bypassed 512KB limit");
    }
    finally { await api.StopAsync(); }
    Assert(!api.IsRunning, "API state still running after stop");
});

await Check("automatic connection needs a trusted origin independent from CORS and preserves tokens", async () =>
{
    var directory = TempDirectory();
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "config.json"), JsonSerializer.Serialize(new AgentConfig
    {
        AllowedOrigins = AppConstants.DefaultAllowedOrigins.Append("https://cors-only.example").ToList()
    }));
    var config = new ConfigStore(directory);
    var manager = new PrinterManager(config);
    var api = new LocalApiServer(config, new PairingService(config), manager, new PrintService(manager, config), "http://127.0.0.1:0");
    await api.StartAsync();
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(api.ListeningUrl!) };
        Assert((await client.PostAsync("/connect", null)).StatusCode == HttpStatusCode.Forbidden, "Missing origin minted token");
        client.DefaultRequestHeaders.Add("Origin", "https://cors-only.example");
        Assert((await client.PostAsync("/connect", null)).StatusCode == HttpStatusCode.Forbidden, "CORS alone grants token minting");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://zelopdv.com.br");
        var first = await (await client.PostAsync("/connect", null)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsync("/connect", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert(config.VerifyToken(first.GetProperty("token").GetString()) && config.VerifyToken(second.GetProperty("token").GetString()), "Automatic connection revoked an existing browser");
        for (var i = 2; i < ConfigStore.MaxPairedBrowsers; i++) config.IssueToken();
        Assert((await client.PostAsync("/connect", null)).StatusCode == HttpStatusCode.Conflict, "Connect bypasses token capacity");
        Assert(config.VerifyToken(first.GetProperty("token").GetString()), "Full capacity silently revoked token");
    }
    finally { await api.StopAsync(); }
});

await Check("local revocation blocks automatic reconnect after restart but explicit pairing remains available", async () =>
{
    var directory = TempDirectory();
    var before = new ConfigStore(directory);
    var oldToken = before.IssueToken();
    before.RevokeTokens();
    var config = new ConfigStore(directory);
    var pairing = new PairingService(config);
    var manager = new PrinterManager(config);
    var api = new LocalApiServer(config, pairing, manager, new PrintService(manager, config), "http://127.0.0.1:0");
    await api.StartAsync();
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(api.ListeningUrl!) };
        client.DefaultRequestHeaders.Add("Origin", "https://zelopdv.com.br");
        Assert(!config.VerifyToken(oldToken), "Revoked token survived restart");
        using var rejected = await client.PostAsync("/connect", null);
        Assert(rejected.StatusCode == HttpStatusCode.Forbidden, "Revoked browser immediately reauthorized itself");
        var paired = await (await client.PostAsJsonAsync("/pair", new { code = pairing.GetCode().Code })).Content.ReadFromJsonAsync<JsonElement>();
        Assert(config.VerifyToken(paired.GetProperty("token").GetString()), "Explicit pairing stopped working after revocation");
        client.DefaultRequestHeaders.Add("X-Zelo-Impressao-Token", paired.GetProperty("token").GetString());
        Assert((await client.PostAsJsonAsync("/config", new { autoConnectEnabled = true })).StatusCode == HttpStatusCode.Forbidden, "Browser reenabled automatic authorization remotely");
        Assert((await client.PostAsync("/connect", null)).StatusCode == HttpStatusCode.Forbidden, "Explicit pairing silently reenabled automatic connection");
        config.Update(new ConfigPatch { AutoConnectEnabled = true });
        Assert((await client.PostAsync("/connect", null)).IsSuccessStatusCode, "Explicit local reactivation failed");
    }
    finally { await api.StopAsync(); }
});

await Check("second launch IPC requests settings and cancels idle listener without a window", async () =>
{
    var pipe = "zelo-impressao-test-" + Guid.NewGuid().ToString("N");
    using var stopped = new CancellationTokenSource();
    var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    Exception? failure = null;
    var listener = InstanceSignal.ListenAsync(pipe, () => received.TrySetResult(true), error => failure = error, stopped.Token);
    await InstanceSignal.NotifyAsync(pipe);
    await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
    stopped.Cancel();
    await listener.WaitAsync(TimeSpan.FromSeconds(3));
    Assert(failure is null, "IPC failed or could not shut down");
});

await Check("HTTP arbitration returns the winner and replay survives a new native service", async () =>
{
    var config = new ConfigStore(TempDirectory()); var token = config.IssueToken();
    var manager = new PrinterManager(config); var calls = 0;
    PrinterInfo Print(PrintJob _) { calls++; return new PrinterInfo { Id = "fake", Name = "Fake device" }; }
    for (var run = 0; run < 2; run++)
    {
        var api = new LocalApiServer(config, new PairingService(config), manager, new PrintService(manager, config, Print), "http://127.0.0.1:0");
        await api.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(api.ListeningUrl!) };
            var health = await client.GetFromJsonAsync<JsonElement>("/health");
            Assert(health.GetProperty("capabilities").GetProperty("canonicalAutoPrint").GetBoolean(), "Automatic capability missing");
            Assert((await client.PostAsJsonAsync("/config", new { preferredAutoPrintSource = "zelochat" })).StatusCode == HttpStatusCode.Unauthorized, "Preference changed without authentication");
            client.DefaultRequestHeaders.Add("X-Zelo-Impressao-Token", token);
            var chatTask = client.PostAsJsonAsync("/print", AutomaticJob("zelochat", "chat"));
            await Task.Delay(50);
            using var pdvResponse = await client.PostAsJsonAsync("/print", AutomaticJob("zelopdv", "pdv", "PDV rendering"));
            using var chatResponse = await chatTask;
            var pdv = await pdvResponse.Content.ReadFromJsonAsync<JsonElement>();
            var chat = await chatResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert(pdvResponse.IsSuccessStatusCode && chatResponse.IsSuccessStatusCode, "HTTP arbitration returned an error");
            Assert(chat.GetProperty("status").GetString() == "deduplicated" && chat.GetProperty("arbitration").GetProperty("source").GetString() == "zelopdv", "Chat reply lost winner metadata");
            Assert(pdv.GetProperty("status").GetString() == (run == 0 ? "spooled" : "deduplicated"), "Restart replay status incorrect");
            if (run == 1) Assert(pdv.GetProperty("printer").ValueKind == JsonValueKind.Null, "Persistent replay exposed printer metadata");
        }
        finally { await api.StopAsync(); }
    }
    Assert(calls == 1, "HTTP concurrency or restart resubmitted the ticket");
});

await Check("aborting HTTP after reservation does not cancel or repeat the accepted native attempt", async () =>
{
    var config = new ConfigStore(TempDirectory()); var token = config.IssueToken();
    var manager = new PrinterManager(config); var calls = 0;
    var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var release = new ManualResetEventSlim();
    var api = new LocalApiServer(config, new PairingService(config), manager, new PrintService(manager, config, _ =>
    {
        calls++; started.TrySetResult(true); release.Wait(TimeSpan.FromSeconds(5)); return new PrinterInfo();
    }), "http://127.0.0.1:0");
    await api.StartAsync();
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(api.ListeningUrl!) };
        client.DefaultRequestHeaders.Add("X-Zelo-Impressao-Token", token);
        using var aborted = new CancellationTokenSource();
        var response = client.PostAsJsonAsync("/print", AutomaticJob("zelopdv"), aborted.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        aborted.Cancel();
        try { await response; throw new Exception("HTTP cancellation was ignored"); } catch (OperationCanceledException) { }
        release.Set();
        using var replay = await client.PostAsJsonAsync("/print", AutomaticJob("zelochat", "retry"));
        var result = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert(result.GetProperty("status").GetString() == "deduplicated" && calls == 1, "Client abort canceled or repeated the accepted attempt");
    }
    finally { release.Set(); await api.StopAsync(); }
});

Console.WriteLine($"{tests - failures}/{tests} passed");
if (args.Contains("--benchmark"))
{
    var config = new ConfigStore(TempDirectory());
    var printers = new PrinterManager(config);
    var api = new LocalApiServer(config, new PairingService(config), printers, new PrintService(printers, config), "http://127.0.0.1:0");
    var startup = Stopwatch.StartNew();
    await api.StartAsync();
    var startupMs = startup.Elapsed.TotalMilliseconds;
    using var client = new HttpClient { BaseAddress = new Uri(api.ListeningUrl!) };
    for (var i = 0; i < 10; i++) await client.GetStringAsync("/health");
    var latencies = new List<double>();
    for (var i = 0; i < 500; i++)
    {
        var timer = Stopwatch.StartNew();
        await client.GetStringAsync("/health");
        latencies.Add(timer.Elapsed.TotalMilliseconds);
    }
    latencies.Sort();
    using var process = Process.GetCurrentProcess();
    await Task.Delay(3000); // Let startup/JIT work settle after the request burst.
    process.Refresh();
    var cpuBefore = process.TotalProcessorTime;
    await Task.Delay(3000);
    process.Refresh();
    var cpuIdleMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
    var discoveryMs = new List<double>();
    var count = 0;
    for (var i = 0; i < 5; i++)
    {
        var discoveryTimer = Stopwatch.StartNew();
        count = printers.ListPrinters().Count;
        discoveryMs.Add(discoveryTimer.Elapsed.TotalMilliseconds);
    }
    var durableLatencies = new List<double>();
    var durableDirectory = TempDirectory();
    var durableDispatcher = new PrintDispatcher(_ => new PrinterInfo(), historyDirectory: durableDirectory);
    for (var i = 0; i < 100; i++)
    {
        var timer = Stopwatch.StartNew();
        await durableDispatcher.SubmitAsync(Job($"benchmark-{i}"));
        durableLatencies.Add(timer.Elapsed.TotalMilliseconds);
    }
    durableLatencies.Sort();
    Console.WriteLine("BENCHMARK " + JsonSerializer.Serialize(new
    {
        runtime = Environment.Version.ToString(), startupMs, requests = latencies.Count,
        p50Ms = latencies[250], p95Ms = latencies[475], maxMs = latencies[^1],
        idleCpuMsIn3Seconds = cpuIdleMs, rssMb = process.WorkingSet64 / 1024d / 1024d,
        heapMb = GC.GetTotalMemory(false) / 1024d / 1024d, printers = count, printerDiscoveryMs = discoveryMs,
        durableSubmissions = durableLatencies.Count, durableP50Ms = durableLatencies[50], durableP95Ms = durableLatencies[95], durableMaxMs = durableLatencies[^1],
        journalBytes = new FileInfo(Path.Combine(durableDirectory, "print-history.jsonl")).Length
    }));
    await api.StopAsync();
}
return failures == 0 ? 0 : 1;
