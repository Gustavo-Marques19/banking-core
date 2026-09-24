namespace Banking.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>amqp://usuario:senha@host:5672/. Sem valor, publisher e consumidores não sobem.</summary>
    public string? Uri { get; set; }

    public string Exchange { get; set; } = "banking.events";

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Uri);
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 10;

    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan PublishedRetention { get; set; } = TimeSpan.FromDays(7);
}

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumers";

    public bool Enabled { get; set; } = true;

    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
}
