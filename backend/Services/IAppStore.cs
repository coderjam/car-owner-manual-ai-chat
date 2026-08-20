using CarManualAssistant.Api.Models;

namespace CarManualAssistant.Api.Services;

public interface IAppStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<User> FindOrCreateUserAsync(string username, CancellationToken cancellationToken);

    Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken);

    Task<Vehicle?> GetVehicleAsync(long vehicleId, CancellationToken cancellationToken);

    Task<UserVehicle?> SetUserVehicleAsync(CreateUserVehicleRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<Manual>> GetManualsAsync(CancellationToken cancellationToken);

    Task<Manual?> GetManualAsync(long manualId, CancellationToken cancellationToken);

    Task<Manual> CreateManualAsync(CreateManualRequest request, CancellationToken cancellationToken);

    Task MarkManualUploadedAsync(
        long manualId,
        string relativePdfUrl,
        string physicalPath,
        CancellationToken cancellationToken);

    Task UpdateManualStatusAsync(long manualId, string status, CancellationToken cancellationToken);

    Task UpdateManualStatsAsync(
        long manualId,
        string status,
        int totalPages,
        int generatedPageImages,
        int knowledgeChunks,
        CancellationToken cancellationToken);

    Task ReplaceManualPagesAsync(
        long manualId,
        IReadOnlyList<ManualPage> pages,
        CancellationToken cancellationToken);

    Task<bool> DeleteManualAsync(long manualId, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeReference>> SearchFallbackReferencesAsync(
        long vehicleId,
        string question,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatHistory>> GetChatHistoryAsync(
        long userId,
        long? vehicleId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatHistory>> GetRecentChatHistoryAsync(
        long userId,
        long vehicleId,
        string conversationId,
        int take,
        CancellationToken cancellationToken);

    Task<ChatHistory> AddChatHistoryAsync(
        ChatRequest request,
        ChatResponse response,
        CancellationToken cancellationToken);
}
