using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lovecraft.Backend.Controllers.V1;
using Lovecraft.Backend.MockData;
using System.Linq;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Images;
using Lovecraft.Common.Models;

namespace Lovecraft.UnitTests;

[Collection("ImageTests")]
public class ImageTests
{
    [Fact]
    public async Task MockImageService_UploadProfileImageAsync_ReturnsNonNullUrl()
    {
        var service = new MockImageService();
        const string userId = "1";

        var result = await service.UploadProfileImageAsync(userId, Stream.Null, "image/jpeg");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(
            MockDataStore.Users.First(u => u.Id == userId).ProfileImage,
            result);
    }

    [Fact]
    public async Task UploadProfileImage_InvalidContentType_Returns400WithInvalidImageType()
    {
        var mockUserService = new Mock<IUserService>();
        var mockEventService = new Mock<IEventService>();
        var mockImageService = new Mock<IImageService>();
        var controller = new UsersController(
            mockUserService.Object,
            mockEventService.Object,
            NullLogger<UsersController>.Instance,
            mockImageService.Object);

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
        var mockEventService = new Mock<IEventService>();
        var mockImageService = new Mock<IImageService>();
        var controller = new UsersController(
            mockUserService.Object,
            mockEventService.Object,
            NullLogger<UsersController>.Instance,
            mockImageService.Object);

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
        mockFile.Setup(f => f.Length).Returns(21 * 1024 * 1024); // 21 MB — over limit

        var result = await controller.UploadProfileImage("user1", mockFile.Object);

        var objectResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
        Assert.Equal("IMAGE_TOO_LARGE", response.Error?.Code);
    }

    [Fact]
    public async Task MockImageService_UploadContentImageAsync_ReturnsPlaceholderUrl()
    {
        var service = new MockImageService();
        var result = await service.UploadContentImageAsync("user1", Stream.Null, "image/jpeg");
        Assert.Equal("https://placehold.co/600x400", result);
    }

    [Fact]
    public async Task UploadContentImage_ValidJpeg_Returns200WithUrl()
    {
        var mockImageService = new Mock<IImageService>();
        mockImageService
            .Setup(s => s.UploadContentImageAsync(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("https://placehold.co/600x400");

        var controller = new ImagesController(
            mockImageService.Object,
            NullLogger<ImagesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "user1") }, "test"));

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
        mockFile.Setup(f => f.Length).Returns(1024);
        mockFile.Setup(f => f.OpenReadStream()).Returns(Stream.Null);

        var result = await controller.UploadContentImage(mockFile.Object);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<UploadImageResponseDto>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("https://placehold.co/600x400", response.Data?.Url);
    }

    [Fact]
    public async Task UploadContentImage_InvalidContentType_Returns400WithCode()
    {
        var mockImageService = new Mock<IImageService>();
        var controller = new ImagesController(
            mockImageService.Object,
            NullLogger<ImagesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "user1") }, "test"));

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.ContentType).Returns("text/plain");
        mockFile.Setup(f => f.Length).Returns(1024);

        var result = await controller.UploadContentImage(mockFile.Object);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<UploadImageResponseDto>>(bad.Value);
        Assert.Equal("INVALID_CONTENT_TYPE", response.Error?.Code);
    }

    [Fact]
    public async Task UploadContentImage_FileTooLarge_Returns400WithCode()
    {
        var mockImageService = new Mock<IImageService>();
        var controller = new ImagesController(
            mockImageService.Object,
            NullLogger<ImagesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "user1") }, "test"));

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.ContentType).Returns("image/png");
        mockFile.Setup(f => f.Length).Returns(11 * 1024 * 1024); // 11 MB > 10 MB limit

        var result = await controller.UploadContentImage(mockFile.Object);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<UploadImageResponseDto>>(bad.Value);
        Assert.Equal("FILE_TOO_LARGE", response.Error?.Code);
    }
}
