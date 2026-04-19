namespace TravelBlogger.Common;

public sealed class ContactNotificationOptions
{
    public EmailNotificationOptions Email { get; set; } = new();
    public SmsNotificationOptions Sms { get; set; } = new();
}

public sealed class EmailNotificationOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
}

public sealed class SmsNotificationOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string SenderPhoneNumber { get; set; } = string.Empty;
}
