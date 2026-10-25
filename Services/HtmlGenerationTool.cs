using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.AI;

namespace BlazorWebCreateAgent.Services;

internal static class HtmlGenerationTool
{
    public static AITool Create(
        IChatClient chatClient,
        Func<string, Task>? onToolActivity = null,
        Func<string, Task>? onToolResult = null)
    {
        Func<string, CancellationToken, Task<string>> generateBestHtml =
            (request, cancellationToken) => GenerateBestHtmlAsync(
                chatClient,
                request,
                cancellationToken,
                onToolActivity,
                onToolResult);

        return AIFunctionFactory.Create(
            generateBestHtml,
            "generate_best_html",
            "Generate and score two HTML page candidates, then return the selected candidate with a brief instruction summary as JSON.");
    }

    private static async Task<string> GenerateBestHtmlAsync(
        IChatClient chatClient,
        string request,
        CancellationToken cancellationToken,
        Func<string, Task>? onToolActivity,
        Func<string, Task>? onToolResult)
    {
        if (onToolActivity is not null)
        {
            await onToolActivity("正在生成网页...");
        }

        var candidates = await Task.WhenAll(
            GenerateCandidateAsync(chatClient, request, "Use a clean editorial layout with strong typography and clear visual hierarchy.", onToolActivity, cancellationToken),
            GenerateCandidateAsync(chatClient, request, "Use a distinct visual direction with a compact, highly usable responsive layout.", onToolActivity, cancellationToken));

        var firstScore = candidates[0].Score;
        var secondScore = candidates[1].Score;
        var selectedIndex = secondScore > firstScore ? 1 : 0;
        var selectedCandidate = candidates[selectedIndex];

        if (onToolActivity is not null)
        {
            await onToolActivity("候选网页已生成，正在比较结果...");
        }

        var result = JsonSerializer.Serialize(
            new
            {
                instruction = selectedCandidate.Instruction,
                htmlcontent = selectedCandidate.Html
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });

        if (onToolResult is not null)
        {
            await onToolResult(result);
        }

        return result;
    }

    private static async Task<HtmlCandidate> GenerateCandidateAsync(
        IChatClient chatClient,
        string request,
        string direction,
        Func<string, Task>? onToolActivity,
        CancellationToken cancellationToken)
    {
        var prompt = $"Create a complete, self-contained, responsive HTML5 page for this request:\n{request}\n\n" +
            $"Design direction: {direction}\n\n" +
            "Use Simplified Chinese for visible page text unless the user explicitly requests another language. Use semantic HTML, an appropriate page title, a viewport meta tag, accessible controls and images, and responsive CSS. Keep it under 1000 lines. Make sure it works in an iframe with sandbox=\"allow-scripts allow-modals\" and Use only inline scripts and styles; do not load external scripts, stylesheets, fonts, images, or other resources. Images and media must use data: or blob: URLs. Do not use network requests, fetch/XHR, WebSockets, external navigation, or form submissions. Do not use unsafe-eval" +
            "Estimate the quality of the page you create from 0 to 100 using these equally weighted criteria: fulfillment of the request, visual hierarchy and usability, responsive behavior, accessibility, and semantic/code quality. Be candid; do not default to a high score. " +
            "Write a brief Simplified Chinese \"instruction\" describing the design choices, implemented features, and how to use the page. " +
            "Return only one valid JSON object with an integer \"score\" from 0 to 100, the summary in \"instruction\", and the complete HTML document in \"htmlcontent\". Do not use markdown fences or commentary.";

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        var candidate = ParseCandidate(response.Text ?? string.Empty);

        if (onToolActivity is not null)
        {
            await onToolActivity(
                $"候选方案已生成（评分：{candidate.Score}/100）。");
        }

        return candidate;
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
                TryGetPropertyIgnoreCase(root, "instruction", out var instructionElement) &&
                instructionElement.ValueKind == JsonValueKind.String &&
                TryGetPropertyIgnoreCase(root, "htmlcontent", out var htmlElement) &&
                htmlElement.ValueKind == JsonValueKind.String)
            {
                var instruction = instructionElement.GetString();
                var html = htmlElement.GetString();
                if (!string.IsNullOrWhiteSpace(instruction) && !string.IsNullOrWhiteSpace(html))
                {
                    return new HtmlCandidate(instruction, html, Math.Clamp(score, 0, 100));
                }
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidDataException("The HTML generation response did not contain a valid candidate.");
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

    private sealed record HtmlCandidate(string Instruction, string Html, int Score);
}