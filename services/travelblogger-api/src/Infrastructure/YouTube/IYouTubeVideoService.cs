using TravelBlogger.Contracts.Responses;

namespace TravelBlogger.Infrastructure.YouTube;

public interface IYouTubeVideoService
{
    Task<IReadOnlyList<VideoResponse>> GetAllVideosAsync(CancellationToken ct);
    Task<VideoListResponse> GetVideosPageAsync(int pageSize, string? pageToken, CancellationToken ct);
}
