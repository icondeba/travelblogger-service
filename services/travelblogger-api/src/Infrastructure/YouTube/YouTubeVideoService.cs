using System.Net;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TravelBlogger.Contracts.Responses;

namespace TravelBlogger.Infrastructure.YouTube;

public sealed class YouTubeVideoService : IYouTubeVideoService
{
    private const int YouTubeApiMaxPageSize = 50;
    private const int MaxPageIterations = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly YouTubeOptions _options;

    public YouTubeVideoService(HttpClient httpClient, IOptions<YouTubeOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<VideoResponse>> GetAllVideosAsync(CancellationToken ct)
    {
        var pageSize = _options.MaxResults <= 0
            ? YouTubeApiMaxPageSize
            : Math.Clamp(_options.MaxResults, 1, YouTubeApiMaxPageSize);

        var videos = new List<VideoResponse>();
        string? nextPageToken = null;
        var iterations = 0;

        do
        {
            ct.ThrowIfCancellationRequested();

            var page = await GetVideosPageAsync(pageSize, nextPageToken, ct);
            videos.AddRange(page.Items);
            nextPageToken = page.NextPageToken;
            iterations++;
        }
        while (!string.IsNullOrWhiteSpace(nextPageToken) && iterations < MaxPageIterations);

        return videos;
    }

    public async Task<VideoListResponse> GetVideosPageAsync(int pageSize, string? pageToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ChannelId))
        {
            return new VideoListResponse();
        }

        var normalizedPageSize = Math.Clamp(pageSize, 1, YouTubeApiMaxPageSize);
        var query = new Dictionary<string, string?>
        {
            ["part"] = "snippet",
            ["channelId"] = _options.ChannelId,
            ["maxResults"] = normalizedPageSize.ToString(CultureInfo.InvariantCulture),
            ["order"] = "date",
            ["type"] = "video",
            ["key"] = _options.ApiKey
        };

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            query["pageToken"] = pageToken.Trim();
        }

        var requestQuery = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var requestUri = QueryHelpers.AddQueryString("https://www.googleapis.com/youtube/v3/search", requestQuery);

        using var response = await _httpClient.GetAsync(requestUri, ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new VideoListResponse();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<YouTubeSearchResponse>(stream, JsonOptions, ct);

        var items = payload?.Items?
            .Where(item => !string.IsNullOrWhiteSpace(item.Id?.VideoId))
            .Select(ToVideoResponse)
            .ToList() ?? new List<VideoResponse>();

        return new VideoListResponse
        {
            Items = items,
            NextPageToken = payload?.NextPageToken
        };
    }

    private static VideoResponse ToVideoResponse(YouTubeSearchItem item)
    {
        var videoId = item.Id?.VideoId ?? string.Empty;
        return new VideoResponse
        {
            Id = Guid.Empty,
            Title = item.Snippet?.Title ?? string.Empty,
            Description = item.Snippet?.Description ?? string.Empty,
            VideoId = videoId,
            YouTubeUrl = $"https://www.youtube.com/watch?v={videoId}",
            PublishedAt = item.Snippet?.PublishedAt
        };
    }

    private sealed class YouTubeSearchResponse
    {
        public string? NextPageToken { get; set; }
        public List<YouTubeSearchItem>? Items { get; set; }
    }

    private sealed class YouTubeSearchItem
    {
        public YouTubeSearchItemId? Id { get; set; }
        public YouTubeSearchSnippet? Snippet { get; set; }
    }

    private sealed class YouTubeSearchItemId
    {
        public string? VideoId { get; set; }
    }

    private sealed class YouTubeSearchSnippet
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
}
