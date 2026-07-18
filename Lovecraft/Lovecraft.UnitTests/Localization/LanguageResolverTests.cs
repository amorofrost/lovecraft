using Lovecraft.Common.Enums;
using Lovecraft.Common.Localization;
using Xunit;

namespace Lovecraft.UnitTests.Localization;

public class LanguageResolverTests
{
    [Theory]
    [InlineData("ru", Language.Ru)]
    [InlineData("ru-RU", Language.Ru)]
    [InlineData("en", Language.En)]
    [InlineData("en-US", Language.En)]
    [InlineData("EN", Language.En)]
    [InlineData("de", Language.Ru)]
    [InlineData("", Language.Ru)]
    [InlineData(null, Language.Ru)]
    public void FromTelegramCode_maps_expected(string? code, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.FromTelegramCode(code));
    }

    [Theory]
    [InlineData("{\"Language\":0}", Language.Ru)]
    [InlineData("{\"Language\":1}", Language.En)]
    [InlineData("{\"language\":1}", Language.En)]
    [InlineData("{\"Language\":\"en\"}", Language.En)]
    [InlineData("{\"Language\":\"En\"}", Language.En)]
    [InlineData("{\"Language\":\"ru\"}", Language.Ru)]
    [InlineData("{\"Language\":\"xx\"}", Language.Ru)]
    [InlineData("{\"Language\":2}", Language.Ru)]
    [InlineData("{}", Language.Ru)]
    [InlineData("", Language.Ru)]
    [InlineData(null, Language.Ru)]
    [InlineData("not-json", Language.Ru)]
    [InlineData("[1,2,3]", Language.Ru)]
    public void FromSettings_maps_expected(string? json, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.FromSettings(json));
    }
}
