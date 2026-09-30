using System.Text.Json;

namespace BlazorWebCreateAgent.Services;

public sealed class UserStateService(
    HtmlPageStorageService htmlPageStorageService,
    ILogger<UserStateService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<UserStateSnapshot> LoadAsync(string userId, string sessionId)
    {
        UserChatHistoryEntity? chatHistory = null;
        var chatHistoryLoaded = false;
        try
        {
            chatHistory = await htmlPageStorageService.GetChatHistoryAsync(userId, sessionId);
            chatHistoryLoaded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load chat history for user {UserId} and session {SessionId}.", userId, sessionId);
        }

        UserGeneratedHtmlEntity? generatedHtml = null;
        var generatedHtmlLoaded = false;
        try
        {
            generatedHtml = await htmlPageStorageService.GetGeneratedHtmlAsync(userId, sessionId);
            generatedHtmlLoaded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load generated HTML for user {UserId} and session {SessionId}.", userId, sessionId);
        }

        IReadOnlyList<UserChatMessage>? messages = null;
        if (!string.IsNullOrWhiteSpace(chatHistory?.ChatHistory))
        {
            try
            {
                messages = JsonSerializer.Deserialize<List<UserChatMessage>>(chatHistory.ChatHistory, JsonOptions);
                if (messages?.Count == 0)
                {
                    messages = null;
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Stored chat history is not valid JSON for user {UserId} and session {SessionId}.", userId, sessionId);
            }
        }

        return new UserStateSnapshot(
            messages,
            chatHistoryLoaded && chatHistory is null,
            generatedHtml?.HtmlContent,
            (chatHistory is not null && chatHistory.UserId == userId && chatHistory.SessionId == sessionId)
                || (generatedHtml is not null && generatedHtml.UserId == userId && generatedHtml.SessionId == sessionId),
            chatHistoryLoaded && generatedHtmlLoaded);
    }

    public async Task<IReadOnlyList<ChatHistorySession>> LoadAllChatHistoryAsync(string userId)
    {
        var sessions = new List<ChatHistorySession>();
        await foreach (var entity in htmlPageStorageService.QueryChatHistoryAsync(userId))
        {
            try
            {
                var messages = JsonSerializer.Deserialize<List<UserChatMessage>>(entity.ChatHistory, JsonOptions);
                if (messages is { Count: > 0 })
                {
                    sessions.Add(new ChatHistorySession(entity.SessionId, entity.UpdatedUtc, messages));
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Stored chat history is not valid JSON for user {UserId} and session {SessionId}.", userId, entity.SessionId);
            }
        }

        return sessions.OrderByDescending(session => session.UpdatedUtc).ToList();
    }

    public async Task<string?> LoadSavedPageAsync(string userId, string pageId)
    {
        try
        {
            var savedPage = await htmlPageStorageService.GetAsync(userId, pageId);
            return savedPage?.HtmlContent;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to load saved page {SavedPageId} for user {UserId}.", pageId, userId);
            return null;
        }
    }

    public async Task SaveChatHistoryAsync(string userId, string sessionId, IEnumerable<UserChatMessage> messages)
    {
        var chatHistory = JsonSerializer.Serialize(messages.Select(message => new
        {
            role = message.Role,
            content = message.Content
        }));

        try
        {
            await htmlPageStorageService.SaveChatHistoryAsync(userId, sessionId, chatHistory);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save chat history for user {UserId} and session {SessionId}.", userId, sessionId);
        }
    }

    public async Task SaveGeneratedHtmlAsync(string userId, string sessionId, string? htmlContent)
    {
        try
        {
            await htmlPageStorageService.SaveGeneratedHtmlAsync(userId, sessionId, htmlContent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to save generated HTML for user {UserId} and session {SessionId}.", userId, sessionId);
        }
    }

    public Task SavePageAsync(string userId, string fileName, string htmlContent)
    {
        return htmlPageStorageService.SaveAsync(userId, fileName, htmlContent);
    }
}

public sealed record UserStateSnapshot(
    IReadOnlyList<UserChatMessage>? ChatMessages,
    bool ShouldSaveInitialChatHistory,
    string? GeneratedHtml,
    bool SessionExists,
    bool SessionLookupSucceeded);

public sealed record ChatHistorySession(
    string SessionId,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<UserChatMessage> Messages);

public sealed record UserChatMessage(string Role, string Content);