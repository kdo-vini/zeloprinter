using System.Security.Cryptography;
using System.Globalization;

namespace ZeloImpressao;

internal sealed class PairingService
{
    private readonly ConfigStore _configStore;
    private string _code = "";
    private DateTimeOffset? _expiresAt;
    private readonly object _lock = new();
    private int _failedAttempts;

    public PairingService(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public (string Code, DateTimeOffset ExpiresAt) GetCode(bool renew = false)
    {
        lock (_lock)
        {
            var now = AppClock.UtcNow;
            if (renew || _expiresAt is null || string.IsNullOrEmpty(_code) || now.AddSeconds(30) > _expiresAt.Value)
            {
                _code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
                _expiresAt = now.AddMinutes(10);
                _failedAttempts = 0;
            }
            return (_code, _expiresAt.Value);
        }
    }

    public string? Confirm(string code)
    {
        lock (_lock)
        {
            if (_expiresAt is null || AppClock.UtcNow > _expiresAt.Value || _failedAttempts >= 5) return null;
            if (!string.Equals(code.Trim(), _code, StringComparison.Ordinal))
            {
                _failedAttempts++;
                return null;
            }
            var token = _configStore.IssueToken();
            _expiresAt = null;
            _code = "";
            return token;
        }
    }
}
