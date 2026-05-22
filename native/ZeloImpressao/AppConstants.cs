namespace ZeloImpressao;

internal static class AppConstants
{
    public const string ProductName = "Zelo Impressão";
    public const string ProductDescription = "Componente local do Zelo para impressão automática de comprovantes, pedidos e comandas.";
    public const string Version = "0.1.3";
    public const string ApiHost = "127.0.0.1";
    public const int ApiPort = 17321;
    public const int MaxJsonBytes = 512 * 1024;
    public const string MutexName = "Global\\Techne_Zelo_Impressao";

    public static readonly string[] DefaultAllowedOrigins =
    [
        "https://zelopdv.com.br",
        "https://www.zelopdv.com.br",
        "https://app.zelopdv.com.br",
        "https://chat.zelopdv.com.br",
        "https://zelochat.com.br",
        "https://www.zelochat.com.br",
        "https://app.zelochat.com.br",
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:3000",
        "http://127.0.0.1:3000"
    ];
}
