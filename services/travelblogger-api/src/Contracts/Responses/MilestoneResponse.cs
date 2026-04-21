namespace TravelBlogger.Contracts.Responses;

public sealed class MilestoneResponse
{
    public Guid Id { get; set; }
    public string Year { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
