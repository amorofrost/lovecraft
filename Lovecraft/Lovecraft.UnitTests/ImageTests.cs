using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lovecraft.Backend.Controllers.V1;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.Models;

namespace Lovecraft.UnitTests;

[Collection("ImageTests")]
public class ImageTests
{
    [Fact]
    public async Task MockImageService_UploadProfileImageAsync_ReturnsNonNullUrl()
    {
        var service = new MockImageService();
        var userId = MockDataStore.Users[0].Id;

        var result = await service.UploadProfileImageAsync(userId, Stream.Null, "image/jpeg");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(MockDataStore.Users[0].ProfileImage, result);
    }

    [Fact]
    public async Task UploadProfileImage_InvalidContentType_Returns400WithInvalidImageType()
    {
        var mockUserService = new Mock<IUserService>();
        var mockImageService = new Mock<IImageService>();
        var controller = new UsersController(mockUserService.Object, NullLogger<UsersController>.Instance, mockImageService.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user1")
            }, "test"));

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.ContentType).Returns("text/plain");
        mockFile.Setup(f => f.Length).Returns(1024);

        var result = await controller.UploadProfileImage("user1", mockFile.Object);

        var objectResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
        Assert.Equal("INVALID_IMAGE_TYPE", response.Error?.Code);
    }

    [Fact]
    public async Task UploadProfileImage_FileTooLarge_Returns400WithImageTooLarge()
    {
        var mockUserService = new Mock<IUserService>();
        var mockImageService = new Mock<IImageService>();
        var controller = new UsersController(mockUserService.Object, NullLogger<UsersController>.Instance, mockImageService.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user1")
            }, "test"));

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.Length).Returns(6 * 1024 * 1024); // 6 MB — over limit

        var result = await controller.UploadProfileImage("user1", mockFile.Object);

        var objectResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
        Assert.Equal("IMAGE_TOO_LARGE", response.Error?.Code);
    }
}
