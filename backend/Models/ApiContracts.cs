namespace CarManualAssistant.Api.Models;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(long UserId, string Username, string Token);

public sealed record UserManualResponse(
    long Id,
    string FileName,
    string PdfUrl,
    int TotalPages);

public sealed record AdminLoginRequest(string Username, string Password);

public sealed record AdminLoginResponse(string Username, string Token);

public sealed record CreateUserVehicleRequest(
    long UserId,
    long VehicleId,
    DateOnly? BuyDate,
    int? Mileage);

public sealed record CreateManualRequest(
    long VehicleId,
    string FileName,
    string? SourceType,
    string? SourceUrl);

public sealed record ChatRequest(
    long UserId,
    long VehicleId,
    string Question,
    string? ConversationId);

public sealed record ChatResponse(
    string Answer,
    IReadOnlyList<KnowledgeReference> References,
    long ChatHistoryId,
    DateTimeOffset CreateTime);

public sealed record AiChatRequest(
    long UserId,
    long VehicleId,
    string Question,
    Vehicle Vehicle,
    IReadOnlyList<ChatHistory> RecentHistory);

public sealed record ProcessManualRequest(
    long ManualId,
    long VehicleId,
    string DocumentName,
    string FilePath,
    string PageImageDirectory);

public sealed record ProcessManualResult(
    int TotalPages,
    int GeneratedPageImages,
    int KnowledgeChunks,
    IReadOnlyList<ManualPage> Pages);

public sealed record SavedManualFile(
    string PhysicalPath,
    string RelativePdfUrl,
    string PageImageDirectory);
