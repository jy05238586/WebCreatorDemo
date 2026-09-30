using Azure;
using Azure.Data.Tables;

namespace BlazorWebCreateAgent.Services;

public sealed class SavedHtmlEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string HtmlContent { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}