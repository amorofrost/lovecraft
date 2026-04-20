using Lovecraft.Backend.Auth;
using Lovecraft.Common.DTOs.Auth;

namespace Lovecraft.UnitTests;

public class TelegramLoginVerifierTests
{
    [Fact]
    public void Verify_RoundTrip_AcceptsFreshPayload()
    {
        var token = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
        var dto = new TelegramLoginRequestDto
        {
            Id = 123456789,
            FirstName = "Test",
            AuthDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Hash = ""
        };
        dto.Hash = TelegramLoginVerifier.ComputeLoginHashHex(token, dto);

        Assert.True(TelegramLoginVerifier.Verify(token, dto));
    }

    [Fact]
    public void Verify_WrongHash_Rejects()
    {
        var token = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
        var dto = new TelegramLoginRequestDto
        {
            Id = 123456789,
            FirstName = "Test",
            AuthDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Hash = "0000000000000000000000000000000000000000000000000000000000000000"
        };

        Assert.False(TelegramLoginVerifier.Verify(token, dto));
    }

    [Fact]
    public void Verify_KnownVector_MatchesPythonReference()
    {
        var token = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
        var dto = new TelegramLoginRequestDto
        {
            Id = 123456789,
            FirstName = "Test",
            AuthDate = 1234567890,
            Hash = ""
        };
        var hex = TelegramLoginVerifier.ComputeLoginHashHex(token, dto);
        Assert.Equal("dbc6155025e4fff0ca9cca061bb6fa09435d55f5b9360d78605a754c3d15f313", hex);
    }
}
