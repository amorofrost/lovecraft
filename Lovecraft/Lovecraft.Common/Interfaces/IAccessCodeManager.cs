namespace Lovecraft.Common.Interfaces;

public interface IAccessCodeManager
{
    bool IsValidCode(string code);
}