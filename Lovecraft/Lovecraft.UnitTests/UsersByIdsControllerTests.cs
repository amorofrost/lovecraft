using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lovecraft.Backend.Controllers.V1;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;
using Xunit;

namespace Lovecraft.UnitTests;

public class UsersByIdsControllerTests
{
    private static UsersController BuildController(Mock<IUserService> userService)
    {
        return new UsersController(
            userService.Object,
            new Mock<IEventService>().Object,
            new Mock<IMatchingService>().Object,
            NullLogger<UsersController>.Instance,
            new Mock<IImageService>().Object);
    }

    private static List<UserDto> Unwrap(ActionResult<ApiResponse<List<UserDto>>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<List<UserDto>>>(ok.Value);
        Assert.True(body.Success);
        return body.Data!;
    }

    [Fact]
    public async Task GetUsersByIds_ResolvesRequestedUsers()
    {
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetUserByIdAsync("a")).ReturnsAsync(new UserDto { Id = "a" });
        userService.Setup(s => s.GetUserByIdAsync("b")).ReturnsAsync(new UserDto { Id = "b" });
        var controller = BuildController(userService);

        var data = Unwrap(await controller.GetUsersByIds("a,b"));

        Assert.Equal(new[] { "a", "b" }, data.Select(u => u.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task GetUsersByIds_DeduplicatesIds()
    {
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetUserByIdAsync("a")).ReturnsAsync(new UserDto { Id = "a" });
        var controller = BuildController(userService);

        var data = Unwrap(await controller.GetUsersByIds("a,a,a"));

        Assert.Single(data);
        userService.Verify(s => s.GetUserByIdAsync("a"), Times.Once);
    }

    [Fact]
    public async Task GetUsersByIds_SkipsMissingUsers()
    {
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetUserByIdAsync("a")).ReturnsAsync(new UserDto { Id = "a" });
        userService.Setup(s => s.GetUserByIdAsync("ghost")).ReturnsAsync((UserDto?)null);
        var controller = BuildController(userService);

        var data = Unwrap(await controller.GetUsersByIds("a,ghost"));

        Assert.Single(data);
        Assert.Equal("a", data[0].Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ,  ,")]
    public async Task GetUsersByIds_EmptyInput_ReturnsEmpty(string? ids)
    {
        var userService = new Mock<IUserService>();
        var controller = BuildController(userService);

        var data = Unwrap(await controller.GetUsersByIds(ids));

        Assert.Empty(data);
        userService.Verify(s => s.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
    }
}
