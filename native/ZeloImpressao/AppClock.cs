namespace ZeloImpressao;

internal static class AppClock
{
    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public static DateTimeOffset SafeNow
    {
        get
        {
            try
            {
                return DateTimeOffset.Now;
            }
            catch
            {
                return UtcNow;
            }
        }
    }
}
