using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ZeloImpressao;

internal sealed class LocalApiServer
{
    private readonly ConfigStore _configStore;
    private readonly PairingService _pairingService;
    private readonly PrinterManager _printerManager;
    private readonly PrintService _printService;
    private WebApplication? _app;
    private readonly string _listenUrl;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    public LocalApiServer(ConfigStore configStore, PairingService pairingService, PrinterManager printerManager, PrintService printService, string? listenUrl = null)
    {
        _configStore = configStore;
        _pairingService = pairingService;
        _printerManager = printerManager;
        _printService = printService;
        _listenUrl = listenUrl ?? $"http://{AppConstants.ApiHost}:{AppConstants.ApiPort}";
    }

    public bool IsRunning => _app is not null;
    internal string? ListeningUrl => _app?.Urls.FirstOrDefault();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StartCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LocalApiServer).Assembly.GetName().Name
        });
        builder.WebHost.UseUrls(_listenUrl);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = AppConstants.MaxJsonBytes);
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        var app = builder.Build();

        app.UseExceptionHandler(handler =>
        {
            handler.Run(async context =>
            {
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var printError = error as PrintRequestException;
                var status = printError?.StatusCode ?? (error is BadHttpRequestException badRequest ? badRequest.StatusCode : error is JsonException ? 400 : 500);
                var uncertainPrint = printError is null && status >= 500 && (context.Request.Path == "/print" || context.Request.Path == "/test-print");
                _configStore.Log("api_request_failed", new { path = context.Request.Path.Value, status, error = error?.GetType().Name, detail = printError?.InnerException?.Message, code = printError?.Code, traceId = context.TraceIdentifier });
                context.Response.StatusCode = status;
                if (!await CheckCorsAsync(context)) return;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new ApiError
                {
                    Message = printError?.Message ?? (uncertainPrint ? "Não foi possível confirmar a impressão. Confira a saída antes de tentar novamente." : status == 413 ? "Payload muito grande." : "Falha ao processar solicitação."),
                    Code = printError?.Code ?? (uncertainPrint ? "PRINT_OUTCOME_UNKNOWN" : status == 413 ? "PAYLOAD_TOO_LARGE" : "INVALID_REQUEST"),
                    RetrySafe = printError?.RetrySafe ?? !uncertainPrint
                });
            });
        });

        app.Use(async (context, next) =>
        {
            if (!await CheckCorsAsync(context)) return;
            if (context.Request.ContentLength > AppConstants.MaxJsonBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new ApiError { Message = "Payload muito grande." });
                return;
            }
            if (context.Request.Method == HttpMethods.Options)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next();
        });

        app.MapGet("/health", () =>
        {
            using var process = Process.GetCurrentProcess();
            var cfg = _configStore.Get();
            return Results.Json(new
            {
                ok = true,
                status = "running",
                productName = AppConstants.ProductName,
                version = AppConstants.Version,
                os = "win32",
                memory = new
                {
                    rssMb = Math.Round(process.WorkingSet64 / 1024d / 1024d),
                    heapUsedMb = Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d)
                },
                pairingRequired = cfg.RequirePairing,
                paired = cfg.TokenHashes.Count > 0,
                capabilities = new
                {
                    rawEscpos = true,
                    windowsDriverPrinting = true,
                    testPrint = true,
                    printerSelection = true,
                    silentPrinting = true,
                    autoConnect = cfg.AutoConnectEnabled,
                    jobIdDeduplication = true,
                    canonicalAutoPrint = true,
                    persistentPrintDeduplication = true,
                    autoPrintPreferredSource = cfg.PreferredAutoPrintSource,
                    autoPrintGraceMs = PrintDispatcher.PreferenceGraceMs,
                    deduplicationWindowSeconds = PrintJournal.RetentionSeconds,
                    maxRememberedJobs = cfg.PrintHistoryCapacity,
                    productName = AppConstants.ProductName
                }
            });
        });

        app.MapPost("/pair", async (HttpRequest request) =>
        {
            var body = await ReadJson<PairRequest>(request);
            var token = _pairingService.Confirm(body?.Code ?? "");
            return token is null
                ? Results.Json(new ApiError { Message = "Código de pareamento inválido ou expirado." }, statusCode: 401)
                : Results.Json(new { ok = true, token });
        });

        app.MapPost("/connect", (HttpContext context) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!AppConstants.AutoConnectOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                return Results.Json(new ApiError
                {
                    Code = string.IsNullOrWhiteSpace(origin) ? "AUTO_CONNECT_ORIGIN_REQUIRED" : "AUTO_CONNECT_NOT_ALLOWED",
                    Message = "Conecte este navegador usando o código exibido no aplicativo."
                }, statusCode: 403);
            var token = _configStore.IssueToken(automatic: true);
            return Results.Json(new { ok = true, token });
        });

        app.MapGet("/printers", (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            return Results.Json(new { ok = true, printers = _printerManager.ListPrinters() });
        });

        app.MapGet("/config", (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var cfg = _configStore.Get();
            return Results.Json(new { ok = true, config = ConfigStore.ToApiView(cfg) });
        });

        app.MapPost("/config", async (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var patch = await ReadJson<ConfigPatch>(context.Request) ?? new ConfigPatch();
            if (patch.RequirePairing.HasValue || patch.AutoConnectEnabled.HasValue)
                throw new PrintRequestException("A segurança do pareamento só pode ser alterada no aplicativo local.", "LOCAL_SETTING_ONLY", 403);
            var cfg = _configStore.Update(patch);
            return Results.Json(new { ok = true, config = ConfigStore.ToApiView(cfg) });
        });

        app.MapPost("/print", async (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var job = await ReadJson<PrintJob>(context.Request) ?? throw new PrintRequestException("Payload inválido.", "INVALID_JOB");
            var stopwatch = Stopwatch.StartNew();
            var result = await _printService.PrintAsync(job);
            _configStore.Log("print_job_accepted", new { job.JobId, result.Source, job.Type, duplicate = result.Duplicate, elapsedMs = stopwatch.ElapsedMilliseconds, traceId = context.TraceIdentifier });
            return Results.Json(new
            {
                ok = true, job.JobId, status = result.Duplicate ? "deduplicated" : "spooled", printer = result.Printer, mode = result.Mode,
                arbitration = job.Intent?.Mode == "automatic" ? new { mode = "automatic", source = result.Source, job.Intent.OrderId, job.Intent.Purpose, duplicate = result.Duplicate } : null
            });
        });

        app.MapPost("/test-print", async (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var body = await ReadJson<TestPrintRequest>(context.Request);
            var result = await _printService.TestPrintAsync(body?.PrinterId);
            _configStore.Log("test_print_ok", new { printer = result.Printer?.Name });
            return Results.Json(new { ok = true, printer = result.Printer, mode = "raw" });
        });

        try { await app.StartAsync(cancellationToken).ConfigureAwait(false); }
        catch { await app.DisposeAsync().ConfigureAwait(false); throw; }
        _app = app;
        _configStore.Log("api_started");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var app = _app;
        if (app is null) return;
        try { await app.StopAsync(cancellationToken).ConfigureAwait(false); }
        finally { _app = null; await app.DisposeAsync().ConfigureAwait(false); }
    }

    public async Task RestartAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync(default).ConfigureAwait(false); await StartCoreAsync(default).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task<bool> CheckCorsAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return true;

        if (!_configStore.Get().AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ApiError { Message = "Origem não autorizada." });
            return false;
        }

        context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        context.Response.Headers["Vary"] = "Origin";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Zelo-Impressao-Token";
        context.Response.Headers["Access-Control-Max-Age"] = "600";
        return true;
    }

    private bool RequireAuth(HttpContext context)
    {
        var token = context.Request.Headers["X-Zelo-Impressao-Token"].FirstOrDefault();
        return _configStore.VerifyToken(token);
    }

    private static IResult Unauthorized()
    {
        return Results.Json(new ApiError
        {
            Code = "PAIRING_REQUIRED",
            Message = "Pareie este navegador com o Zelo Impressão."
        }, statusCode: 401);
    }

    private static async Task<T?> ReadJson<T>(HttpRequest request)
    {
        if (!request.HasJsonContentType()) throw new BadHttpRequestException("Content-Type deve ser application/json.", StatusCodes.Status415UnsupportedMediaType);
        if (request.ContentLength > AppConstants.MaxJsonBytes)
            throw new InvalidOperationException("Payload muito grande.");

        return await request.ReadFromJsonAsync<T>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

}
