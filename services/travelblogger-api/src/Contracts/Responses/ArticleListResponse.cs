namespace TravelBlogger.Contracts.Responses;

public sealed class ArticleListResponse
{
    public List<ArticleResponse> Items { get; set; } = new();
    public int? NextOffset { get; set; }
    public bool HasMore => NextOffset.HasValue;
}
