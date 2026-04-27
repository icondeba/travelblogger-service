using System.Net;
using Microsoft.AspNetCore.WebUtilities;
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

public sealed class EventsFunction
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;
    private const int TitleMaxLength = 200;
    private const int LocationMaxLength = 200;
    private const int ImageMaxLength = 2048;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string VersionKey = "events:v";

    private readonly IEventRepository _events;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<EventsFunction> _logger;
    private readonly IMemoryCache _cache;

    public EventsFunction(IEventRepository eventsRepo, IBlobStorageService blobStorage, ILogger<EventsFunction> logger, IMemoryCache cache)
    {
        _events = eventsRepo;
        _blobStorage = blobStorage;
        _logger = logger;
        _cache = cache;
    }

    private long EventsVersion =>
        _cache.GetOrCreate(VersionKey, e => { e.Priority = CacheItemPriority.NeverRemove; return 0L; });

    private void InvalidateEventsCache()
    {
        _cache.TryGetValue(VersionKey, out long current);
        _cache.Set(VersionKey, current + 1, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
    }

    [Function("GetEvents")]
    [OpenApiOperation(operationId: "GetEvents", tags: new[] { "Events" }, Summary = "Get all events")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<List<EventResponse>>))]
    [OpenApiParameter("limit", In = ParameterLocation.Query, Required = false, Type = typeof(int), Summary = "Optional page size (1-50).")]
    [OpenApiParameter("offset", In = ParameterLocation.Query, Required = false, Type = typeof(int), Summary = "Optional start offset (>=0).")]
    public async Task<HttpResponseData> GetEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetEvents started", correlationId);

        try
        {
            var query = QueryHelpers.ParseQuery(req.Url.Query);
            var limitRaw = ReadQueryValue(query, "limit");
            var offsetRaw = ReadQueryValue(query, "offset");
            var hasPagingArgs = !string.IsNullOrWhiteSpace(limitRaw) || !string.IsNullOrWhiteSpace(offsetRaw);

            var pageSize = DefaultPageSize;
            var offset = 0;

            if (!string.IsNullOrWhiteSpace(limitRaw)
                && (!int.TryParse(limitRaw, out pageSize) || pageSize < 1 || pageSize > MaxPageSize))
            {
                return await ResponseFactory.BadRequestAsync(req, $"Query parameter 'limit' must be between 1 and {MaxPageSize}.");
            }

            if (!string.IsNullOrWhiteSpace(offsetRaw)
                && (!int.TryParse(offsetRaw, out offset) || offset < 0))
            {
                return await ResponseFactory.BadRequestAsync(req, "Query parameter 'offset' must be greater than or equal to 0.");
            }

            var v = EventsVersion;

            if (hasPagingArgs)
            {
                var pageCacheKey = $"events:{v}:page:{offset}:{pageSize}";
                if (_cache.TryGetValue(pageCacheKey, out EventListResponse? cachedPage) && cachedPage is not null)
                {
                    _logger.LogInformation("CorrelationId {CorrelationId} - GetEvents (paged) served from cache", correlationId);
                    return await ResponseFactory.OkCachedAsync(req, cachedPage);
                }

                var pageItems = await _events.GetPageAsync(offset, pageSize + 1, ct);
                var hasMore = pageItems.Count > pageSize;
                var events = (hasMore ? pageItems.Take(pageSize) : pageItems)
                    .Select(ToResponse)
                    .ToList();

                var pageResponse = new EventListResponse
                {
                    Items = events,
                    NextOffset = hasMore ? offset + pageSize : null
                };

                _cache.Set(pageCacheKey, pageResponse, CacheTtl);
                _logger.LogInformation(
                    "CorrelationId {CorrelationId} - GetEvents completed (paged). Count {Count}, HasMore {HasMore}",
                    correlationId,
                    pageResponse.Items.Count,
                    pageResponse.HasMore);

                return await ResponseFactory.OkCachedAsync(req, pageResponse);
            }

            var allCacheKey = $"events:{v}:all";
            if (_cache.TryGetValue(allCacheKey, out List<EventResponse>? cachedAll) && cachedAll is not null)
            {
                _logger.LogInformation("CorrelationId {CorrelationId} - GetEvents served from cache", correlationId);
                return await ResponseFactory.OkCachedAsync(req, cachedAll);
            }

            var items = await _events.GetAllAsync(ct);
            var response = items.Select(ToResponse).ToList();
            _cache.Set(allCacheKey, response, CacheTtl);

            _logger.LogInformation("CorrelationId {CorrelationId} - GetEvents completed", correlationId);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetEvents failed", correlationId);
            throw;
        }
    }

    private static string? ReadQueryValue(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
    {
        return query.TryGetValue(key, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;
    }

    [Function("GetEventById")]
    [OpenApiOperation(operationId: "GetEventById", tags: new[] { "Events" }, Summary = "Get event by id")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<EventResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetEventById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetEventById started. EventId {EventId}", correlationId, id);

        try
        {
            var cacheKey = $"events:{EventsVersion}:id:{id}";
            if (_cache.TryGetValue(cacheKey, out EventResponse? cached) && cached is not null)
                return await ResponseFactory.OkCachedAsync(req, cached);

            var entity = await _events.GetByIdAsync(id, ct);
            if (entity is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - GetEventById not found. EventId {EventId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Event not found.");
            }

            var response = ToResponse(entity);
            _cache.Set(cacheKey, response, CacheTtl);
            _logger.LogInformation("CorrelationId {CorrelationId} - GetEventById completed. EventId {EventId}", correlationId, id);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetEventById failed. EventId {EventId}", correlationId, id);
            throw;
        }
    }

    [Function("CreateEvent")]
    [OpenApiOperation(operationId: "CreateEvent", tags: new[] { "Events" }, Summary = "Create event (admin)")]
    [OpenApiRequestBody("application/json", typeof(EventCreateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<EventResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> CreateEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "events")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - CreateEvent started", correlationId);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateEvent unauthorized", correlationId);
                return authResult;
            }

            var body = await req.ReadFromJsonAsync<EventCreateRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateEvent invalid request body", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var validationErrors = ValidateEventPayload(body.Title, body.Location, body.Description, body.Image);
            if (validationErrors.Length > 0)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateEvent validation failed", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", validationErrors);
            }

            var (image, imageBlobName) = await ResolveImageAsync(body.Image, string.Empty, string.Empty, ct);

            var entity = new Event
            {
                Id = Guid.NewGuid(),
                Title = body.Title.Trim(),
                Description = body.Description.Trim(),
                Location = body.Location.Trim(),
                Image = image,
                ImageBlobName = imageBlobName,
                EventDate = body.EventDate,
                CreatedAt = DateTime.UtcNow
            };

            await _events.AddAsync(entity, ct);
            InvalidateEventsCache();

            _logger.LogInformation("CorrelationId {CorrelationId} - CreateEvent completed. EventId {EventId}", correlationId, entity.Id);
            return await ResponseFactory.CreatedAsync(req, ToResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - CreateEvent failed", correlationId);
            throw;
        }
    }

    [Function("UpdateEvent")]
    [OpenApiOperation(operationId: "UpdateEvent", tags: new[] { "Events" }, Summary = "Update event (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(EventUpdateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<EventResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> UpdateEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "options", Route = "events/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - UpdateEvent started. EventId {EventId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateEvent unauthorized. EventId {EventId}", correlationId, id);
                return authResult;
            }

            var body = await req.ReadFromJsonAsync<EventUpdateRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateEvent invalid request body. EventId {EventId}", correlationId, id);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var validationErrors = ValidateEventPayload(body.Title, body.Location, body.Description, body.Image);
            if (validationErrors.Length > 0)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateEvent validation failed. EventId {EventId}", correlationId, id);
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", validationErrors);
            }

            var entity = await _events.GetByIdAsync(id, ct);
            if (entity is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateEvent not found. EventId {EventId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Event not found.");
            }

            var (image, imageBlobName) = await ResolveImageAsync(body.Image, entity.Image, entity.ImageBlobName, ct);

            entity.Title = body.Title.Trim();
            entity.Description = body.Description.Trim();
            entity.Location = body.Location.Trim();
            entity.Image = image;
            entity.ImageBlobName = imageBlobName;
            entity.EventDate = body.EventDate;

            await _events.UpdateAsync(entity, ct);
            InvalidateEventsCache();

            _logger.LogInformation("CorrelationId {CorrelationId} - UpdateEvent completed. EventId {EventId}", correlationId, id);
            return await ResponseFactory.OkAsync(req, ToResponse(entity), "Updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - UpdateEvent failed. EventId {EventId}", correlationId, id);
            throw;
        }
    }

    [Function("DeleteEvent")]
    [OpenApiOperation(operationId: "DeleteEvent", tags: new[] { "Events" }, Summary = "Delete event (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> DeleteEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "events/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - DeleteEvent started. EventId {EventId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - DeleteEvent unauthorized. EventId {EventId}", correlationId, id);
                return authResult;
            }

            var deleted = await _events.DeleteAsync(id, ct);
            if (!deleted)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - DeleteEvent not found. EventId {EventId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Event not found.");
            }

            InvalidateEventsCache();
            _logger.LogInformation("CorrelationId {CorrelationId} - DeleteEvent completed. EventId {EventId}", correlationId, id);
            return await ResponseFactory.NoContentAsync(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - DeleteEvent failed. EventId {EventId}", correlationId, id);
            throw;
        }
    }

    private Task<HttpResponseData?> ValidateAdminAsync(HttpRequestData req, Guid correlationId)
    {
        return Task.FromResult<HttpResponseData?>(null);
    }

    private EventResponse ToResponse(Event entity)
    {
        return new EventResponse
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            Location = entity.Location,
            Image = ResolveImageUrl(entity),
            EventDate = entity.EventDate,
            CreatedAt = entity.CreatedAt
        };
    }

    private static string[] ValidateEventPayload(string? title, string? location, string? description, string? image)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Title is required.");
        }
        else if (title.Trim().Length > TitleMaxLength)
        {
            errors.Add($"Title cannot exceed {TitleMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            errors.Add("Location is required.");
        }
        else if (location.Trim().Length > LocationMaxLength)
        {
            errors.Add($"Location cannot exceed {LocationMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add("Description is required.");
        }

        if (!string.IsNullOrWhiteSpace(image))
        {
            var trimmedImage = image.Trim();
            if (!IsBase64Payload(trimmedImage))
            {
                if (!Uri.TryCreate(trimmedImage, UriKind.Absolute, out _))
                {
                    errors.Add("Image must be a valid URL or base64-encoded image.");
                }
                else if (trimmedImage.Length > ImageMaxLength)
                {
                    errors.Add($"Image URL cannot exceed {ImageMaxLength} characters.");
                }
            }
        }

        return errors.ToArray();
    }

    private async Task<(string Image, string ImageBlobName)> ResolveImageAsync(
        string? image,
        string existingImage,
        string existingImageBlobName,
        CancellationToken ct)
    {
        var incomingImage = image?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(incomingImage))
        {
            return (existingImage, existingImageBlobName);
        }

        if (IsBase64Payload(incomingImage))
        {
            var imageBlobName = await _blobStorage.UploadBase64ImageAsync(incomingImage, ct);
            return (_blobStorage.GetReadSasUrl(imageBlobName), imageBlobName);
        }

        if (Uri.TryCreate(incomingImage, UriKind.Absolute, out _))
        {
            if (!string.IsNullOrWhiteSpace(existingImageBlobName))
            {
                return (_blobStorage.GetReadSasUrl(existingImageBlobName), existingImageBlobName);
            }

            return (incomingImage, string.Empty);
        }

        return (existingImage, existingImageBlobName);
    }

    private string ResolveImageUrl(Event entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ImageBlobName))
        {
            return _blobStorage.GetReadSasUrl(entity.ImageBlobName);
        }

        return entity.Image;
    }

    private static bool IsBase64Payload(string value)
    {
        return value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            && value.Contains(";base64,", StringComparison.OrdinalIgnoreCase);
    }
}
