using Azure;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Microsoft.Extensions.Options;
using TravelBlogger.Common;
using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Notifications;

public sealed class AzureContactNotificationService : IContactNotificationService
{
    private readonly ContactNotificationOptions _options;

    public AzureContactNotificationService(IOptions<ContactNotificationOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendReplyAsync(ContactMessage message, string replyMessage, CancellationToken ct)
    {
        var failures = new List<string>();

        if (_options.Email.Enabled)
        {
            try
            {
                await SendEmailAsync(message, replyMessage, ct);
            }
            catch (Exception ex)
            {
                failures.Add($"Email notification failed: {ex.Message}");
            }
        }

        if (_options.Sms.Enabled)
        {
            try
            {
                await SendSmsAsync(message, replyMessage, ct);
            }
            catch (Exception ex)
            {
                failures.Add($"SMS notification failed: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", failures));
        }
    }

    private async Task SendEmailAsync(ContactMessage message, string replyMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Email.ConnectionString))
        {
            throw new InvalidOperationException("Notifications:Email:ConnectionString is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Email.SenderAddress))
        {
            throw new InvalidOperationException("Notifications:Email:SenderAddress is not configured.");
        }

        var subject = "Reply to your Travel Blogger contact message";
        var plainText = $"Hi {message.Name},{Environment.NewLine}{Environment.NewLine}{replyMessage}{Environment.NewLine}{Environment.NewLine}Thank you.";
        var html =
            $"<p>Hi {System.Net.WebUtility.HtmlEncode(message.Name)},</p><p>{System.Net.WebUtility.HtmlEncode(replyMessage)}</p><p>Thank you.</p>";

        var emailClient = new EmailClient(_options.Email.ConnectionString);
        var emailMessage = new EmailMessage(
            senderAddress: _options.Email.SenderAddress,
            recipients: new EmailRecipients(new[] { new EmailAddress(message.Email) }),
            content: new EmailContent(subject)
            {
                PlainText = plainText,
                Html = html
            });

        await emailClient.SendAsync(WaitUntil.Completed, emailMessage, ct);
    }

    private async Task SendSmsAsync(ContactMessage message, string replyMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Sms.ConnectionString))
        {
            throw new InvalidOperationException("Notifications:Sms:ConnectionString is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Sms.SenderPhoneNumber))
        {
            throw new InvalidOperationException("Notifications:Sms:SenderPhoneNumber is not configured.");
        }

        var smsClient = new SmsClient(_options.Sms.ConnectionString);
        Response<SmsSendResult> result = await smsClient.SendAsync(
            from: _options.Sms.SenderPhoneNumber,
            to: message.PhoneNumber,
            message: $"Travel Blogger reply: {replyMessage}",
            cancellationToken: ct);

        if (!result.Value.Successful)
        {
            throw new InvalidOperationException(result.Value.ErrorMessage ?? "Unknown SMS sending error.");
        }
    }
}
