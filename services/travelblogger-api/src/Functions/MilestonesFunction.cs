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

namespace TravelBlogger.Functions;

public sealed class MilestonesFunction
{
    private const int TitleMaxLength = 200;
    private const int YearMaxLength = 20;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CacheKeyAll = "milestones:all";
    private static string CacheKeyById(Guid id) => $"milestones:{id}";

    private readonly IMilestoneRepository _milestones;
    private readonly ILogger<MilestonesFunction> _logger;
    private readonly IMemoryCache _cache;

    public MilestonesFunction(IMilestoneRepository milestonesRepo, ILogger<MilestonesFunction> logger, IMemoryCache cache)
    {
        _milestones = milestonesRepo;
        _logger = logger;
        _cache = cache;
    }

    [Function("GetMilestones")]
    [OpenApiOperation(operationId: "GetMilestones", tags: new[] { "Milestones" }, Summary = "Get all milestones")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<List<MilestoneResponse>>))]
    public async Task<HttpResponseData> GetMilestones(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "milestones")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetMilestones started", correlationId);

        try
        {
            if (_cache.TryGetValue(CacheKeyAll, out List<MilestoneResponse>? cached) && cached is not null)
            {
                _logger.LogInformation("CorrelationId {CorrelationId} - GetMilestones served from cache. Count {Count}", correlationId, cached.Count);
                return await ResponseFactory.OkCachedAsync(req, cached);
            }

            var items = await _milestones.GetAllAsync(ct);
            var response = items.Select(ToResponse).ToList();
            _cache.Set(CacheKeyAll, response, CacheTtl);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetMilestones failed", correlationId);
            throw;
        }
    }

    [Function("GetMilestoneById")]
    [OpenApiOperation(operationId: "GetMilestoneById", tags: new[] { "Milestones" }, Summary = "Get milestone by id")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<MilestoneResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetMilestoneById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "milestones/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetMilestoneById started. MilestoneId {MilestoneId}", correlationId, id);

        try
        {
            var cacheKey = CacheKeyById(id);
            if (_cache.TryGetValue(cacheKey, out MilestoneResponse? cached) && cached is not null)
                return await ResponseFactory.OkCachedAsync(req, cached);

            var entity = await _milestones.GetByIdAsync(id, ct);
            if (entity is null)
                return await ResponseFactory.NotFoundAsync(req, "Milestone not found.");

            var response = ToResponse(entity);
            _cache.Set(cacheKey, response, CacheTtl);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetMilestoneById failed", correlationId);
            throw;
        }
    }

    [Function("CreateMilestone")]
    [OpenApiOperation(operationId: "CreateMilestone", tags: new[] { "Milestones" }, Summary = "Create milestone (admin)")]
    [OpenApiRequestBody("application/json", typeof(MilestoneCreateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<MilestoneResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> CreateMilestone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "milestones")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - CreateMilestone started", correlationId);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null) return authResult;

            var body = await req.ReadFromJsonAsync<MilestoneCreateRequest>(cancellationToken: ct);
            if (body is null)
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");

            var errors = ValidatePayload(body.Year, body.Title, body.Description);
            if (errors.Length > 0)
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", errors);

            var entity = new Milestone
            {
                Id = Guid.NewGuid(),
                Year = body.Year.Trim(),
                Title = body.Title.Trim(),
                Description = body.Description.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await _milestones.AddAsync(entity, ct);
            _cache.Remove(CacheKeyAll);
            _logger.LogInformation("CorrelationId {CorrelationId} - CreateMilestone completed. MilestoneId {MilestoneId}", correlationId, entity.Id);
            return await ResponseFactory.CreatedAsync(req, ToResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - CreateMilestone failed", correlationId);
            throw;
        }
    }

    [Function("UpdateMilestone")]
    [OpenApiOperation(operationId: "UpdateMilestone", tags: new[] { "Milestones" }, Summary = "Update milestone (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(MilestoneUpdateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<MilestoneResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> UpdateMilestone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "options", Route = "milestones/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - UpdateMilestone started. MilestoneId {MilestoneId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null) return authResult;

            var body = await req.ReadFromJsonAsync<MilestoneUpdateRequest>(cancellationToken: ct);
            if (body is null)
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");

            var errors = ValidatePayload(body.Year, body.Title, body.Description);
            if (errors.Length > 0)
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", errors);

            var entity = await _milestones.GetByIdAsync(id, ct);
            if (entity is null)
                return await ResponseFactory.NotFoundAsync(req, "Milestone not found.");

            entity.Year = body.Year.Trim();
            entity.Title = body.Title.Trim();
            entity.Description = body.Description.Trim();

            await _milestones.UpdateAsync(entity, ct);
            _cache.Remove(CacheKeyAll);
            _cache.Remove(CacheKeyById(id));
            _logger.LogInformation("CorrelationId {CorrelationId} - UpdateMilestone completed. MilestoneId {MilestoneId}", correlationId, id);
            return await ResponseFactory.OkAsync(req, ToResponse(entity), "Updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - UpdateMilestone failed", correlationId);
            throw;
        }
    }

    [Function("DeleteMilestone")]
    [OpenApiOperation(operationId: "DeleteMilestone", tags: new[] { "Milestones" }, Summary = "Delete milestone (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> DeleteMilestone(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "milestones/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - DeleteMilestone started. MilestoneId {MilestoneId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null) return authResult;

            var deleted = await _milestones.DeleteAsync(id, ct);
            if (!deleted)
                return await ResponseFactory.NotFoundAsync(req, "Milestone not found.");

            _cache.Remove(CacheKeyAll);
            _cache.Remove(CacheKeyById(id));
            _logger.LogInformation("CorrelationId {CorrelationId} - DeleteMilestone completed. MilestoneId {MilestoneId}", correlationId, id);
            return await ResponseFactory.NoContentAsync(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - DeleteMilestone failed", correlationId);
            throw;
        }
    }

    private Task<HttpResponseData?> ValidateAdminAsync(HttpRequestData req, Guid correlationId) =>
        Task.FromResult<HttpResponseData?>(null);

    private static MilestoneResponse ToResponse(Milestone entity) => new()
    {
        Id = entity.Id,
        Year = entity.Year,
        Title = entity.Title,
        Description = entity.Description,
        CreatedAt = entity.CreatedAt
    };

    private static string[] ValidatePayload(string? year, string? title, string? description)
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

        if (string.IsNullOrWhiteSpace(description))
            errors.Add("Description is required.");

        return errors.ToArray();
    }
}
