using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using TravelBlogger.Common;
using TravelBlogger.Contracts.Responses;
using TravelBlogger.Infrastructure.YouTube;

namespace TravelBlogger.Functions;

public sealed class VideosFunction
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;

    private readonly IYouTubeVideoService _youTube;
    private readonly ILogger<VideosFunction> _logger;

    public VideosFunction(IYouTubeVideoService youTube, ILogger<VideosFunction> logger)
    {
        _youTube = youTube;
        _logger = logger;
    }

    [Function("GetVideos")]
    [OpenApiOperation(operationId: "GetVideos", tags: new[] { "Videos" }, Summary = "Get all videos")]
    [OpenApiParameter("limit", In = ParameterLocation.Query, Required = false, Type = typeof(int), Summary = "Optional page size (1-50).")]
    [OpenApiParameter("pageToken", In = ParameterLocation.Query, Required = false, Type = typeof(string), Summary = "Optional YouTube continuation token.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<VideoListResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetVideos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "videos")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetVideos started", correlationId);

        try
        {
            var query = QueryHelpers.ParseQuery(req.Url.Query);
            var pageToken = ReadQueryValue(query, "pageToken");
            var limitRaw = ReadQueryValue(query, "limit");
            var hasPagingArgs = !string.IsNullOrWhiteSpace(limitRaw) || !string.IsNullOrWhiteSpace(pageToken);
            var parsedLimit = DefaultPageSize;

            if (!string.IsNullOrWhiteSpace(limitRaw)
                && (!int.TryParse(limitRaw, out parsedLimit) || parsedLimit < 1 || parsedLimit > MaxPageSize))
            {
                return await ResponseFactory.BadRequestAsync(req, $"Query parameter 'limit' must be between 1 and {MaxPageSize}.");
            }

            VideoListResponse response;
            if (hasPagingArgs)
            {
                var pageSize = string.IsNullOrWhiteSpace(limitRaw) ? DefaultPageSize : parsedLimit;
                response = await _youTube.GetVideosPageAsync(pageSize, pageToken, ct);
            }
            else
            {
                var allVideos = await _youTube.GetAllVideosAsync(ct);
                response = new VideoListResponse
                {
                    Items = allVideos.ToList(),
                    NextPageToken = null
                };
            }

            _logger.LogInformation(
                "CorrelationId {CorrelationId} - GetVideos completed. Count {Count}, HasMore {HasMore}",
                correlationId,
                response.Items.Count,
                response.HasMore);

            return await ResponseFactory.OkAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetVideos failed", correlationId);
            throw;
        }
    }

    private static string? ReadQueryValue(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
    {
        return query.TryGetValue(key, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;
    }
}
