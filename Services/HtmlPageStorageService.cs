using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;

namespace BlazorWebCreateAgent.Services;

public sealed class HtmlPageStorageService
{
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

    public async Task SaveChatHistoryAsync(string userId, string sessionId, string chatHistory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        await _userSessionsTableClient.CreateIfNotExistsAsync(cancellationToken);

        var entity = new UserChatHistoryEntity
        {
            PartitionKey = userId,
            RowKey = GetSessionRowKey(sessionId),
            UserId = userId,
            SessionId = sessionId,
            ChatHistory = chatHistory,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _userSessionsTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<UserChatHistoryEntity?> GetChatHistoryAsync(string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        await _userSessionsTableClient.CreateIfNotExistsAsync(cancellationToken);
        var response = await _userSessionsTableClient.GetEntityIfExistsAsync<UserChatHistoryEntity>(
            userId,
            GetSessionRowKey(sessionId),
            cancellationToken: cancellationToken);

        return response.HasValue ? response.Value : null;
    }

    public async IAsyncEnumerable<UserChatHistoryEntity> QueryChatHistoryAsync(
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            yield break;
        }

        await _userSessionsTableClient.CreateIfNotExistsAsync(cancellationToken);
        await foreach (var entity in _userSessionsTableClient.QueryAsync<UserChatHistoryEntity>(
            row => row.PartitionKey == userId,
            cancellationToken: cancellationToken))
        {
            yield return entity;
        }
    }

    public async Task SaveGeneratedHtmlAsync(string userId, string sessionId, string? htmlContent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        await _userGeneratedHtmlTableClient.CreateIfNotExistsAsync(cancellationToken);

        var entity = new UserGeneratedHtmlEntity
        {
            PartitionKey = userId,
            RowKey = GetSessionRowKey(sessionId),
            UserId = userId,
            SessionId = sessionId,
            HtmlContent = htmlContent ?? string.Empty,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _userGeneratedHtmlTableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<UserGeneratedHtmlEntity?> GetGeneratedHtmlAsync(string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        await _userGeneratedHtmlTableClient.CreateIfNotExistsAsync(cancellationToken);
        var response = await _userGeneratedHtmlTableClient.GetEntityIfExistsAsync<UserGeneratedHtmlEntity>(
            userId,
            GetSessionRowKey(sessionId),
            cancellationToken: cancellationToken);

        return response.HasValue ? response.Value : null;
    }

    private static string GetSessionRowKey(string sessionId)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));
    }

}
