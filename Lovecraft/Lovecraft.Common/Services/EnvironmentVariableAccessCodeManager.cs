using Lovecraft.Common.Interfaces;

namespace Lovecraft.Common.Services;

public class EnvironmentVariableAccessCodeManager : IAccessCodeManager
{
    private readonly Dictionary<string, DateTime> _validCodes;

    public EnvironmentVariableAccessCodeManager(string envVarName = "ACCESS_CODE")
    {
        var codes = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(codes))
        {
            _validCodes = new();
        }
        else
        {
            var validCodes = codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _validCodes = validCodes.ToDictionary(c => c, c => DateTime.MaxValue);
        }
    }

    public bool IsValidCode(string code)
    {
        return _validCodes.ContainsKey(code) && DateTime.UtcNow < _validCodes[code];
    }
}