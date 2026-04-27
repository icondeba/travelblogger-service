using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using TravelBlogger.Common;
using TravelBlogger.Contracts.Requests;
using TravelBlogger.Contracts.Responses;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Repositories;
using TravelBlogger.Infrastructure.Storage;

namespace TravelBlogger.Functions;

public sealed class AwardsFunction
{
    private const int TitleMaxLength = 200;
    private const int OrgMaxLength = 200;
    private const int YearMaxLength = 20;
    private const int ImageMaxLength = 2048;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CacheKeyAll = "awards:all";
    private static string CacheKeyById(Guid id) => $"awards:{id}";

    private readonly IAwardRepository _awards;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<AwardsFunction> _logger;
    private readonly IMemoryCache _cache;

    public AwardsFunction(IAwardRepository awardsRepo, IBlobStorageService blobStorage, ILogger<AwardsFunction> logger, IMemoryCache cache)
    {
        _awards = awardsRepo;
        _blobStorage = blobStorage;
        _logger = logger;
        _cache = cache;
    }

    [Function("GetAwards")]
    [OpenApiOperation(operationId: "GetAwards", tags: new[] { "Awards" }, Summary = "Get all awards")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<List<AwardResponse>>))]
    public async Task<HttpResponseData> GetAwards(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "awards")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetAwards started", correlationId);

        try
        {
            if (_cache.TryGetValue(CacheKeyAll, out List<AwardResponse>? cached) && cached is not null)
            {
                _logger.LogInformation("CorrelationId {CorrelationId} - GetAwards served from cache. Count {Count}", correlationId, cached.Count);
                return await ResponseFactory.OkCachedAsync(req, cached);
            }

            var items = await _awards.GetAllAsync(ct);
            var response = items.Select(ToResponse).ToList();
            _cache.Set(CacheKeyAll, response, CacheTtl);
            _logger.LogInformation("CorrelationId {CorrelationId} - GetAwards completed. Count {Count}", correlationId, response.Count);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetAwards failed", correlationId);
            throw;
        }
    }

    [Function("GetAwardById")]
    [OpenApiOperation(operationId: "GetAwardById", tags: new[] { "Awards" }, Summary = "Get award by id")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<AwardResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetAwardById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "awards/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetAwardById started. AwardId {AwardId}", correlationId, id);

        try
        {
            var cacheKey = CacheKeyById(id);
            if (_cache.TryGetValue(cacheKey, out AwardResponse? cached) && cached is not null)
                return await ResponseFactory.OkCachedAsync(req, cached);

            var entity = await _awards.GetByIdAsync(id, ct);
            if (entity is null)
                return await ResponseFactory.NotFoundAsync(req, "Award not found.");

            var response = ToResponse(entity);
            _cache.Set(cacheKey, response, CacheTtl);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetAwardById failed", correlationId);
            throw;
        }
    }

    [Function("CreateAward")]
    [OpenApiOperation(operationId: "CreateAward", tags: new[] { "Awards" }, Summary = "Create award (admin)")]
    [OpenApiRequestBody("application/json", typeof(AwardCreateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<AwardResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> CreateAward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "awards")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - CreateAward started", correlationId);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null) return authResult;

            var body = await req.ReadFromJsonAsync<AwardCreateRequest>(cancellationToken: ct);
            if (body is null)
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");

            var errors = ValidatePayload(body.Year, body.Title, body.Organization, body.Description, body.Image);
            if (errors.Length > 0)
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", errors);

            var (image, imageBlobName) = await ResolveImageAsync(body.Image, string.Empty, string.Empty, ct);

            var entity = new Award
            {
                Id = Guid.NewGuid(),
                Year = body.Year.Trim(),
                Title = body.Title.Trim(),
                Organization = body.Organization.Trim(),
                Description = body.Description.Trim(),
                Image = image,
                ImageBlobName = imageBlobName,
                CreatedAt = DateTime.UtcNow
            };

            await _awards.AddAsync(entity, ct);
            _cache.Remove(CacheKeyAll);
            _logger.LogInformation("CorrelationId {CorrelationId} - CreateAward completed. AwardId {AwardId}", correlationId, entity.Id);
            return await ResponseFactory.CreatedAsync(req, ToResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - CreateAward failed", correlationId);
            throw;
        }
    }

    [Function("UpdateAward")]
    [OpenApiOperation(operationId: "UpdateAward", tags: new[] { "Awards" }, Summary = "Update award (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(AwardUpdateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<AwardResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> UpdateAward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "options", Route = "awards/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - UpdateAward started. AwardId {AwardId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null) return authResult;

            var body = await req.ReadFromJsonAsync<AwardUpdateRequest>(cancellationToken: ct);
            if (body is null)
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");

            var errors = ValidatePayload(body.Year, body.Title, body.Organization, body.Description, body.Image);
            if (errors.Length > 0)
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", errors);

            var entity = await _awards.GetByIdAsync(id, ct);
            if (entity is null)
                return await ResponseFactory.NotFoundAsync(req, "Award not found.");

            var (image, imageBlobName) = await ResolveImageAsync(body.Image, entity.Image, entity.ImageBlobName, ct);

            entity.Year = body.Year.Trim();
            entity.Title = body.Title.Trim();
            entity.Organization = body.Organization.Trim();
            entity.Description = body.Description.Trim();
            entity.Image = image;
            entity.ImageBlobName = imageBlobName;

            await _awards.UpdateAsync(entity, ct);
            _cache.Remove(CacheKeyAll);
            _cache.Remove(CacheKeyById(id));
            _logger.LogInformation("CorrelationId {CorrelationId} - UpdateAward completed. AwardId {AwardId}", correlationId, id);
            return await ResponseFactory.OkAsync(req, ToResponse(entity), "Updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - UpdateAward failed", correlationId);
            throw;
        }
    }

    [Function("DeleteAward")]
    [OpenApiOperation(operationId: "DeleteAward", tags: new[] { "Awards" }, Summary = "Delete award (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> DeleteAward(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "awards/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - DeleteAward started. AwardId {AwardId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null) return authResult;

            var deleted = await _awards.DeleteAsync(id, ct);
            if (!deleted)
                return await ResponseFactory.NotFoundAsync(req, "Award not found.");

            _cache.Remove(CacheKeyAll);
            _cache.Remove(CacheKeyById(id));
            _logger.LogInformation("CorrelationId {CorrelationId} - DeleteAward completed. AwardId {AwardId}", correlationId, id);
            return await ResponseFactory.NoContentAsync(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - DeleteAward failed", correlationId);
            throw;
        }
    }

    private Task<HttpResponseData?> ValidateAdminAsync(HttpRequestData req, Guid correlationId) =>
        Task.FromResult<HttpResponseData?>(null);

    private AwardResponse ToResponse(Award entity) => new()
    {
        Id = entity.Id,
        Year = entity.Year,
        Title = entity.Title,
        Organization = entity.Organization,
        Description = entity.Description,
        Image = ResolveImageUrl(entity),
        CreatedAt = entity.CreatedAt
    };

    private string ResolveImageUrl(Award entity) =>
        !string.IsNullOrWhiteSpace(entity.ImageBlobName)
            ? _blobStorage.GetReadSasUrl(entity.ImageBlobName)
            : entity.Image;

    private static string[] ValidatePayload(string? year, string? title, string? organization, string? description, string? image)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(year))
            errors.Add("Year is required.");
        else if (year.Trim().Length > YearMaxLength)
            errors.Add($"Year cannot exceed {YearMaxLength} characters.");

        if (string.IsNullOrWhiteSpace(title))
            errors.Add("Title is required.");
        else if (title.Trim().Length > TitleMaxLength)
            errors.Add($"Title cannot exceed {TitleMaxLength} characters.");

        if (string.IsNullOrWhiteSpace(organization))
            errors.Add("Organization is required.");
        else if (organization.Trim().Length > OrgMaxLength)
            errors.Add($"Organization cannot exceed {OrgMaxLength} characters.");

        if (string.IsNullOrWhiteSpace(description))
            errors.Add("Description is required.");

        if (!string.IsNullOrWhiteSpace(image))
        {
            var trimmed = image.Trim();
            if (!IsBase64Payload(trimmed) && !Uri.TryCreate(trimmed, UriKind.Absolute, out _))
                errors.Add("Image must be a valid URL or base64-encoded image.");
        }

        return errors.ToArray();
    }

    private async Task<(string Image, string ImageBlobName)> ResolveImageAsync(
        string? image, string existingImage, string existingImageBlobName, CancellationToken ct)
    {
        var incoming = image?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(incoming)) return (existingImage, existingImageBlobName);

        if (IsBase64Payload(incoming))
        {
            var blobName = await _blobStorage.UploadBase64ImageAsync(incoming, ct);
            return (_blobStorage.GetReadSasUrl(blobName), blobName);
        }

        if (Uri.TryCreate(incoming, UriKind.Absolute, out _))
        {
            if (!string.IsNullOrWhiteSpace(existingImageBlobName))
                return (_blobStorage.GetReadSasUrl(existingImageBlobName), existingImageBlobName);
            return (incoming, string.Empty);
        }

        return (existingImage, existingImageBlobName);
    }

    private static bool IsBase64Payload(string value) =>
        value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
        && value.Contains(";base64,", StringComparison.OrdinalIgnoreCase);
}
