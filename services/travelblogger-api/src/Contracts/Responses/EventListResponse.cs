namespace TravelBlogger.Contracts.Responses;

public sealed class EventListResponse
{
    public List<EventResponse> Items { get; set; } = new();
    public int? NextOffset { get; set; }
    public bool HasMore => NextOffset.HasValue;
}
