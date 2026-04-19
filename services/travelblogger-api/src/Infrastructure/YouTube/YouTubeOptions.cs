namespace TravelBlogger.Infrastructure.YouTube;

public sealed class YouTubeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 10;
}
