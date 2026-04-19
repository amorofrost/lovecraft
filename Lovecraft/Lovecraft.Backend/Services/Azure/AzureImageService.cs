using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Lovecraft.Backend.Services.Azure;

public class AzureImageService : IImageService
{
    private const int MaxDimension = 800;
    private const int JpegQuality = 85;
    private const string ContainerName = "profile-images";

    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly TableClient _usersTable;
    private readonly ILogger<AzureImageService> _logger;

    public AzureImageService(
        BlobServiceClient blobServiceClient,
        TableServiceClient tableServiceClient,
        ILogger<AzureImageService> logger)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob).GetAwaiter().GetResult();
        _usersTable = tableServiceClient.GetTableClient(TableNames.Users);
    }

    public async Task<string> UploadProfileImageAsync(string userId, Stream imageStream, string contentType)
    {
        // 1. Resize image
        using var image = await Image.LoadAsync(imageStream);
        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(MaxDimension, MaxDimension),
                Mode = ResizeMode.Max
            }));
        }

        // 2. Encode to JPEG
        using var outputStream = new MemoryStream();
        var encoder = new JpegEncoder { Quality = JpegQuality };
        await image.SaveAsync(outputStream, encoder);
        outputStream.Position = 0;

        // 3. Upload to Blob Storage
        var blobName = $"{userId}/{Guid.NewGuid()}.jpg";
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(outputStream);
        var blobUrl = blobClient.Uri.ToString();

        // 4. Update UserEntity.ProfileImage in Table Storage; capture old URL for cleanup
        string? oldBlobUrl = null;
        try
        {
            var response = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            var entity = response.Value;
            oldBlobUrl = entity.ProfileImage;
            entity.ProfileImage = blobUrl;
            entity.UpdatedAt = DateTime.UtcNow;
            await _usersTable.UpdateEntityAsync(entity, entity.ETag);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to update ProfileImage in Table Storage for user {UserId}", userId);
            throw;
        }

        // 5. Delete the previous profile image blob (best-effort — don't fail the request)
        if (!string.IsNullOrEmpty(oldBlobUrl))
        {
            try
            {
                var containerPrefix = _containerClient.Uri.ToString().TrimEnd('/') + '/';
                if (oldBlobUrl.StartsWith(containerPrefix))
                {
                    var oldBlobName = oldBlobUrl[containerPrefix.Length..];
                    await _containerClient.GetBlobClient(oldBlobName).DeleteIfExistsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old profile image blob for user {UserId}", userId);
            }
        }

        return blobUrl;
    }

    public async Task<string> UploadContentImageAsync(string userId, Stream imageStream, string contentType)
    {
        // 1. Resize (same max edge for forum attachments and admin badge uploads)
        using var image = await Image.LoadAsync(imageStream);
        const int maxDimension = 1200;
        if (image.Width > maxDimension || image.Height > maxDimension)
        {
            var ratio = Math.Min((double)maxDimension / image.Width, (double)maxDimension / image.Height);
            image.Mutate(x => x.Resize((int)(image.Width * ratio), (int)(image.Height * ratio)));
        }

        // 2. Encode: JPEG has no alpha — converting PNG/WebP/GIF with transparency used to flatten onto an
        //    unintended background. Preserve alpha as PNG for those types; JPEG uploads stay JPEG.
        using var outputStream = new MemoryStream();
        string blobExtension;
        string outputContentType;

        if (contentType is "image/png" or "image/webp" or "image/gif")
        {
            await image.SaveAsync(outputStream, new PngEncoder());
            blobExtension = ".png";
            outputContentType = "image/png";
        }
        else
        {
            await image.SaveAsync(outputStream, new JpegEncoder { Quality = JpegQuality });
            blobExtension = ".jpg";
            outputContentType = "image/jpeg";
        }

        outputStream.Position = 0;

        // 3. Upload to Blob Storage
        var blobName = $"{userId}/{Guid.NewGuid()}{blobExtension}";
        var containerClient = _blobServiceClient.GetBlobContainerClient("content-images");
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(outputStream, new BlobHttpHeaders { ContentType = outputContentType });

        return blobClient.Uri.ToString();
    }
}
