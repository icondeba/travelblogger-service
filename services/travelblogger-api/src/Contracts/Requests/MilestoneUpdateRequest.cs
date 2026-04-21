namespace TravelBlogger.Contracts.Requests;

public sealed class MilestoneUpdateRequest
{
    public string Year { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
