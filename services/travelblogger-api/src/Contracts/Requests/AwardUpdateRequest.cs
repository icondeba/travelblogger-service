namespace TravelBlogger.Contracts.Requests;

public sealed class AwardUpdateRequest
{
    public string Year { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}
