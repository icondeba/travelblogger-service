using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using TravelBlogger.Common;

namespace TravelBlogger.Infrastructure.Storage;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private static readonly Regex Base64Regex = new(@"^[A-Za-z0-9+/]*={0,2}$", RegexOptions.Compiled);
    private readonly BlobContainerClient _container;
    private readonly BlobStorageOptions _options;

    public AzureBlobStorageService(IOptions<BlobStorageOptions> options)
    {
        _options = options.Value;
        var serviceClient = new BlobServiceClient(_options.ConnectionString);
        _container = serviceClient.GetBlobContainerClient(_options.ContainerName);
    }

    public async Task<string> UploadBase64ImageAsync(string base64Image, CancellationToken ct)
    {
        var (bytes, contentType, extension) = ParseBase64Image(base64Image);
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobName = $"about-me/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var blob = _container.GetBlobClient(blobName);

        using var stream = new MemoryStream(bytes);
        var headers = new BlobHttpHeaders { ContentType = contentType };
        await blob.UploadAsync(stream, headers, cancellationToken: ct);

        return blobName;
    }

    public async Task<string> UploadImageAsync(byte[] bytes, string? contentType, CancellationToken ct)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Image file is empty.", nameof(bytes));
        }

        var detectedType = DetectContentType(bytes, contentType ?? "application/octet-stream");
        if (!detectedType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Only image files are allowed.");
        }

        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var extension = ContentTypeToExtension(detectedType);
        var blobName = $"about-me/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var blob = _container.GetBlobClient(blobName);

        using var stream = new MemoryStream(bytes);
        var headers = new BlobHttpHeaders { ContentType = detectedType };
        await blob.UploadAsync(stream, headers, cancellationToken: ct);

        return blobName;
    }

    public string GetReadSasUrl(string blobName)
    {
        var blob = _container.GetBlobClient(blobName);
        if (!blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException("Cannot generate SAS URI. Ensure BlobStorage:ConnectionString includes the account key.");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_options.SasMinutes)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(sasBuilder).ToString();
    }

    private static (byte[] Bytes, string ContentType, string Extension) ParseBase64Image(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Image is required.", nameof(input));
        }

        var trimmed = input.Trim();
        var contentType = "image/jpeg";
        var base64 = trimmed;

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = trimmed.IndexOf(',');
            if (commaIndex < 0)
            {
                throw new FormatException("Invalid data URL format.");
            }

            var meta = trimmed[5..commaIndex];
            base64 = trimmed[(commaIndex + 1)..];

            var parts = meta.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0].Contains('/'))
            {
                contentType = parts[0].Trim();
            }
        }

        base64 = base64.Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (base64.Length % 4 != 0 || !Base64Regex.IsMatch(base64))
        {
            throw new FormatException("Image must be a base64 string or data URL.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new FormatException("Image must be a base64 string or data URL.", ex);
        }

        contentType = DetectContentType(bytes, contentType);
        var extension = ContentTypeToExtension(contentType);

        return (bytes, contentType, extension);
    }

    private static string DetectContentType(byte[] bytes, string fallback)
    {
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return "image/jpeg";
            }

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return "image/png";
            }

            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            {
                return "image/gif";
            }

            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            {
                if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                {
                    return "image/webp";
                }
            }
        }

        return fallback;
    }

    private static string ContentTypeToExtension(string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".bin"
        };
    }
}
