using System.Globalization;
using System.Security.Cryptography;

namespace ZeloImpressao;

internal sealed class PairingService
{
    private readonly ConfigStore _configStore;
    private string _code = "";
    private DateTimeOffset? _expiresAt;

    public PairingService(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public (string Code, DateTimeOffset ExpiresAt) GetCode()
    {
        var now = AppClock.UtcNow;
        if (_expiresAt is null || now.AddSeconds(30) > _expiresAt.Value || string.IsNullOrWhiteSpace(_code))
        {
            _code = RandomNumberGenerator.GetInt32(100000, 999999).ToString(CultureInfo.InvariantCulture);
            _expiresAt = now.AddMinutes(10);
        }
        return (_code, _expiresAt.Value);
    }

    public string? Confirm(string code)
    {
        if (_expiresAt is null || string.IsNullOrWhiteSpace(_code)) return null;
        if (AppClock.UtcNow > _expiresAt.Value) return null;
        if (!string.Equals(code.Trim(), _code, StringComparison.Ordinal)) return null;
        _expiresAt = null;
        _code = "";
        return _configStore.IssueToken();
    }
}
