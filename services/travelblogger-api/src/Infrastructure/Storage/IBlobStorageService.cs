namespace TravelBlogger.Infrastructure.Storage;

public interface IBlobStorageService
{
    Task<string> UploadBase64ImageAsync(string base64Image, CancellationToken ct);
    Task<string> UploadImageAsync(byte[] bytes, string? contentType, CancellationToken ct);
    string GetReadSasUrl(string blobName);
}
