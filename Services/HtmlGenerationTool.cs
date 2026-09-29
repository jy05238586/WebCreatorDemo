using System.Text.Json;
using Microsoft.Extensions.AI;

namespace BlazorWebCreateAgent.Services;

internal static class HtmlGenerationTool
{
    public static AITool Create(IChatClient chatClient)
    {
        Func<string, CancellationToken, Task<string>> generateBestHtml =
            (request, cancellationToken) => GenerateBestHtmlAsync(chatClient, request, cancellationToken);

        return AIFunctionFactory.Create(
            generateBestHtml,
            "generate_best_html",
            "Generate two independent HTML page candidates for the complete user request, estimate each candidate's quality, and return the higher-scoring HTML.");
    }

    private static async Task<string> GenerateBestHtmlAsync(
        IChatClient chatClient,
        string request,
        CancellationToken cancellationToken)
    {
        var candidates = await Task.WhenAll(
            GenerateCandidateAsync(chatClient, request, "Use a clean editorial layout with strong typography and clear visual hierarchy.", cancellationToken),
            GenerateCandidateAsync(chatClient, request, "Use a distinct visual direction with a compact, highly usable responsive layout.", cancellationToken));

        var firstScore = candidates[0].Score;
        var secondScore = candidates[1].Score;
        var selectedIndex = secondScore > firstScore ? 1 : 0;
        var selectedHtml = candidates[selectedIndex].Html;

        return $"Candidate 1 estimated score: {firstScore}/100. Candidate 2 estimated score: {secondScore}/100. " +
            $"Selected candidate {selectedIndex + 1}. Return its HTML as the htmlcontent value.\n\n{selectedHtml}";
    }

    private static async Task<HtmlCandidate> GenerateCandidateAsync(
        IChatClient chatClient,
        string request,
        string direction,
        CancellationToken cancellationToken)
    {
        var prompt = $"Create a complete, self-contained, responsive HTML5 page for this request:\n{request}\n\n" +
            $"Design direction: {direction}\n\n" +
            "Use semantic HTML, an appropriate page title, a viewport meta tag, accessible controls and images, and responsive CSS. Keep it under 1000 lines. " +
            "Estimate the quality of the page you create from 0 to 100 using these equally weighted criteria: fulfillment of the request, visual hierarchy and usability, responsive behavior, accessibility, and semantic/code quality. Be candid; do not default to a high score. " +
            "Return only one valid JSON object with an integer \"score\" from 0 to 100 and the complete HTML document in \"htmlcontent\". Do not use markdown fences or commentary.";

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return ParseCandidate(response.Text ?? string.Empty);
    }

    private static HtmlCandidate ParseCandidate(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(root, "score", out var scoreElement) &&
                scoreElement.TryGetInt32(out var score) &&
                TryGetPropertyIgnoreCase(root, "htmlcontent", out var htmlElement) &&
                htmlElement.ValueKind == JsonValueKind.String)
            {
                return new HtmlCandidate(htmlElement.GetString() ?? string.Empty, Math.Clamp(score, 0, 100));
            }
        }
        catch (JsonException)
        {
        }

        return new HtmlCandidate(string.Empty, 0);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record HtmlCandidate(string Html, int Score);
}