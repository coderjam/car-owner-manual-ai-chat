namespace CarManualAssistant.Api.Models;

public static class ManualStatus
{
    public const string Uploaded = "uploaded";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed class User
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public DateTimeOffset CreateTime { get; init; }
}

public sealed class Vehicle
{
    public long Id { get; init; }
    public string Brand { get; init; } = "";
    public string Model { get; init; } = "";
    public int Year { get; init; }
    public string Engine { get; init; } = "";
    public string Configuration { get; init; } = "";
    public DateTimeOffset CreateTime { get; init; }
}

public sealed class UserVehicle
{
    public long Id { get; init; }
    public long UserId { get; init; }
    public long VehicleId { get; init; }
    public DateOnly? BuyDate { get; init; }
    public int? Mileage { get; init; }
    public DateTimeOffset CreateTime { get; init; }
}

public sealed class Manual
{
    public long Id { get; init; }
    public long VehicleId { get; init; }
    public string FileName { get; init; } = "";
    public string? PdfUrl { get; set; }
    public string? PhysicalPath { get; set; }
    public string? SourceType { get; init; }
    public string? SourceUrl { get; init; }
    public string Status { get; set; } = ManualStatus.Uploaded;
    public int TotalPages { get; set; }
    public int GeneratedPageImages { get; set; }
    public int KnowledgeChunks { get; set; }
    public DateTimeOffset CreateTime { get; init; }
}

public sealed class ManualPage
{
    public long Id { get; init; }
    public long ManualId { get; init; }
    public int PdfPageNumber { get; init; }
    public int? PrintedPageNumber { get; init; }
    public string Chapter { get; init; } = "";
    public string PageText { get; init; } = "";
    public string PageImageUrl { get; init; } = "";
}

public sealed class KnowledgeReference
{
    public long DocumentId { get; init; }
    public string DocumentName { get; init; } = "";
    public string Chapter { get; init; } = "";
    public int PdfPageNumber { get; init; }
    public int? PrintedPageNumber { get; init; }
    public string Quote { get; init; } = "";
    public string PageImageUrl { get; init; } = "";
    public string PdfPageUrl { get; init; } = "";
    public int TotalPages { get; init; }
}

public sealed class ChatHistory
{
    public long Id { get; init; }
    public long UserId { get; init; }
    public long VehicleId { get; init; }
    public string ConversationId { get; init; } = "";
    public string Question { get; init; } = "";
    public string Answer { get; init; } = "";
    public IReadOnlyList<KnowledgeReference> References { get; init; } = [];
    public DateTimeOffset CreateTime { get; init; }
}
