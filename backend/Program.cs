using CarManualAssistant.Api.Models;
using CarManualAssistant.Api.Services;
using Microsoft.Extensions.FileProviders;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// 允许前端开发服务器访问 API。正式环境建议改成固定域名白名单。
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(origin =>
                origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
    });
});

builder.Services.Configure<AppStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.AddSingleton<ManualFileService>();
builder.Services.AddSingleton<AdminAuthService>();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Memory";
if (string.Equals(databaseProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("使用 PostgreSQL 时必须配置 ConnectionStrings:Default");
    }

    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
    builder.Services.AddSingleton<IAppStore, PostgresAppStore>();
}
else
{
    // 默认使用内存仓储，适合刚克隆项目后的本地演示。
    // 只要配置 Database:Provider=Postgres，就会切换到上面的持久化仓储。
    builder.Services.AddSingleton<IAppStore, AppStore>();
}

// HttpClient 统一由框架管理，避免频繁创建连接导致端口耗尽。
builder.Services.AddHttpClient<AiAssistantClient>(client =>
{
    var baseUrl = builder.Configuration["AiService:BaseUrl"] ?? "http://localhost:8001";
    client.BaseAddress = new Uri(baseUrl);
    // 整本手册首次渲染可能超过 HttpClient 默认的 100 秒。
    // 聊天请求在 AiAssistantClient 内另设较短超时，避免影响用户交互。
    client.Timeout = TimeSpan.FromMinutes(20);
});
builder.Services.AddSingleton<ManualProcessingService>();

var app = builder.Build();

await app.Services.GetRequiredService<IAppStore>().InitializeAsync(CancellationToken.None);

app.UseCors("FrontendDev");

// 将上传的 PDF 和预生成的整页图片作为静态资源开放。
// 业务接口只返回相对 URL，例如 /manuals/12/pages/229.webp，
// 前端拿到 URL 后可以直接展示整页手册图片。
var manualRoot = app.Services.GetRequiredService<ManualFileService>().ManualRoot;
Directory.CreateDirectory(manualRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(manualRoot),
    RequestPath = "/manuals"
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "backend" }));

var api = app.MapGroup("/api");

api.MapPost("/auth/login", async (
    LoginRequest request,
    IAppStore store,
    CancellationToken cancellationToken) =>
{
    // V1 骨架里先做演示登录：只要用户名非空就返回一个开发令牌。
    // 真正上线时这里应替换为密码哈希校验、JWT 签发和刷新令牌逻辑。
    var user = await store.FindOrCreateUserAsync(request.Username, cancellationToken);

    return Results.Ok(new LoginResponse(
        UserId: user.Id,
        Username: user.Username,
        Token: $"dev-token-{user.Id}"
    ));
});

api.MapPost("/admin/auth/login", (
    AdminLoginRequest request,
    AdminAuthService adminAuth) =>
{
    var login = adminAuth.Login(request);

    return login is null
        ? Results.Unauthorized()
        : Results.Ok(login);
});

api.MapGet("/vehicles", async (IAppStore store, CancellationToken cancellationToken) =>
{
    return Results.Ok(await store.GetVehiclesAsync(cancellationToken));
});

api.MapGet("/vehicles/{vehicleId:long}/manual", async (
    long vehicleId,
    IAppStore store,
    CancellationToken cancellationToken) =>
{
    var vehicle = await store.GetVehicleAsync(vehicleId, cancellationToken);
    if (vehicle is null)
    {
        return Results.NotFound(new { message = "车型不存在" });
    }

    var manual = (await store.GetManualsAsync(cancellationToken))
        .Where(item => item.VehicleId == vehicleId && item.Status == ManualStatus.Completed)
        .OrderByDescending(item => item.CreateTime)
        .FirstOrDefault();

    return manual is null
        ? Results.NotFound(new { message = "该车型暂未导入可浏览的用户手册" })
        : Results.Ok(new UserManualResponse(
            manual.Id,
            manual.FileName,
            manual.PdfUrl,
            manual.TotalPages));
});

api.MapPost("/user-vehicles", async (
    CreateUserVehicleRequest request,
    IAppStore store,
    CancellationToken cancellationToken) =>
{
    var selectedVehicle = await store.SetUserVehicleAsync(request, cancellationToken);
    return selectedVehicle is null
        ? Results.NotFound(new { message = "车型不存在" })
        : Results.Ok(selectedVehicle);
});

api.MapGet("/chat/history", async (
    long userId,
    long? vehicleId,
    IAppStore store,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await store.GetChatHistoryAsync(userId, vehicleId, cancellationToken));
});

api.MapPost("/chat", async (
    ChatRequest request,
    IAppStore store,
    AiAssistantClient aiClient,
    CancellationToken cancellationToken) =>
{
    var vehicle = await store.GetVehicleAsync(request.VehicleId, cancellationToken);
    if (vehicle is null)
    {
        return Results.NotFound(new { message = "车型不存在，请先选择正确车型" });
    }

    var question = request.Question?.Trim() ?? "";
    if (question.Length == 0)
    {
        return Results.BadRequest(new { message = "问题不能为空" });
    }

    if (question.Length > 1000)
    {
        return Results.BadRequest(new { message = "问题不能超过 1000 个字符" });
    }

    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("N")
        : request.ConversationId.Trim();

    if (conversationId.Length > 64)
    {
        return Results.BadRequest(new { message = "对话标识格式不正确" });
    }

    var normalizedRequest = request with
    {
        Question = question,
        ConversationId = conversationId
    };

    // 这里取最近几轮对话交给 AI 服务，用于多轮追问。
    // 注意：多轮上下文只用于理解代词和省略，不应该覆盖车型范围检索限制。
    var recentHistory = await store.GetRecentChatHistoryAsync(
        request.UserId,
        request.VehicleId,
        conversationId,
        take: 6,
        cancellationToken);

    var aiResponse = await aiClient.AskAsync(
        normalizedRequest,
        vehicle,
        recentHistory,
        cancellationToken);

    var saved = await store.AddChatHistoryAsync(normalizedRequest, aiResponse, cancellationToken);

    return Results.Ok(new ChatResponse(
        Answer: saved.Answer,
        References: saved.References,
        ChatHistoryId: saved.Id,
        CreateTime: saved.CreateTime
    ));
});

var admin = api.MapGroup("/admin");

// 管理后台和用户端必须分开。这里的保护层只挂在 /api/admin 下，
// 普通用户问答接口仍然可以正常访问；手册上传、删除、重新解析等高风险操作
// 必须先通过 /api/admin/auth/login 拿到后台令牌。
admin.AddEndpointFilter(async (context, next) =>
{
    var adminAuth = context.HttpContext.RequestServices.GetRequiredService<AdminAuthService>();
    var authorizationHeader = context.HttpContext.Request.Headers.Authorization.ToString();

    if (!adminAuth.IsAuthorized(authorizationHeader))
    {
        return Results.Unauthorized();
    }

    return await next(context);
});

admin.MapGet("/manuals", async (IAppStore store, CancellationToken cancellationToken) =>
{
    return Results.Ok(await store.GetManualsAsync(cancellationToken));
});

admin.MapGet("/manuals/{manualId:long}", async (
    long manualId,
    IAppStore store,
    CancellationToken cancellationToken) =>
{
    var manual = await store.GetManualAsync(manualId, cancellationToken);
    return manual is null
        ? Results.NotFound(new { message = "手册不存在" })
        : Results.Ok(manual);
});

admin.MapPost("/manuals", async (
    HttpRequest httpRequest,
    IAppStore store,
    ManualFileService manualFiles,
    ManualProcessingService manualProcessor,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(new { message = "请使用 multipart/form-data 上传 PDF" });
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "请选择用户手册 PDF 文件" });
    }

    // 后台只接受 PDF。这里先用扩展名和 Content-Type 做基础校验，
    // 防止把图片、压缩包或其它二进制文件送进 PDF 解析流程。
    // 生产环境还可以进一步读取文件头，确认 magic number 是 %PDF。
    var isPdfFile = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    var isPdfContent = string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase);

    if (!isPdfFile || !isPdfContent)
    {
        return Results.BadRequest(new { message = "只支持上传 PDF 用户手册" });
    }

    if (!await manualFiles.HasPdfHeaderAsync(file, cancellationToken))
    {
        return Results.BadRequest(new { message = "文件内容不是有效的 PDF" });
    }

    var storageOptions = httpRequest.HttpContext.RequestServices
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<AppStorageOptions>>()
        .Value;

    if (file.Length > storageOptions.MaxUploadBytes)
    {
        return Results.BadRequest(new { message = $"PDF 文件不能超过 {storageOptions.MaxUploadBytes / 1024 / 1024} MB" });
    }

    if (!long.TryParse(form["vehicleId"], out var vehicleId) ||
        await store.GetVehicleAsync(vehicleId, cancellationToken) is null)
    {
        return Results.BadRequest(new { message = "车型不存在" });
    }

    var manual = await store.CreateManualAsync(new CreateManualRequest(
        VehicleId: vehicleId,
        FileName: file.FileName,
        SourceType: form["sourceType"],
        SourceUrl: form["sourceUrl"]
    ), cancellationToken);

    SavedManualFile savedFile;
    try
    {
        savedFile = await manualFiles.SaveOriginalPdfAsync(manual.Id, file, cancellationToken);
        await store.MarkManualUploadedAsync(
            manual.Id,
            savedFile.RelativePdfUrl,
            savedFile.PhysicalPath,
            cancellationToken);
    }
    catch
    {
        await store.DeleteManualAsync(manual.Id, CancellationToken.None);
        manualFiles.DeleteManualDirectory(manual.Id);
        throw;
    }

    await store.UpdateManualStatusAsync(manual.Id, ManualStatus.Processing, cancellationToken);
    _ = manualProcessor.ProcessAsync(
        manual,
        savedFile.PhysicalPath,
        savedFile.PageImageDirectory,
        CancellationToken.None);

    return Results.Accepted(
        $"/api/admin/manuals/{manual.Id}",
        await store.GetManualAsync(manual.Id, cancellationToken));
});

admin.MapPost("/manuals/{manualId:long}/reprocess", async (
    long manualId,
    IAppStore store,
    ManualFileService manualFiles,
    ManualProcessingService manualProcessor,
    CancellationToken cancellationToken) =>
{
    var manual = await store.GetManualAsync(manualId, cancellationToken);
    if (manual is null)
    {
        return Results.NotFound(new { message = "手册不存在" });
    }

    if (string.IsNullOrWhiteSpace(manual.PhysicalPath))
    {
        return Results.BadRequest(new { message = "手册文件路径不存在，无法重新解析" });
    }

    if (manual.Status == ManualStatus.Processing)
    {
        return Results.Conflict(new { message = "该手册正在解析，请稍后再试" });
    }

    await store.UpdateManualStatusAsync(manualId, ManualStatus.Processing, cancellationToken);
    var pageImageDirectory = manualFiles.GetPageImageDirectory(manualId);
    _ = manualProcessor.ProcessAsync(
        manual,
        manual.PhysicalPath,
        pageImageDirectory,
        CancellationToken.None);

    return Results.Accepted(
        $"/api/admin/manuals/{manual.Id}",
        await store.GetManualAsync(manualId, cancellationToken));
});

admin.MapDelete("/manuals/{manualId:long}", async (
    long manualId,
    IAppStore store,
    ManualFileService manualFiles,
    CancellationToken cancellationToken) =>
{
    var manual = await store.GetManualAsync(manualId, cancellationToken);
    if (manual is null)
    {
        return Results.NotFound(new { message = "手册不存在" });
    }

    if (manual.Status == ManualStatus.Processing)
    {
        return Results.Conflict(new { message = "该手册正在解析，暂时不能删除" });
    }

    var removed = await store.DeleteManualAsync(manualId, cancellationToken);
    if (!removed)
    {
        return Results.NotFound(new { message = "手册不存在" });
    }

    manualFiles.DeleteManualDirectory(manualId);
    return Results.NoContent();
});

app.Run();
