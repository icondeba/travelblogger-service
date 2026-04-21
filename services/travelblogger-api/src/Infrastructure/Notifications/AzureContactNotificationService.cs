using Azure;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelBlogger.Common;
using TravelBlogger.Domain.Entities;

namespace TravelBlogger.Infrastructure.Notifications;

public sealed class AzureContactNotificationService : IContactNotificationService
{
    private readonly ContactNotificationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureContactNotificationService> _logger;

    public AzureContactNotificationService(
        IOptions<ContactNotificationOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureContactNotificationService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendOwnerNotificationAsync(ContactMessage message, CancellationToken ct)
    {
        var tasks = new List<Task>();

        if (_options.Email.Enabled && !string.IsNullOrWhiteSpace(_options.OwnerEmail))
            tasks.Add(SendOwnerEmailAsync(message, ct));

        if (_options.WhatsApp.Enabled)
            tasks.Add(SendWhatsAppAsync(message, ct));

        await Task.WhenAll(tasks);
    }

    public async Task SendReplyAsync(ContactMessage message, string replyMessage, CancellationToken ct)
    {
        var failures = new List<string>();

        if (_options.Email.Enabled)
        {
            try { await SendReplyEmailAsync(message, replyMessage, ct); }
            catch (Exception ex) { failures.Add($"Email notification failed: {ex.Message}"); }
        }

        if (_options.Sms.Enabled)
        {
            try { await SendSmsAsync(message, replyMessage, ct); }
            catch (Exception ex) { failures.Add($"SMS notification failed: {ex.Message}"); }
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join(" ", failures));
    }

    private async Task SendOwnerEmailAsync(ContactMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Email.ConnectionString))
        {
            _logger.LogWarning("Owner email skipped: ConnectionString not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Email.SenderAddress))
        {
            _logger.LogWarning("Owner email skipped: SenderAddress not configured.");
            return;
        }

        var subject = $"New contact message from {message.Name}";
        var html = $"""
            <h2>New Contact Message</h2>
            <table cellpadding="8">
              <tr><td><strong>Name</strong></td><td>{System.Net.WebUtility.HtmlEncode(message.Name)}</td></tr>
              <tr><td><strong>Email</strong></td><td>{System.Net.WebUtility.HtmlEncode(message.Email)}</td></tr>
              <tr><td><strong>Phone</strong></td><td>{System.Net.WebUtility.HtmlEncode(message.PhoneNumber)}</td></tr>
              <tr><td><strong>Message</strong></td><td>{System.Net.WebUtility.HtmlEncode(message.Message)}</td></tr>
            </table>
            """;
        var plainText = $"New contact from {message.Name}\nEmail: {message.Email}\nPhone: {message.PhoneNumber}\nMessage: {message.Message}";

        var emailClient = new EmailClient(_options.Email.ConnectionString);
        var emailMsg = new EmailMessage(
            senderAddress: _options.Email.SenderAddress,
            recipients: new EmailRecipients(new[] { new EmailAddress(_options.OwnerEmail) }),
            content: new EmailContent(subject) { PlainText = plainText, Html = html });

        await emailClient.SendAsync(WaitUntil.Completed, emailMsg, ct);
        _logger.LogInformation("Owner notification email sent to {OwnerEmail}", _options.OwnerEmail);
    }

    private async Task SendWhatsAppAsync(ContactMessage message, CancellationToken ct)
    {
        var opts = _options.WhatsApp;

        if (string.IsNullOrWhiteSpace(opts.PhoneNumber) || string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            _logger.LogWarning("WhatsApp notification skipped: PhoneNumber or ApiKey not configured.");
            return;
        }

        var body = $"New contact from {message.Name}%0AEmail: {message.Email}%0APhone: {message.PhoneNumber}%0AMessage: {Uri.EscapeDataString(message.Message)}";
        var url = $"https://api.callmebot.com/whatsapp.php?phone={opts.PhoneNumber}&text={body}&apikey={opts.ApiKey}";

        try
        {
            var client = _httpClientFactory.CreateClient("callmebot");
            using var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("CallMeBot returned {StatusCode}", response.StatusCode);
            else
                _logger.LogInformation("WhatsApp notification sent to {Phone}", opts.PhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp notification failed (non-fatal)");
        }
    }

    private async Task SendReplyEmailAsync(ContactMessage message, string replyMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Email.ConnectionString))
        {
            _logger.LogWarning("Reply email skipped: ConnectionString not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Email.SenderAddress))
        {
            _logger.LogWarning("Reply email skipped: SenderAddress not configured.");
            return;
        }

        var subject = "Reply to your contact message";
        var plainText = $"Hi {message.Name},{Environment.NewLine}{Environment.NewLine}{replyMessage}{Environment.NewLine}{Environment.NewLine}Thank you.";
        var html = $"<p>Hi {System.Net.WebUtility.HtmlEncode(message.Name)},</p><p>{System.Net.WebUtility.HtmlEncode(replyMessage)}</p><p>Thank you.</p>";

        var emailClient = new EmailClient(_options.Email.ConnectionString);
        var emailMsg = new EmailMessage(
            senderAddress: _options.Email.SenderAddress,
            recipients: new EmailRecipients(new[] { new EmailAddress(message.Email) }),
            content: new EmailContent(subject) { PlainText = plainText, Html = html });

        await emailClient.SendAsync(WaitUntil.Completed, emailMsg, ct);
    }

    private async Task SendSmsAsync(ContactMessage message, string replyMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Sms.ConnectionString))
            throw new InvalidOperationException("Notifications:Sms:ConnectionString is not configured.");

        if (string.IsNullOrWhiteSpace(_options.Sms.SenderPhoneNumber))
            throw new InvalidOperationException("Notifications:Sms:SenderPhoneNumber is not configured.");

        var smsClient = new SmsClient(_options.Sms.ConnectionString);
        Response<SmsSendResult> result = await smsClient.SendAsync(
            from: _options.Sms.SenderPhoneNumber,
            to: message.PhoneNumber,
            message: $"Travel Blogger reply: {replyMessage}",
            cancellationToken: ct);

        if (!result.Value.Successful)
            throw new InvalidOperationException(result.Value.ErrorMessage ?? "Unknown SMS error.");
    }
}
