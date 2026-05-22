using System.Security.Cryptography;

namespace ZeloImpressao;

internal sealed class PairingService
{
    private readonly ConfigStore _configStore;
    private string _code = "";
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public PairingService(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public (string Code, DateTimeOffset ExpiresAt) GetCode()
    {
        if (DateTimeOffset.Now.AddSeconds(30) > _expiresAt)
        {
            _code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            _expiresAt = DateTimeOffset.Now.AddMinutes(10);
        }
        return (_code, _expiresAt);
    }

    public string? Confirm(string code)
    {
        if (DateTimeOffset.Now > _expiresAt) return null;
        if (!string.Equals(code.Trim(), _code, StringComparison.Ordinal)) return null;
        _expiresAt = DateTimeOffset.MinValue;
        _code = "";
        return _configStore.IssueToken();
    }
}
