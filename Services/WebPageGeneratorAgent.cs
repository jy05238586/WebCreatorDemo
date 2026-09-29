using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BlazorWebCreateAgent.Services;

internal static class WebPageGeneratorAgent
{
    public static AIAgent Create(IChatClient chatClient)
        => Create(chatClient, out _);

    public static AIAgent Create(IChatClient chatClient, out InMemoryChatHistoryProvider chatHistoryProvider)
    {
        chatHistoryProvider = new InMemoryChatHistoryProvider();
        var options = new ChatClientAgentOptions
        {
            Id = "webpage-generator",
            Name = "Web page generator",
            ChatOptions = new ChatOptions
            {
                Instructions = LoadSystemPrompt(),
                Tools = [HtmlGenerationTool.Create(chatClient)]
            },
            ChatHistoryProvider = chatHistoryProvider
        };

        return new ChatClientAgent(chatClient, options);
    }

    private static string LoadSystemPrompt()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Resources", "createhtmlpromt.txt");

        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath).Trim();
        }

        return "You are a helpful assistant for this Blazor application.";
    }
}
