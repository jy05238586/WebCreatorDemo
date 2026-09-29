using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BlazorWebCreateAgent.Services;

public sealed class WebCreateChatHandler
{
    private readonly IChatClient? _chatClient;
    private readonly ILogger<WebCreateChatHandler> _logger;
    private readonly bool _isConfigured;
    private readonly InMemoryChatHistoryProvider? _chatHistoryProvider;

    private AgentSession? _agentSession;
    private AIAgent? _agent;

    public WebCreateChatHandler(IConfiguration configuration, ILogger<WebCreateChatHandler> logger)
    {
        var endpoint = configuration["AzureFoundry:Endpoint"];
        var apiKey = configuration["AzureFoundry:ApiKey"];
        var modelName = configuration["AzureFoundry:Model"] ?? "gpt-5-mini";

        _logger = logger;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) ||
            endpoint.Contains("<your-") || apiKey.Contains("<your-"))
        {
            _isConfigured = false;
            _chatClient = null;
            return;
        }

        var azureOpenAiClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _chatClient = azureOpenAiClient.GetChatClient(modelName).AsIChatClient();
        _agent = WebPageGeneratorAgent.Create(_chatClient, out var chatHistoryProvider);
        _chatHistoryProvider = chatHistoryProvider;
        _isConfigured = true;
    }

    public string? LastHtmlContent { get; private set; }
    public string? LastInstruction { get; private set; }

    public async Task<string> SendAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (_agentSession == null)
        {
            if (_agent is null)
            {
                return "Azure Foundry is not configured yet. Add your Endpoint and ApiKey to appsettings.json, then try again.";
            }

            _agentSession = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return "Please enter a message before sending.";
        }

        if (!_isConfigured || _chatClient is null)
        {
            return "Azure Foundry is not configured yet. Add your Endpoint and ApiKey to appsettings.json, then try again.";
        }

        try
        {
            var agent = _agent ?? throw new InvalidOperationException("The AI agent is not configured.");
            var response = await agent.RunAsync(userMessage, _agentSession, new AgentRunOptions(), cancellationToken);
            var finalText = response.Text ?? string.Empty;

            return ParseWorkflowResult(finalText);
        }
        catch (OperationCanceledException)
        {
            return "Request cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The agent workflow failed while generating the page.");
            return "The Azure Foundry workflow could not be reached. Please retry.";
        }
    }

    public async Task NewSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_agent is null)
        {
            throw new InvalidOperationException("Azure Foundry is not configured yet. Add your Endpoint and ApiKey to appsettings.json, then try again.");
        }

        _agentSession = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreHistoryAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        if (_agent is null || _chatHistoryProvider is null)
        {
            return;
        }

        _agentSession ??= await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        _chatHistoryProvider.SetMessages(_agentSession, messages.ToList());
    }

    private string ParseWorkflowResult(string finalText)
    {
        LastHtmlContent = null;
        LastInstruction = null;

        var trimmed = finalText.Trim();

        if (trimmed.StartsWith('{') && trimmed.Contains("htmlcontent", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                var root = document.RootElement;

                if (root.TryGetProperty("instruction", out var instructionElement) && instructionElement.ValueKind == JsonValueKind.String)
                {
                    LastInstruction = instructionElement.GetString();
                }

                if (root.TryGetProperty("htmlcontent", out var htmlElement) && htmlElement.ValueKind == JsonValueKind.String)
                {
                    LastHtmlContent = htmlElement.GetString();
                }

                if (!string.IsNullOrWhiteSpace(LastInstruction))
                {
                    return LastInstruction;
                }

                if (!string.IsNullOrWhiteSpace(LastHtmlContent))
                {
                    return "The request was processed and the preview has been updated.";
                }
            }
            catch (JsonException)
            {
                return finalText;
            }
        }

        LastInstruction = trimmed;
        return trimmed;
    }
}
