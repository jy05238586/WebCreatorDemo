using Azure;
using Azure.Data.Tables;

namespace BlazorWebCreateAgent.Services;

public sealed class UserChatHistoryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ChatHistory { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}