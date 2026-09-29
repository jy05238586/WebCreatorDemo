using System.Runtime.CompilerServices;
using Azure;
using Azure.Data.Tables;

namespace BlazorWebCreateAgent.Services;

public sealed class HtmlPageStorageService
{
    private const string CurrentStateRowKey = "current";
    private readonly TableClient _tableClient;
    private readonly TableClient _userSessionsTableClient;
    private readonly TableClient _userGeneratedHtmlTableClient;

    public HtmlPageStorageService(IConfiguration configuration)
    {
        var connectionString = configuration["AzureStorage:ConnectionString"];
        var tableName = configuration["AzureStorage:TableName"] ?? "SavedHtmlPages";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("AzureStorage:ConnectionString is not configured.");
        }

        _tableClient = new TableClient(connectionString, tableName);
        _userSessionsTableClient = new TableClient(connectionString, "usersessions");
        _userGeneratedHtmlTableClient = new TableClient(connectionString, "usergeneratedhtml");
    }

    public async Task SaveAsync(string userId, string fileName, string htmlContent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(htmlContent))
        {
            throw new ArgumentException("HTML content is required.", nameof(htmlContent));
        }

        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var entity = new SavedHtmlEntity
        {
            PartitionKey = userId,
            RowKey = $"{Guid.NewGuid():N}",
            UserId = userId,
            FileName = fileName,
            HtmlContent = htmlContent,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        await _tableClient.AddEntityAsync(entity, cancellationToken);
    }

    public async IAsyncEnumerable<SavedHtmlEntity> QueryAsync(string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            yield break;
        }

        await foreach (var entity in _tableClient.QueryAsync<SavedHtmlEntity>(e => e.PartitionKey == userId, cancellationToken: cancellationToken))
        {
            yield return entity;
        }
    }

    public async Task<SavedHtmlEntity?> GetAsync(string userId, string rowKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(rowKey))
        {
            return null;
        }

        var response = await _tableClient.GetEntityIfExistsAsync<SavedHtmlEntity>(userId, rowKey, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    public async Task SaveChatHistoryAsync(string userId, string chatHistory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        await _userSessionsTableClient.CreateIfNotExistsAsync(cancellationToken);

        var entity = new UserChatHistoryEntity
        {
            PartitionKey = userId,
            RowKey = CurrentStateRowKey,
            UserId = userId,
            ChatHistory = chatHistory,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _userSessionsTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        await RemoveSupersededRowsAsync<UserChatHistoryEntity>(_userSessionsTableClient, userId, cancellationToken);
    }

    public async Task<UserChatHistoryEntity?> GetChatHistoryAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        await _userSessionsTableClient.CreateIfNotExistsAsync(cancellationToken);
        var response = await _userSessionsTableClient.GetEntityIfExistsAsync<UserChatHistoryEntity>(
            userId,
            CurrentStateRowKey,
            cancellationToken: cancellationToken);

        return response.HasValue ? response.Value : null;
    }

    public async Task SaveGeneratedHtmlAsync(string userId, string? htmlContent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        await _userGeneratedHtmlTableClient.CreateIfNotExistsAsync(cancellationToken);

        var entity = new UserGeneratedHtmlEntity
        {
            PartitionKey = userId,
            RowKey = CurrentStateRowKey,
            UserId = userId,
            HtmlContent = htmlContent ?? string.Empty,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _userGeneratedHtmlTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        await RemoveSupersededRowsAsync<UserGeneratedHtmlEntity>(_userGeneratedHtmlTableClient, userId, cancellationToken);
    }

    public async Task<UserGeneratedHtmlEntity?> GetGeneratedHtmlAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        await _userGeneratedHtmlTableClient.CreateIfNotExistsAsync(cancellationToken);
        var response = await _userGeneratedHtmlTableClient.GetEntityIfExistsAsync<UserGeneratedHtmlEntity>(
            userId,
            CurrentStateRowKey,
            cancellationToken: cancellationToken);

        return response.HasValue ? response.Value : null;
    }

    private static async Task RemoveSupersededRowsAsync<TEntity>(TableClient tableClient, string userId, CancellationToken cancellationToken)
        where TEntity : class, ITableEntity, new()
    {
        await foreach (var entity in tableClient.QueryAsync<TEntity>(row => row.PartitionKey == userId, cancellationToken: cancellationToken))
        {
            if (!string.Equals(entity.RowKey, CurrentStateRowKey, StringComparison.Ordinal))
            {
                await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);
            }
        }
    }

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

    public sealed class UserChatHistoryEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string UserId { get; set; } = string.Empty;
        public string ChatHistory { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }

    public sealed class UserGeneratedHtmlEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string UserId { get; set; } = string.Empty;
        public string HtmlContent { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
