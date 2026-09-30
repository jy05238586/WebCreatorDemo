using System.Text;
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
    private Func<string, Task>? _onToolActivityUpdate;

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
        _agent = WebPageGeneratorAgent.Create(_chatClient, out var chatHistoryProvider, ReportToolActivityAsync);
        _chatHistoryProvider = chatHistoryProvider;
        _isConfigured = true;
    }

    public string? LastHtmlContent { get; private set; }
    public string? LastInstruction { get; private set; }

    public async Task<string> SendAsync(
        string userMessage,
        CancellationToken cancellationToken = default,
        Func<string, Task>? onTextUpdate = null)
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
            var responseText = new StringBuilder();
            var toolActivities = new List<string>();
            var lastStreamedText = string.Empty;
            _onToolActivityUpdate = async activity =>
            {
                toolActivities.Add(activity);
                if (onTextUpdate is not null)
                {
                    await onTextUpdate(ComposeAssistantOutput(toolActivities, lastStreamedText));
                }
            };

            await foreach (var update in agent.RunStreamingAsync(
                userMessage,
                _agentSession,
                new AgentRunOptions(),
                cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                responseText.Append(update.Text);
                var streamedText = GetStreamedText(responseText.ToString());
                if (onTextUpdate is not null && !string.IsNullOrEmpty(streamedText) &&
                    !string.Equals(streamedText, lastStreamedText, StringComparison.Ordinal))
                {
                    lastStreamedText = streamedText;
                    await onTextUpdate(ComposeAssistantOutput(toolActivities, lastStreamedText));
                }
            }

            var finalText = responseText.ToString();
            var reply = ParseWorkflowResult(finalText);
            var finalReply = ComposeAssistantOutput(toolActivities, reply);
            if (onTextUpdate is not null)
            {
                await onTextUpdate(finalReply);
            }

            return finalReply;
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
        finally
        {
            _onToolActivityUpdate = null;
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

    private static string GetStreamedText(string responseText)
    {
        var trimmed = responseText.AsSpan().TrimStart();
        if (trimmed.IsEmpty || trimmed[0] != '{')
        {
            return responseText;
        }

        return ExtractJsonStringProperty(responseText, "instruction") ?? string.Empty;
    }

    private Task ReportToolActivityAsync(string activity)
    {
        return _onToolActivityUpdate?.Invoke(activity) ?? Task.CompletedTask;
    }

    private static string ComposeAssistantOutput(IReadOnlyList<string> toolActivities, string response)
    {
        var output = new StringBuilder();
        if (toolActivities.Count > 0)
        {
            output.AppendJoin(Environment.NewLine + Environment.NewLine, toolActivities);
        }

        if (!string.IsNullOrWhiteSpace(response))
        {
            if (output.Length > 0)
            {
                output.AppendLine().AppendLine();
            }

            output.Append(response);
        }

        return output.ToString();
    }

    private static string? ExtractJsonStringProperty(string json, string propertyName)
    {
        var propertyIndex = json.IndexOf(JsonSerializer.Serialize(propertyName), StringComparison.Ordinal);
        if (propertyIndex < 0)
        {
            return null;
        }

        var index = propertyIndex + propertyName.Length + 2;
        SkipWhitespace(json, ref index);
        if (index >= json.Length || json[index++] != ':')
        {
            return null;
        }

        SkipWhitespace(json, ref index);
        if (index >= json.Length || json[index++] != '"')
        {
            return null;
        }

        var value = new StringBuilder();
        while (index < json.Length)
        {
            var character = json[index++];
            if (character == '"')
            {
                break;
            }

            if (character != '\\')
            {
                value.Append(character);
                continue;
            }

            if (index >= json.Length)
            {
                break;
            }

            character = json[index++];
            switch (character)
            {
                case '"': value.Append('"'); break;
                case '\\': value.Append('\\'); break;
                case '/': value.Append('/'); break;
                case 'b': value.Append('\b'); break;
                case 'f': value.Append('\f'); break;
                case 'n': value.Append('\n'); break;
                case 'r': value.Append('\r'); break;
                case 't': value.Append('\t'); break;
                case 'u':
                    if (json.Length - index < 4 || !int.TryParse(
                        json.AsSpan(index, 4),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var codePoint))
                    {
                        return value.ToString();
                    }

                    value.Append((char)codePoint);
                    index += 4;
                    break;
                default:
                    return value.ToString();
            }
        }

        return value.ToString();
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }
    }
}
