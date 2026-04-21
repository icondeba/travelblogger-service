using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Notifications;

public interface IContactNotificationService
{
    Task SendOwnerNotificationAsync(ContactMessage message, CancellationToken ct);
    Task SendReplyAsync(ContactMessage message, string replyMessage, CancellationToken ct);
}
