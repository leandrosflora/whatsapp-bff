namespace whatsapp_bff.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; init; } = "localhost:6379";
    public int PendingTtlSeconds { get; init; } = 86400;
    public int CompletedTtlSeconds { get; init; } = 604800;
}
