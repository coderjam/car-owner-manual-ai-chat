using System.Net.Http.Json;
using CarManualAssistant.Api.Models;

namespace CarManualAssistant.Api.Services;

public sealed class AiAssistantClient
{
    private readonly HttpClient _httpClient;
    private readonly IAppStore _store;
    private readonly ILogger<AiAssistantClient> _logger;

    public AiAssistantClient(HttpClient httpClient, IAppStore store, ILogger<AiAssistantClient> logger)
    {
        _httpClient = httpClient;
        _store = store;
        _logger = logger;
    }

    public async Task<ChatResponse> AskAsync(
        ChatRequest request,
        Vehicle vehicle,
        IReadOnlyList<ChatHistory> recentHistory,
        CancellationToken cancellationToken)
    {
        var aiRequest = new AiChatRequest(
            UserId: request.UserId,
            VehicleId: request.VehicleId,
            Question: request.Question,
            Vehicle: vehicle,
            RecentHistory: recentHistory);

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(90));

            var response = await _httpClient.PostAsJsonAsync(
                "/rag/chat",
                aiRequest,
                timeoutSource.Token);

            if (response.IsSuccessStatusCode)
            {
                var aiResponse = await response.Content.ReadFromJsonAsync<ChatResponse>(
                    cancellationToken: timeoutSource.Token);

                if (aiResponse is not null)
                {
                    return aiResponse;
                }
            }

            _logger.LogWarning(
                "AI service returned non-success status code {StatusCode}",
                response.StatusCode);
        }
        catch (Exception exception)
        {
            // V1 开发阶段允许 AI 服务暂时不可用。
            // 这里返回一个可追溯的兜底答案，让前端和后端流程仍然能完整跑通。
            _logger.LogWarning(exception, "AI service is unavailable, fallback answer will be used");
        }

        var references = await _store.SearchFallbackReferencesAsync(
            request.VehicleId,
            request.Question,
            cancellationToken);

        var answer = references.Count == 0
            ? "当前没有找到该车型手册中的明确依据。请先上传并解析对应用户手册，再重新提问。"
            : "当前 AI 服务未连接，系统先根据本地手册索引返回参考内容。请查看下方手册页码和整页图片，确认具体说明。";

        return new ChatResponse(
            Answer: answer,
            References: references,
            ChatHistoryId: 0,
            CreateTime: DateTimeOffset.UtcNow);
    }

    public async Task<ProcessManualResult> ProcessManualAsync(
        long manualId,
        long vehicleId,
        string documentName,
        string filePath,
        string pageImageDirectory,
        CancellationToken cancellationToken)
    {
        var request = new ProcessManualRequest(
            ManualId: manualId,
            VehicleId: vehicleId,
            DocumentName: documentName,
            FilePath: filePath,
            PageImageDirectory: pageImageDirectory);

        var response = await _httpClient.PostAsJsonAsync(
            "/manuals/process",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProcessManualResult>(
            cancellationToken: cancellationToken);

        return result ?? new ProcessManualResult(0, 0, 0, []);
    }
}
