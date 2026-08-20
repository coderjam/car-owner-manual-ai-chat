using System.Text.Json;
using CarManualAssistant.Api.Models;

namespace CarManualAssistant.Api.Services;

public sealed class AppStore : IAppStore
{
    private const long InitialManualId = 1;
    private const long InitialVehicleId = 1;
    private const string InitialManualFileName = "2026款凯美瑞智能电混双擎用户手册-2507版（01999-06154）.pdf";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly ManualFileService _manualFiles;
    private readonly ILogger<AppStore> _logger;
    private long _nextUserId = 2;
    private long _nextUserVehicleId = 1;
    private long _nextManualId = 1;
    private long _nextManualPageId = 1;
    private long _nextChatHistoryId = 1;

    private readonly List<User> _users =
    [
        new User
        {
            Id = 1,
            Username = "demo",
            CreateTime = DateTimeOffset.UtcNow
        }
    ];

    private readonly List<Vehicle> _vehicles =
    [
        new Vehicle
        {
            Id = 1,
            Brand = "丰田",
            Model = "凯美瑞",
            Year = 2026,
            Engine = "智能电混双擎",
            Configuration = "通用版",
            CreateTime = DateTimeOffset.UtcNow
        }
    ];

    private readonly List<UserVehicle> _userVehicles = [];

    private readonly List<Manual> _manuals = [];

    private readonly List<ManualPage> _manualPages = [];

    private readonly List<ChatHistory> _chatHistories = [];

    public AppStore(ManualFileService manualFiles, ILogger<AppStore> logger)
    {
        _manualFiles = manualFiles;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var pdfPath = _manualFiles.GetOriginalPdfPath(InitialManualId);
        if (!File.Exists(pdfPath))
        {
            return;
        }

        ProcessManualResult? manifest = null;
        var manifestPath = _manualFiles.GetManifestPath(InitialManualId);

        if (File.Exists(manifestPath))
        {
            try
            {
                await using var stream = File.OpenRead(manifestPath);
                manifest = await JsonSerializer.DeserializeAsync<ProcessManualResult>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                _logger.LogWarning(exception, "无法读取初始手册解析清单 {ManifestPath}", manifestPath);
            }
        }

        lock (_gate)
        {
            var manual = new Manual
            {
                Id = InitialManualId,
                VehicleId = InitialVehicleId,
                FileName = InitialManualFileName,
                PdfUrl = $"/manuals/{InitialManualId}/original.pdf",
                PhysicalPath = pdfPath,
                SourceType = "official",
                Status = manifest is null ? ManualStatus.Uploaded : ManualStatus.Completed,
                TotalPages = manifest?.TotalPages ?? 0,
                GeneratedPageImages = manifest?.GeneratedPageImages ?? 0,
                KnowledgeChunks = manifest?.KnowledgeChunks ?? 0,
                CreateTime = DateTimeOffset.UtcNow
            };

            _manuals.Add(manual);
            _nextManualId = InitialManualId + 1;

            if (manifest is null)
            {
                return;
            }

            foreach (var page in manifest.Pages)
            {
                _manualPages.Add(new ManualPage
                {
                    Id = _nextManualPageId++,
                    ManualId = InitialManualId,
                    PdfPageNumber = page.PdfPageNumber,
                    PrintedPageNumber = page.PrintedPageNumber,
                    Chapter = page.Chapter,
                    PageText = page.PageText,
                    PageImageUrl = page.PageImageUrl
                });
            }
        }
    }

    public Task<User> FindOrCreateUserAsync(string username, CancellationToken cancellationToken)
    {
        username = string.IsNullOrWhiteSpace(username) ? "demo" : username.Trim();

        lock (_gate)
        {
            var existing = _users.FirstOrDefault(user =>
                string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var user = new User
            {
                Id = _nextUserId++,
                Username = username,
                CreateTime = DateTimeOffset.UtcNow
            };

            _users.Add(user);
            return Task.FromResult(user);
        }
    }

    public Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Vehicle>>(_vehicles.ToList());
        }
    }

    public Task<Vehicle?> GetVehicleAsync(long vehicleId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_vehicles.FirstOrDefault(vehicle => vehicle.Id == vehicleId));
        }
    }

    public Task<UserVehicle?> SetUserVehicleAsync(CreateUserVehicleRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_vehicles.All(vehicle => vehicle.Id != request.VehicleId))
            {
                return Task.FromResult<UserVehicle?>(null);
            }

            var selectedVehicle = new UserVehicle
            {
                Id = _nextUserVehicleId++,
                UserId = request.UserId,
                VehicleId = request.VehicleId,
                BuyDate = request.BuyDate,
                Mileage = request.Mileage,
                CreateTime = DateTimeOffset.UtcNow
            };

            _userVehicles.Add(selectedVehicle);
            return Task.FromResult<UserVehicle?>(selectedVehicle);
        }
    }

    public Task<IReadOnlyList<Manual>> GetManualsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Manual>>(_manuals
                .OrderByDescending(manual => manual.CreateTime)
                .ToList());
        }
    }

    public Task<Manual?> GetManualAsync(long manualId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_manuals.FirstOrDefault(manual => manual.Id == manualId));
        }
    }

    public Task<Manual> CreateManualAsync(CreateManualRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var manual = new Manual
            {
                Id = _nextManualId++,
                VehicleId = request.VehicleId,
                FileName = request.FileName,
                SourceType = request.SourceType,
                SourceUrl = request.SourceUrl,
                Status = ManualStatus.Uploaded,
                CreateTime = DateTimeOffset.UtcNow
            };

            _manuals.Add(manual);
            return Task.FromResult(manual);
        }
    }

    public Task MarkManualUploadedAsync(
        long manualId,
        string relativePdfUrl,
        string physicalPath,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var manual = _manuals.First(manual => manual.Id == manualId);
            manual.PdfUrl = relativePdfUrl;
            manual.PhysicalPath = physicalPath;
            manual.Status = ManualStatus.Uploaded;
        }

        return Task.CompletedTask;
    }

    public Task UpdateManualStatusAsync(long manualId, string status, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var manual = _manuals.FirstOrDefault(manual => manual.Id == manualId);
            if (manual is not null)
            {
                manual.Status = status;
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateManualStatsAsync(
        long manualId,
        string status,
        int totalPages,
        int generatedPageImages,
        int knowledgeChunks,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var manual = _manuals.FirstOrDefault(manual => manual.Id == manualId);
            if (manual is null)
            {
                return Task.CompletedTask;
            }

            manual.Status = status;
            manual.TotalPages = totalPages;
            manual.GeneratedPageImages = generatedPageImages;
            manual.KnowledgeChunks = knowledgeChunks;
        }

        return Task.CompletedTask;
    }

    public Task ReplaceManualPagesAsync(
        long manualId,
        IReadOnlyList<ManualPage> pages,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _manualPages.RemoveAll(page => page.ManualId == manualId);

            foreach (var page in pages)
            {
                _manualPages.Add(new ManualPage
                {
                    Id = _nextManualPageId++,
                    ManualId = manualId,
                    PdfPageNumber = page.PdfPageNumber,
                    PrintedPageNumber = page.PrintedPageNumber,
                    Chapter = page.Chapter,
                    PageText = page.PageText,
                    PageImageUrl = page.PageImageUrl
                });
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteManualAsync(long manualId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var removed = _manuals.RemoveAll(manual => manual.Id == manualId) > 0;
            if (removed)
            {
                _manualPages.RemoveAll(page => page.ManualId == manualId);
            }

            return Task.FromResult(removed);
        }
    }

    public Task<IReadOnlyList<KnowledgeReference>> SearchFallbackReferencesAsync(
        long vehicleId,
        string question,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var manuals = _manuals
                .Where(manual => manual.VehicleId == vehicleId && manual.Status == ManualStatus.Completed)
                .ToDictionary(manual => manual.Id);

            var keywords = ExtractKeywords(question);

            var matchedPages = _manualPages
                .Where(page => manuals.ContainsKey(page.ManualId))
                .Select(page => new
                {
                    Page = page,
                    Score = keywords.Count(keyword =>
                        page.PageText.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        page.Chapter.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Page.PdfPageNumber)
                .Take(3)
                .Select(item => item.Page)
                .ToList();

            return Task.FromResult<IReadOnlyList<KnowledgeReference>>(matchedPages.Select(page =>
            {
                var manual = manuals[page.ManualId];
                return new KnowledgeReference
                {
                    DocumentId = manual.Id,
                    DocumentName = manual.FileName,
                    Chapter = page.Chapter,
                    PdfPageNumber = page.PdfPageNumber,
                    PrintedPageNumber = page.PrintedPageNumber,
                    Quote = page.PageText,
                    PageImageUrl = page.PageImageUrl,
                    TotalPages = manual.TotalPages,
                    PdfPageUrl = string.IsNullOrWhiteSpace(manual.PdfUrl)
                        ? ""
                        : $"{manual.PdfUrl}#page={page.PdfPageNumber}"
                };
            }).ToList());
        }
    }

    private static IReadOnlyList<string> ExtractKeywords(string question)
    {
        var keywords = question
            .Split([' ', '，', '。', '？', '?', ',', '.', '、'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim())
            .Where(word => word.Length > 1)
            .ToList();

        foreach (var keyword in new[] { "PDA", "主动驾驶", "驾驶辅助", "高速", "油耗", "黄色三角", "保养", "长途" })
        {
            if (question.Contains(keyword, StringComparison.OrdinalIgnoreCase) && !keywords.Contains(keyword))
            {
                keywords.Add(keyword);
            }
        }

        if (question.Contains("长途", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("公里", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("出远门", StringComparison.OrdinalIgnoreCase))
        {
            keywords.AddRange(["长途", "检查", "保养"]);
        }

        return keywords;
    }

    public Task<IReadOnlyList<ChatHistory>> GetChatHistoryAsync(
        long userId,
        long? vehicleId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ChatHistory>>(_chatHistories
                .Where(history => history.UserId == userId)
                .Where(history => vehicleId is null || history.VehicleId == vehicleId.Value)
                .OrderByDescending(history => history.CreateTime)
                .ToList());
        }
    }

    public Task<IReadOnlyList<ChatHistory>> GetRecentChatHistoryAsync(
        long userId,
        long vehicleId,
        string conversationId,
        int take,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ChatHistory>>(_chatHistories
                .Where(history =>
                    history.UserId == userId &&
                    history.VehicleId == vehicleId &&
                    history.ConversationId == conversationId)
                .OrderByDescending(history => history.CreateTime)
                .Take(take)
                .OrderBy(history => history.CreateTime)
                .ToList());
        }
    }

    public Task<ChatHistory> AddChatHistoryAsync(
        ChatRequest request,
        ChatResponse response,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var history = new ChatHistory
            {
                Id = _nextChatHistoryId++,
                UserId = request.UserId,
                VehicleId = request.VehicleId,
                ConversationId = request.ConversationId ?? "",
                Question = request.Question,
                Answer = response.Answer,
                References = response.References,
                CreateTime = DateTimeOffset.UtcNow
            };

            _chatHistories.Add(history);
            return Task.FromResult(history);
        }
    }
}
