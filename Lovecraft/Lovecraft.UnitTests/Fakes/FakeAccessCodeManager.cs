namespace Lovecraft.UnitTests.Fakes;

using Lovecraft.Common.Interfaces;

internal class FakeAccessCodeManager : IAccessCodeManager
{
    public bool IsValidCode(string code)
    {
        return code == "ABC123";
    }
}