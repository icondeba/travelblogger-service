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

public sealed class ArticlesFunction
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;
    private const int TitleMaxLength = 200;
    private const int SlugMaxLength = 200;
    private const int ExcerptMaxLength = 500;
    private const int ImageMaxLength = 2048;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string VersionKey = "articles:v";

    private readonly IArticleRepository _articles;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<ArticlesFunction> _logger;
    private readonly IMemoryCache _cache;

    public ArticlesFunction(IArticleRepository articles, IBlobStorageService blobStorage, ILogger<ArticlesFunction> logger, IMemoryCache cache)
    {
        _articles = articles;
        _blobStorage = blobStorage;
        _logger = logger;
        _cache = cache;
    }

    private long ArticlesVersion =>
        _cache.GetOrCreate(VersionKey, e => { e.Priority = CacheItemPriority.NeverRemove; return 0L; });

    private void InvalidateArticlesCache()
    {
        _cache.TryGetValue(VersionKey, out long current);
        _cache.Set(VersionKey, current + 1, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
    }

    [Function("GetArticles")]
    [OpenApiOperation(operationId: "GetArticles", tags: new[] { "Articles" }, Summary = "Get all articles")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<List<ArticleResponse>>))]
    [OpenApiParameter("limit", In = ParameterLocation.Query, Required = false, Type = typeof(int), Summary = "Optional page size (1-50).")]
    [OpenApiParameter("offset", In = ParameterLocation.Query, Required = false, Type = typeof(int), Summary = "Optional start offset (>=0).")]
    [OpenApiParameter("publishedOnly", In = ParameterLocation.Query, Required = false, Type = typeof(bool), Summary = "Optional flag to return only published stories.")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
     {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetArticles started", correlationId);

        try
        {
            var query = QueryHelpers.ParseQuery(req.Url.Query);
            var limitRaw = ReadQueryValue(query, "limit");
            var offsetRaw = ReadQueryValue(query, "offset");
            var publishedOnlyRaw = ReadQueryValue(query, "publishedOnly");

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

            var publishedOnly = false;
            if (!string.IsNullOrWhiteSpace(publishedOnlyRaw))
            {
                if (!bool.TryParse(publishedOnlyRaw, out publishedOnly))
                {
                    return await ResponseFactory.BadRequestAsync(req, "Query parameter 'publishedOnly' must be true or false.");
                }
            }

            var v = ArticlesVersion;

            if (hasPagingArgs)
            {
                var pageCacheKey = $"articles:{v}:page:{offset}:{pageSize}:{publishedOnly}";
                if (_cache.TryGetValue(pageCacheKey, out ArticleListResponse? cachedPage) && cachedPage is not null)
                {
                    _logger.LogInformation("CorrelationId {CorrelationId} - GetArticles (paged) served from cache", correlationId);
                    return await ResponseFactory.OkCachedAsync(req, cachedPage);
                }

                var pageItems = await _articles.GetPageAsync(offset, pageSize + 1, publishedOnly, ct);
                var hasMore = pageItems.Count > pageSize;
                var articles = (hasMore ? pageItems.Take(pageSize) : pageItems)
                    .Select(ToResponse)
                    .ToList();

                var pageResponse = new ArticleListResponse
                {
                    Items = articles,
                    NextOffset = hasMore ? offset + pageSize : null
                };

                _cache.Set(pageCacheKey, pageResponse, CacheTtl);
                _logger.LogInformation(
                    "CorrelationId {CorrelationId} - GetArticles completed (paged). Count {Count}, HasMore {HasMore}",
                    correlationId,
                    pageResponse.Items.Count,
                    pageResponse.HasMore);

                return await ResponseFactory.OkCachedAsync(req, pageResponse);
            }

            var allCacheKey = $"articles:{v}:all";
            if (_cache.TryGetValue(allCacheKey, out List<ArticleResponse>? cachedAll) && cachedAll is not null)
            {
                _logger.LogInformation("CorrelationId {CorrelationId} - GetArticles served from cache", correlationId);
                return await ResponseFactory.OkCachedAsync(req, cachedAll);
            }

            var items = await _articles.GetAllAsync(ct);
            var response = items.Select(ToResponse).ToList();
            _cache.Set(allCacheKey, response, CacheTtl);

            _logger.LogInformation("CorrelationId {CorrelationId} - GetArticles completed", correlationId);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetArticles failed", correlationId);
            throw;
        }
    }

    private static string? ReadQueryValue(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
    {
        return query.TryGetValue(key, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;
    }

    [Function("GetArticleById")]
    [OpenApiOperation(operationId: "GetArticleById", tags: new[] { "Articles" }, Summary = "Get article by id")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<ArticleResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetArticleById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/id/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetArticleById started. ArticleId {ArticleId}", correlationId, id);

        try
        {
            var cacheKey = $"articles:{ArticlesVersion}:id:{id}";
            if (_cache.TryGetValue(cacheKey, out ArticleResponse? cached) && cached is not null)
                return await ResponseFactory.OkCachedAsync(req, cached);

            var article = await _articles.GetByIdAsync(id, ct);
            if (article is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - GetArticleById not found. ArticleId {ArticleId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Article not found.");
            }

            var response = ToResponse(article);
            _cache.Set(cacheKey, response, CacheTtl);
            _logger.LogInformation("CorrelationId {CorrelationId} - GetArticleById completed. ArticleId {ArticleId}", correlationId, id);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetArticleById failed. ArticleId {ArticleId}", correlationId, id);
            throw;
        }
    }

    [Function("GetArticleBySlug")]
    [OpenApiOperation(operationId: "GetArticleBySlug", tags: new[] { "Articles" }, Summary = "Get article by slug")]
    [OpenApiParameter("slug", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<ArticleResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetArticleBySlug started. Slug {Slug}", correlationId, slug);

        try
        {
            var cacheKey = $"articles:{ArticlesVersion}:slug:{slug}";
            if (_cache.TryGetValue(cacheKey, out ArticleResponse? cached) && cached is not null)
                return await ResponseFactory.OkCachedAsync(req, cached);

            var article = await _articles.GetBySlugAsync(slug, ct);
            if (article is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - GetArticleBySlug not found. Slug {Slug}", correlationId, slug);
                return await ResponseFactory.NotFoundAsync(req, "Article not found.");
            }

            var response = ToResponse(article);
            _cache.Set(cacheKey, response, CacheTtl);
            _logger.LogInformation("CorrelationId {CorrelationId} - GetArticleBySlug completed. ArticleId {ArticleId}", correlationId, article.Id);
            return await ResponseFactory.OkCachedAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetArticleBySlug failed. Slug {Slug}", correlationId, slug);
            throw;
        }
    }

    [Function("CreateArticle")]
    [OpenApiOperation(operationId: "CreateArticle", tags: new[] { "Articles" }, Summary = "Create article (admin)")]
    [OpenApiRequestBody("application/json", typeof(ArticleCreateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<ArticleResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> CreateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - CreateArticle started", correlationId);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateArticle unauthorized", correlationId);
                return authResult;
            }

            var body = await req.ReadFromJsonAsync<ArticleCreateRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateArticle invalid request body", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var validationErrors = ValidateArticlePayload(body.Title, body.Slug, body.Excerpt, body.Content, body.Status, body.Image);
            if (validationErrors.Length > 0)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateArticle validation failed", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", validationErrors);
            }

            var normalizedSlug = NormalizeSlug(body.Slug);
            var slugExists = await _articles.SlugExistsAsync(normalizedSlug, null, ct);
            if (slugExists)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateArticle duplicate slug {Slug}", correlationId, normalizedSlug);
                return await ResponseFactory.BadRequestAsync(req, "An article with the same slug already exists.");
            }

            if (!Enum.TryParse<ArticleStatus>(body.Status.Trim(), true, out var status))
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateArticle invalid status {Status}", correlationId, body.Status);
                return await ResponseFactory.BadRequestAsync(req, "Invalid article status.");
            }

            var (image, imageBlobName) = await ResolveImageAsync(body.Image, string.Empty, string.Empty, ct);

            var entity = new Article
            {
                Id = Guid.NewGuid(),
                Title = body.Title.Trim(),
                Slug = normalizedSlug,
                Content = body.Content.Trim(),
                Excerpt = body.Excerpt.Trim(),
                Image = image,
                ImageBlobName = imageBlobName,
                Status = status,
                PublishedAt = body.PublishedAt,
                CreatedAt = DateTime.UtcNow
            };

            await _articles.AddAsync(entity, ct);
            InvalidateArticlesCache();

            _logger.LogInformation("CorrelationId {CorrelationId} - CreateArticle completed. ArticleId {ArticleId}", correlationId, entity.Id);
            return await ResponseFactory.CreatedAsync(req, ToResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - CreateArticle failed", correlationId);
            throw;
        }
    }

    [Function("UpdateArticle")]
    [OpenApiOperation(operationId: "UpdateArticle", tags: new[] { "Articles" }, Summary = "Update article (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(ArticleUpdateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<ArticleResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> UpdateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "options", Route = "articles/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - UpdateArticle started. ArticleId {ArticleId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateArticle unauthorized. ArticleId {ArticleId}", correlationId, id);
                return authResult;
            }

            var body = await req.ReadFromJsonAsync<ArticleUpdateRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateArticle invalid request body. ArticleId {ArticleId}", correlationId, id);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var validationErrors = ValidateArticlePayload(body.Title, body.Slug, body.Excerpt, body.Content, body.Status, body.Image);
            if (validationErrors.Length > 0)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateArticle validation failed. ArticleId {ArticleId}", correlationId, id);
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", validationErrors);
            }

            if (!Enum.TryParse<ArticleStatus>(body.Status.Trim(), true, out var status))
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateArticle invalid status {Status}. ArticleId {ArticleId}", correlationId, body.Status, id);
                return await ResponseFactory.BadRequestAsync(req, "Invalid article status.");
            }

            var entity = await _articles.GetByIdAsync(id, ct);
            if (entity is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateArticle not found. ArticleId {ArticleId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Article not found.");
            }

            var normalizedSlug = NormalizeSlug(body.Slug);
            var slugExists = await _articles.SlugExistsAsync(normalizedSlug, entity.Id, ct);
            if (slugExists)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - UpdateArticle duplicate slug {Slug}. ArticleId {ArticleId}", correlationId, normalizedSlug, id);
                return await ResponseFactory.BadRequestAsync(req, "An article with the same slug already exists.");
            }

            var (image, imageBlobName) = await ResolveImageAsync(body.Image, entity.Image, entity.ImageBlobName, ct);

            entity.Title = body.Title.Trim();
            entity.Slug = normalizedSlug;
            entity.Content = body.Content.Trim();
            entity.Excerpt = body.Excerpt.Trim();
            entity.Image = image;
            entity.ImageBlobName = imageBlobName;
            entity.Status = status;
            entity.PublishedAt = body.PublishedAt;

            await _articles.UpdateAsync(entity, ct);
            InvalidateArticlesCache();

            _logger.LogInformation("CorrelationId {CorrelationId} - UpdateArticle completed. ArticleId {ArticleId}", correlationId, id);
            return await ResponseFactory.OkAsync(req, ToResponse(entity), "Updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - UpdateArticle failed. ArticleId {ArticleId}", correlationId, id);
            throw;
        }
    }

    [Function("DeleteArticle")]
    [OpenApiOperation(operationId: "DeleteArticle", tags: new[] { "Articles" }, Summary = "Delete article (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> DeleteArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "articles/{id:guid}")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - DeleteArticle started. ArticleId {ArticleId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - DeleteArticle unauthorized. ArticleId {ArticleId}", correlationId, id);
                return authResult;
            }

            var deleted = await _articles.DeleteAsync(id, ct);
            if (!deleted)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - DeleteArticle not found. ArticleId {ArticleId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Article not found.");
            }

            InvalidateArticlesCache();
            _logger.LogInformation("CorrelationId {CorrelationId} - DeleteArticle completed. ArticleId {ArticleId}", correlationId, id);
            return await ResponseFactory.NoContentAsync(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - DeleteArticle failed. ArticleId {ArticleId}", correlationId, id);
            throw;
        }
    }

    private Task<HttpResponseData?> ValidateAdminAsync(HttpRequestData req, Guid correlationId)
    {
        return Task.FromResult<HttpResponseData?>(null);
    }

    private ArticleResponse ToResponse(Article article)
    {
        return new ArticleResponse
        {
            Id = article.Id,
            Title = article.Title,
            Slug = article.Slug,
            Content = article.Content,
            Excerpt = article.Excerpt,
            Image = ResolveImageUrl(article),
            Status = article.Status,
            PublishedAt = article.PublishedAt,
            CreatedAt = article.CreatedAt
        };
    }

    private static string[] ValidateArticlePayload(string? title, string? slug, string? excerpt, string? content, string? status, string? image)
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

        if (string.IsNullOrWhiteSpace(slug))
        {
            errors.Add("Slug is required.");
        }
        else if (slug.Trim().Length > SlugMaxLength)
        {
            errors.Add($"Slug cannot exceed {SlugMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(excerpt))
        {
            errors.Add("Excerpt is required.");
        }
        else if (excerpt.Trim().Length > ExcerptMaxLength)
        {
            errors.Add($"Excerpt cannot exceed {ExcerptMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            errors.Add("Content is required.");
        }

        if (string.IsNullOrWhiteSpace(status) || !Enum.TryParse<ArticleStatus>(status.Trim(), true, out _))
        {
            errors.Add("Status must be Draft or Published.");
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

    private static string NormalizeSlug(string slug)
    {
        return slug.Trim().ToLowerInvariant();
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

    private string ResolveImageUrl(Article article)
    {
        if (!string.IsNullOrWhiteSpace(article.ImageBlobName))
        {
            return _blobStorage.GetReadSasUrl(article.ImageBlobName);
        }

        return article.Image;
    }

    private static bool IsBase64Payload(string value)
    {
        return value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            && value.Contains(";base64,", StringComparison.OrdinalIgnoreCase);
    }
}
