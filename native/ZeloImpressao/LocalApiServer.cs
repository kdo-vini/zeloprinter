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

    public LocalApiServer(ConfigStore configStore, PairingService pairingService, PrinterManager printerManager, PrintService printService)
    {
        _configStore = configStore;
        _pairingService = pairingService;
        _printerManager = printerManager;
        _printService = printService;
    }

    public bool IsRunning => _app is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = AppConstants.ProductName
        });
        builder.WebHost.UseUrls($"http://{AppConstants.ApiHost}:{AppConstants.ApiPort}");
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        var app = builder.Build();

        app.UseExceptionHandler(handler =>
        {
            handler.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new ApiError { Message = "Falha ao processar solicitação." });
            });
        });

        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > AppConstants.MaxJsonBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.Response.WriteAsJsonAsync(new ApiError { Message = "Payload muito grande." });
                return;
            }
            if (!await CheckCorsAsync(context)) return;
            if (context.Request.Method == HttpMethods.Options)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next();
        });

        app.MapGet("/health", () =>
        {
            var process = Process.GetCurrentProcess();
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
            {
                return Results.Json(
                    new ApiError
                    {
                        Code = string.IsNullOrWhiteSpace(origin)
                            ? "AUTO_CONNECT_ORIGIN_REQUIRED"
                            : "AUTO_CONNECT_NOT_ALLOWED",
                        Message = "Esta origem não pode fazer conexão automática. Use o código de pareamento."
                    },
                    statusCode: 403
                );
            }

            var token = _configStore.IssueToken();
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
            return Results.Json(new
            {
                ok = true,
                config = ConfigStore.ToApiView(cfg)
            });
        });

        app.MapPost("/config", async (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var patch = await ReadJson<ConfigPatch>(context.Request) ?? new ConfigPatch();
            var cfg = _configStore.Update(patch);
            return Results.Json(new { ok = true, config = ConfigStore.ToApiView(cfg) });
        });

        app.MapPost("/print", async (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var job = await ReadJson<PrintJob>(context.Request) ?? throw new InvalidOperationException("Payload inválido.");
            var printer = _printService.Print(job);
            _configStore.Log("print_job_ok", new { job.Source, job.Type, printer = printer.Name });
            return Results.Json(new { ok = true, printer, mode = job.Content.Format == "raw_escpos_base64" ? "raw" : "driver" });
        });

        app.MapPost("/test-print", async (HttpContext context) =>
        {
            if (!RequireAuth(context)) return Unauthorized();
            var body = await ReadJson<TestPrintRequest>(context.Request);
            var printer = _printService.TestPrint(body?.PrinterId);
            _configStore.Log("test_print_ok", new { printer = printer.Name });
            return Results.Json(new { ok = true, printer, mode = "raw" });
        });

        _app = app;
        await app.StartAsync(cancellationToken);
        _configStore.Log("api_started");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null) return;
        await _app.StopAsync(cancellationToken);
        await _app.DisposeAsync();
        _app = null;
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
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
        if (request.ContentLength > AppConstants.MaxJsonBytes)
            throw new InvalidOperationException("Payload muito grande.");

        return await request.ReadFromJsonAsync<T>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
