using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Notifications;

public interface IContactNotificationService
{
    Task SendReplyAsync(ContactMessage message, string replyMessage, CancellationToken ct);
}
