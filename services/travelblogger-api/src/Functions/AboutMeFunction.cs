using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Primitives;
using TravelBlogger.Common;
using TravelBlogger.Contracts.Requests;
using TravelBlogger.Contracts.Responses;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Repositories;
using TravelBlogger.Infrastructure.Storage;

namespace TravelBlogger.Functions;

public sealed class AboutMeFunction
{
    private const int HeadingMaxLength = 250;
    private readonly IAboutMeRepository _aboutMe;
    private readonly IBlobStorageService _blobStorage;

    public AboutMeFunction(IAboutMeRepository aboutMe, IBlobStorageService blobStorage)
    {
        _aboutMe = aboutMe;
        _blobStorage = blobStorage;
    }

    [Function("GetAboutMe")]
    [OpenApiOperation(operationId: "GetAboutMe", tags: new[] { "AboutMe" }, Summary = "Get about me content")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<AboutMeResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetAboutMe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "about-me")] HttpRequestData req,
        CancellationToken ct)
    {
        var entity = await _aboutMe.GetAsync(ct);
        if (entity is null)
        {
            return await ResponseFactory.NotFoundAsync(req, "About me not found.");
        }

        var imageUrl = await EnsureSasUrlAsync(entity);
        var response = new AboutMeResponse
        {
            Id = entity.Id,
            Heading = entity.Heading,
            Content = entity.Content,
            Image = imageUrl,
            UpdatedAt = entity.UpdatedAt
        };

        return await ResponseFactory.OkAsync(req, response);
    }

    [Function("UpsertAboutMe")]
    [OpenApiOperation(operationId: "UpsertAboutMe", tags: new[] { "AboutMe" }, Summary = "Create or update about me (admin)")]
    [OpenApiRequestBody("application/json", typeof(AboutMeUpsertRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<AboutMeResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<AboutMeResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> UpsertAboutMe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "options", Route = "about-me")] HttpRequestData req,
        CancellationToken ct)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return await ResponseFactory.NoContentAsync(req);
        }

        var authResult = await ValidateAdminAsync(req);
        if (authResult is not null)
        {
            return authResult;
        }

        var body = await req.ReadFromJsonAsync<AboutMeUpsertRequest>(cancellationToken: ct);
        if (body is null)
        {
            return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
        }

        if (string.IsNullOrWhiteSpace(body.Content))
        {
            return await ResponseFactory.BadRequestAsync(req, "Content is required.");
        }

        if (string.IsNullOrWhiteSpace(body.Heading))
        {
            return await ResponseFactory.BadRequestAsync(req, "Heading is required.");
        }

        if (body.Heading.Trim().Length > HeadingMaxLength)
        {
            return await ResponseFactory.BadRequestAsync(req, $"Heading cannot exceed {HeadingMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(body.Image))
        {
            return await ResponseFactory.BadRequestAsync(req, "Image is required.");
        }

        var entity = await _aboutMe.GetAsync(ct);
        var isNew = entity is null;
        var imageValue = body.Image.Trim();

        var imageIsBase64 = IsBase64Payload(imageValue);
        var imageIsUrl = Uri.TryCreate(imageValue, UriKind.Absolute, out _);
        if (!imageIsBase64 && !imageIsUrl)
        {
            return await ResponseFactory.BadRequestAsync(req, "Image must be a base64 string, data URL, or absolute URL.");
        }

        string imageBlobName;
        string imageSasUrl;
        if (imageIsBase64)
        {
            imageBlobName = await _blobStorage.UploadBase64ImageAsync(imageValue, ct);
            imageSasUrl = _blobStorage.GetReadSasUrl(imageBlobName);
        }
        else
        {
            imageBlobName = entity?.ImageBlobName ?? string.Empty;
            imageSasUrl = imageValue;
        }

        if (entity is null)
        {
            entity = new AboutMe
            {
                Id = Guid.NewGuid(),
                Heading = body.Heading.Trim(),
                Content = body.Content,
                Image = imageSasUrl,
                ImageBlobName = imageBlobName,
                UpdatedAt = DateTime.UtcNow
            };

            await _aboutMe.AddAsync(entity, ct);
        }
        else
        {
            entity.Heading = body.Heading.Trim();
            entity.Content = body.Content;
            entity.Image = imageSasUrl;
            entity.ImageBlobName = imageBlobName;
            entity.UpdatedAt = DateTime.UtcNow;

            await _aboutMe.UpdateAsync(entity, ct);
        }

        var response = new AboutMeResponse
        {
            Id = entity.Id,
            Heading = entity.Heading,
            Content = entity.Content,
            Image = entity.Image,
            UpdatedAt = entity.UpdatedAt
        };

        if (isNew)
        {
            return await ResponseFactory.CreatedAsync(req, response);
        }

        return await ResponseFactory.OkAsync(req, response, "Updated");
    }

    [Function("DeleteAboutMe")]
    [OpenApiOperation(operationId: "DeleteAboutMe", tags: new[] { "AboutMe" }, Summary = "Delete about me content (admin)")]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> DeleteAboutMe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "about-me")] HttpRequestData req,
        CancellationToken ct)
    {
        var authResult = await ValidateAdminAsync(req);
        if (authResult is not null)
        {
            return authResult;
        }

        var entity = await _aboutMe.GetAsync(ct);
        if (entity is null)
        {
            return await ResponseFactory.NotFoundAsync(req, "About me not found.");
        }

        var deleted = await _aboutMe.DeleteAsync(entity.Id, ct);
        if (!deleted)
        {
            return await ResponseFactory.NotFoundAsync(req, "About me not found.");
        }

        return await ResponseFactory.NoContentAsync(req);
    }

    [Function("DeleteAboutMeById")]
    [OpenApiOperation(operationId: "DeleteAboutMeById", tags: new[] { "AboutMe" }, Summary = "Delete about me content by id (admin)")]
    [OpenApiParameter("id", In = Microsoft.OpenApi.Models.ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithoutBody(HttpStatusCode.NoContent)]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> DeleteAboutMeById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", "options", Route = "about-me/{id}")] HttpRequestData req,
        string id,
        CancellationToken ct)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return await ResponseFactory.NoContentAsync(req);
        }

        var authResult = await ValidateAdminAsync(req);
        if (authResult is not null)
        {
            return authResult;
        }

        if (!Guid.TryParse(id, out var parsedId))
        {
            return await ResponseFactory.BadRequestAsync(req, "Invalid about me id.");
        }

        var deleted = await _aboutMe.DeleteAsync(parsedId, ct);
        if (!deleted)
        {
            return await ResponseFactory.NotFoundAsync(req, "About me not found.");
        }

        return await ResponseFactory.NoContentAsync(req);
    }

    [Function("UpsertAboutMeMultipart")]
    [OpenApiOperation(operationId: "UpsertAboutMeMultipart", tags: new[] { "AboutMe" }, Summary = "Create or update about me with multipart form-data (admin)")]
    [OpenApiRequestBody("multipart/form-data", typeof(AboutMeUpsertRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<AboutMeResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<AboutMeResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> UpsertAboutMeMultipart(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "about-me/form")] HttpRequestData req,
        CancellationToken ct)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return await ResponseFactory.NoContentAsync(req);
        }

        var authResult = await ValidateAdminAsync(req);
        if (authResult is not null)
        {
            return authResult;
        }

        try
        {
            var (heading, content, imageBytes, imageContentType) = await ReadMultipartFormAsync(req, ct);

            var imageBlobName = await _blobStorage.UploadImageAsync(imageBytes, imageContentType, ct);
            var imageSasUrl = _blobStorage.GetReadSasUrl(imageBlobName);

            var entity = await _aboutMe.GetAsync(ct);
            var isNew = entity is null;
            if (entity is null)
            {
                entity = new AboutMe
                {
                    Id = Guid.NewGuid(),
                    Heading = heading,
                    Content = content,
                    Image = imageSasUrl,
                    ImageBlobName = imageBlobName,
                    UpdatedAt = DateTime.UtcNow
                };

                await _aboutMe.AddAsync(entity, ct);
            }
            else
            {
                entity.Heading = heading;
                entity.Content = content;
                entity.Image = imageSasUrl;
                entity.ImageBlobName = imageBlobName;
                entity.UpdatedAt = DateTime.UtcNow;

                await _aboutMe.UpdateAsync(entity, ct);
            }

            var response = new AboutMeResponse
            {
                Id = entity.Id,
                Heading = entity.Heading,
                Content = entity.Content,
                Image = entity.Image,
                UpdatedAt = entity.UpdatedAt
            };

            if (isNew)
            {
                return await ResponseFactory.CreatedAsync(req, response);
            }

            return await ResponseFactory.OkAsync(req, response, "Updated");
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is ArgumentException)
        {
            return await ResponseFactory.BadRequestAsync(req, ex.Message);
        }
    }

    private Task<string> EnsureSasUrlAsync(AboutMe entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Image) && string.IsNullOrWhiteSpace(entity.ImageBlobName))
        {
            return Task.FromResult(string.Empty);
        }

        var blobName = entity.ImageBlobName;
        if (string.IsNullOrWhiteSpace(blobName))
        {
            if (Uri.TryCreate(entity.Image, UriKind.Absolute, out _))
            {
                return Task.FromResult(entity.Image);
            }

            blobName = entity.Image;
        }

        try
        {
            return Task.FromResult(_blobStorage.GetReadSasUrl(blobName));
        }
        catch
        {
            // Keep existing value if SAS creation fails so the API can still return content.
            return Task.FromResult(entity.Image ?? string.Empty);
        }
    }

    private static async Task<(string Heading, string Content, byte[] ImageBytes, string ContentType)> ReadMultipartFormAsync(
        HttpRequestData req,
        CancellationToken ct)
    {
        if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
        {
            throw new InvalidOperationException("Content-Type header missing.");
        }

        var contentType = contentTypeValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Content-Type must be multipart/form-data.");
        }

        var boundary = GetBoundary(contentType);
        if (string.IsNullOrWhiteSpace(boundary))
        {
            throw new InvalidOperationException("Multipart boundary missing.");
        }

        var reader = new MultipartReader(boundary, req.Body);
        string? heading = null;
        string? content = null;
        byte[]? imageBytes = null;
        string? imageContentType = null;

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            var contentDisposition = section.ContentDisposition;
            if (string.IsNullOrWhiteSpace(contentDisposition))
            {
                continue;
            }

            var name = GetDispositionValue(contentDisposition, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                using var readerStream = new StreamReader(section.Body, Encoding.UTF8, leaveOpen: true);
                content = await readerStream.ReadToEndAsync(ct);
            }
            else if (name.Equals("heading", StringComparison.OrdinalIgnoreCase))
            {
                using var readerStream = new StreamReader(section.Body, Encoding.UTF8, leaveOpen: true);
                heading = await readerStream.ReadToEndAsync(ct);
            }
            else if (name.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms, ct);
                imageBytes = ms.ToArray();

                if (section.Headers is not null && section.Headers.TryGetValue("Content-Type", out StringValues headerValues))
                {
                    imageContentType = headerValues.FirstOrDefault();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(heading))
        {
            throw new ArgumentException("Heading is required.");
        }

        var trimmedHeading = heading.Trim();
        if (trimmedHeading.Length > HeadingMaxLength)
        {
            throw new ArgumentException($"Heading cannot exceed {HeadingMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.");
        }

        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image file is required.");
        }

        return (trimmedHeading, content, imageBytes, imageContentType ?? "application/octet-stream");
    }

    private static string? GetBoundary(string contentType)
    {
        const string boundaryKey = "boundary=";
        var index = contentType.IndexOf(boundaryKey, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var boundary = contentType[(index + boundaryKey.Length)..].Trim();
        if (boundary.StartsWith("\"", StringComparison.Ordinal) && boundary.EndsWith("\"", StringComparison.Ordinal))
        {
            boundary = boundary[1..^1];
        }

        return boundary;
    }

    private static string? GetDispositionValue(string contentDisposition, string key)
    {
        foreach (var segment in contentDisposition.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex < 0)
            {
                continue;
            }

            var value = trimmed[(equalsIndex + 1)..].Trim().Trim('"');
            return value;
        }

        return null;
    }

    private static bool IsBase64Payload(string input)
    {
        if (input.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = input.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (trimmed.Length < 16 || trimmed.Length % 4 != 0)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            var isBase64Char = (ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch == '+'
                || ch == '/'
                || ch == '=';

            if (!isBase64Char)
            {
                return false;
            }
        }

        return true;
    }

    private Task<HttpResponseData?> ValidateAdminAsync(HttpRequestData req)
    {
        return Task.FromResult<HttpResponseData?>(null);
    }
}
