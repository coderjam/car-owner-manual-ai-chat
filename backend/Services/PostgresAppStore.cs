using System.Text.Json;
using CarManualAssistant.Api.Models;
using Npgsql;
using NpgsqlTypes;

namespace CarManualAssistant.Api.Services;

public sealed class PostgresAppStore : IAppStore
{
    private const long InitialManualId = 1;
    private const long InitialVehicleId = 1;
    private const string InitialManualFileName = "2026款凯美瑞智能电混双擎用户手册-2507版（01999-06154）.pdf";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ManualFileService _manualFiles;
    private readonly ILogger<PostgresAppStore> _logger;

    public PostgresAppStore(
        NpgsqlDataSource dataSource,
        ManualFileService manualFiles,
        ILogger<PostgresAppStore> logger)
    {
        _dataSource = dataSource;
        _manualFiles = manualFiles;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var vectorEnabled = await TryEnableVectorExtensionAsync(cancellationToken);

        await ExecuteNonQueryAsync(
            """
            CREATE TABLE IF NOT EXISTS tb_user (
                id BIGSERIAL PRIMARY KEY,
                username VARCHAR(100) NOT NULL UNIQUE,
                password_hash VARCHAR(255) NOT NULL,
                create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS tb_vehicle (
                id BIGSERIAL PRIMARY KEY,
                brand VARCHAR(100) NOT NULL,
                model VARCHAR(100) NOT NULL,
                year INT NOT NULL,
                engine VARCHAR(100),
                configuration VARCHAR(100),
                create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE UNIQUE INDEX IF NOT EXISTS idx_vehicle_unique_version
                ON tb_vehicle (brand, model, year, (COALESCE(engine, '')), (COALESCE(configuration, '')));

            CREATE TABLE IF NOT EXISTS tb_user_vehicle (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES tb_user(id) ON DELETE CASCADE,
                vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
                buy_date DATE,
                mileage INT,
                create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS tb_manual (
                id BIGSERIAL PRIMARY KEY,
                vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
                file_name VARCHAR(255) NOT NULL,
                file_path VARCHAR(500),
                pdf_url VARCHAR(500),
                source_type VARCHAR(50),
                source_url VARCHAR(500),
                status VARCHAR(50) NOT NULL DEFAULT 'uploaded',
                total_pages INT NOT NULL DEFAULT 0,
                generated_page_images INT NOT NULL DEFAULT 0,
                knowledge_chunks INT NOT NULL DEFAULT 0,
                create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_manual_vehicle_status
                ON tb_manual (vehicle_id, status);

            CREATE TABLE IF NOT EXISTS tb_manual_page (
                id BIGSERIAL PRIMARY KEY,
                manual_id BIGINT NOT NULL REFERENCES tb_manual(id) ON DELETE CASCADE,
                pdf_page_number INT NOT NULL,
                printed_page_number INT,
                chapter VARCHAR(255),
                page_text TEXT,
                page_image_url VARCHAR(500),
                create_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (manual_id, pdf_page_number)
            );

            CREATE INDEX IF NOT EXISTS idx_manual_page_manual_page
                ON tb_manual_page (manual_id, pdf_page_number);

            CREATE TABLE IF NOT EXISTS tb_chat_history (
                id BIGSERIAL PRIMARY KEY,
                user_id BIGINT NOT NULL REFERENCES tb_user(id) ON DELETE CASCADE,
                vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
                conversation_id VARCHAR(64),
                question TEXT NOT NULL,
                answer TEXT NOT NULL,
                references_json JSONB NOT NULL DEFAULT '[]'::jsonb,
                create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            ALTER TABLE tb_chat_history
                ADD COLUMN IF NOT EXISTS conversation_id VARCHAR(64);

            CREATE INDEX IF NOT EXISTS idx_chat_user_vehicle_time
                ON tb_chat_history (user_id, vehicle_id, create_time DESC);

            CREATE INDEX IF NOT EXISTS idx_chat_conversation_time
                ON tb_chat_history (user_id, vehicle_id, conversation_id, create_time);
            """,
            cancellationToken);

        if (vectorEnabled)
        {
            await ExecuteNonQueryAsync(
                """
                CREATE TABLE IF NOT EXISTS tb_knowledge_chunk (
                    id BIGSERIAL PRIMARY KEY,
                    manual_id BIGINT NOT NULL REFERENCES tb_manual(id) ON DELETE CASCADE,
                    manual_page_id BIGINT NOT NULL REFERENCES tb_manual_page(id) ON DELETE CASCADE,
                    vehicle_id BIGINT NOT NULL REFERENCES tb_vehicle(id) ON DELETE RESTRICT,
                    chapter VARCHAR(255),
                    content TEXT NOT NULL,
                    chunk_index INT NOT NULL,
                    embedding VECTOR(1536),
                    create_time TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS idx_chunk_vehicle_manual
                    ON tb_knowledge_chunk (vehicle_id, manual_id);

                CREATE INDEX IF NOT EXISTS idx_chunk_embedding_hnsw
                    ON tb_knowledge_chunk
                    USING hnsw (embedding vector_cosine_ops);
                """,
                cancellationToken);
        }

        await SeedBaseDataAsync(cancellationToken);
        await ImportInitialManualAsync(cancellationToken);
    }

    public async Task<User> FindOrCreateUserAsync(string username, CancellationToken cancellationToken)
    {
        username = string.IsNullOrWhiteSpace(username) ? "demo" : username.Trim();

        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tb_user (username, password_hash)
            VALUES (@username, 'dev-password-hash')
            ON CONFLICT (username)
            DO UPDATE SET username = EXCLUDED.username
            RETURNING id, username, create_time;
            """);

        command.Parameters.AddWithValue("username", username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return ReadUser(reader);
    }

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, brand, model, year, engine, configuration, create_time
            FROM tb_vehicle
            ORDER BY brand, model, year DESC, configuration;
            """);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var vehicles = new List<Vehicle>();

        while (await reader.ReadAsync(cancellationToken))
        {
            vehicles.Add(ReadVehicle(reader));
        }

        return vehicles;
    }

    public async Task<Vehicle?> GetVehicleAsync(long vehicleId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, brand, model, year, engine, configuration, create_time
            FROM tb_vehicle
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", vehicleId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVehicle(reader) : null;
    }

    public async Task<UserVehicle?> SetUserVehicleAsync(
        CreateUserVehicleRequest request,
        CancellationToken cancellationToken)
    {
        if (await GetVehicleAsync(request.VehicleId, cancellationToken) is null)
        {
            return null;
        }

        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tb_user_vehicle (user_id, vehicle_id, buy_date, mileage)
            VALUES (@user_id, @vehicle_id, @buy_date, @mileage)
            RETURNING id, user_id, vehicle_id, buy_date, mileage, create_time;
            """);

        command.Parameters.AddWithValue("user_id", request.UserId);
        command.Parameters.AddWithValue("vehicle_id", request.VehicleId);
        command.Parameters.Add("buy_date", NpgsqlDbType.Date).Value = DbValue(request.BuyDate);
        command.Parameters.Add("mileage", NpgsqlDbType.Integer).Value = DbValue(request.Mileage);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return ReadUserVehicle(reader);
    }

    public async Task<IReadOnlyList<Manual>> GetManualsAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, vehicle_id, file_name, pdf_url, file_path, source_type, source_url,
                   status, total_pages, generated_page_images, knowledge_chunks, create_time
            FROM tb_manual
            ORDER BY create_time DESC;
            """);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var manuals = new List<Manual>();

        while (await reader.ReadAsync(cancellationToken))
        {
            manuals.Add(ReadManual(reader));
        }

        return manuals;
    }

    public async Task<Manual?> GetManualAsync(long manualId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, vehicle_id, file_name, pdf_url, file_path, source_type, source_url,
                   status, total_pages, generated_page_images, knowledge_chunks, create_time
            FROM tb_manual
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", manualId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadManual(reader) : null;
    }

    public async Task<Manual> CreateManualAsync(
        CreateManualRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tb_manual (vehicle_id, file_name, source_type, source_url, status)
            VALUES (@vehicle_id, @file_name, @source_type, @source_url, @status)
            RETURNING id, vehicle_id, file_name, pdf_url, file_path, source_type, source_url,
                      status, total_pages, generated_page_images, knowledge_chunks, create_time;
            """);

        command.Parameters.AddWithValue("vehicle_id", request.VehicleId);
        command.Parameters.AddWithValue("file_name", request.FileName);
        command.Parameters.Add("source_type", NpgsqlDbType.Varchar).Value = DbValue(request.SourceType);
        command.Parameters.Add("source_url", NpgsqlDbType.Varchar).Value = DbValue(request.SourceUrl);
        command.Parameters.AddWithValue("status", ManualStatus.Uploaded);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return ReadManual(reader);
    }

    public async Task MarkManualUploadedAsync(
        long manualId,
        string relativePdfUrl,
        string physicalPath,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE tb_manual
            SET pdf_url = @pdf_url,
                file_path = @file_path,
                status = @status
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", manualId);
        command.Parameters.AddWithValue("pdf_url", relativePdfUrl);
        command.Parameters.AddWithValue("file_path", physicalPath);
        command.Parameters.AddWithValue("status", ManualStatus.Uploaded);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateManualStatusAsync(
        long manualId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE tb_manual
            SET status = @status
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", manualId);
        command.Parameters.AddWithValue("status", status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateManualStatsAsync(
        long manualId,
        string status,
        int totalPages,
        int generatedPageImages,
        int knowledgeChunks,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE tb_manual
            SET status = @status,
                total_pages = @total_pages,
                generated_page_images = @generated_page_images,
                knowledge_chunks = @knowledge_chunks
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", manualId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("total_pages", totalPages);
        command.Parameters.AddWithValue("generated_page_images", generatedPageImages);
        command.Parameters.AddWithValue("knowledge_chunks", knowledgeChunks);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReplaceManualPagesAsync(
        long manualId,
        IReadOnlyList<ManualPage> pages,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var deleteCommand = new NpgsqlCommand(
                "DELETE FROM tb_manual_page WHERE manual_id = @manual_id;",
                connection,
                transaction))
            {
                deleteCommand.Parameters.AddWithValue("manual_id", manualId);
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var page in pages)
            {
                // 一个引用页最重要的定位信息是 pdf_page_number。
                // printed_page_number 只用于给用户看，可能因为封面、目录页导致和 PDF 页序不同。
                await using var insertCommand = new NpgsqlCommand(
                    """
                    INSERT INTO tb_manual_page (
                        manual_id,
                        pdf_page_number,
                        printed_page_number,
                        chapter,
                        page_text,
                        page_image_url
                    )
                    VALUES (
                        @manual_id,
                        @pdf_page_number,
                        @printed_page_number,
                        @chapter,
                        @page_text,
                        @page_image_url
                    );
                    """,
                    connection,
                    transaction);

                insertCommand.Parameters.AddWithValue("manual_id", manualId);
                insertCommand.Parameters.AddWithValue("pdf_page_number", page.PdfPageNumber);
                insertCommand.Parameters.Add("printed_page_number", NpgsqlDbType.Integer).Value = DbValue(page.PrintedPageNumber);
                insertCommand.Parameters.Add("chapter", NpgsqlDbType.Varchar).Value = DbValue(page.Chapter);
                insertCommand.Parameters.Add("page_text", NpgsqlDbType.Text).Value = DbValue(page.PageText);
                insertCommand.Parameters.Add("page_image_url", NpgsqlDbType.Varchar).Value = DbValue(page.PageImageUrl);

                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> DeleteManualAsync(long manualId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            DELETE FROM tb_manual
            WHERE id = @id;
            """);

        command.Parameters.AddWithValue("id", manualId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<KnowledgeReference>> SearchFallbackReferencesAsync(
        long vehicleId,
        string question,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT p.id, p.manual_id, p.pdf_page_number, p.printed_page_number,
                   p.chapter, p.page_text, p.page_image_url,
                   m.file_name, m.pdf_url, m.total_pages
            FROM tb_manual_page p
            INNER JOIN tb_manual m ON m.id = p.manual_id
            WHERE m.vehicle_id = @vehicle_id
              AND m.status = @status
            ORDER BY p.pdf_page_number;
            """);

        command.Parameters.AddWithValue("vehicle_id", vehicleId);
        command.Parameters.AddWithValue("status", ManualStatus.Completed);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pages = new List<(ManualPage Page, string DocumentName, string? PdfUrl, int TotalPages)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            pages.Add((
                new ManualPage
                {
                    Id = reader.GetInt64(0),
                    ManualId = reader.GetInt64(1),
                    PdfPageNumber = reader.GetInt32(2),
                    PrintedPageNumber = GetNullableInt32(reader, 3),
                    Chapter = GetNullableString(reader, 4),
                    PageText = GetNullableString(reader, 5),
                    PageImageUrl = GetNullableString(reader, 6)
                },
                reader.GetString(7),
                GetNullableString(reader, 8),
                reader.GetInt32(9)));
        }

        var keywords = ExtractKeywords(question);

        var matchedPages = pages
            .Select(item => new
            {
                item.Page,
                item.DocumentName,
                item.PdfUrl,
                item.TotalPages,
                Score = keywords.Count(keyword =>
                    item.Page.PageText.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.Page.Chapter.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Page.PdfPageNumber)
            .Take(3)
            .ToList();

        return matchedPages.Select(item => new KnowledgeReference
        {
            DocumentId = item.Page.ManualId,
            DocumentName = item.DocumentName,
            Chapter = item.Page.Chapter,
            PdfPageNumber = item.Page.PdfPageNumber,
            PrintedPageNumber = item.Page.PrintedPageNumber,
            Quote = item.Page.PageText,
            PageImageUrl = item.Page.PageImageUrl,
            TotalPages = item.TotalPages,
            PdfPageUrl = string.IsNullOrWhiteSpace(item.PdfUrl)
                ? ""
                : $"{item.PdfUrl}#page={item.Page.PdfPageNumber}"
        }).ToList();
    }

    public async Task<IReadOnlyList<ChatHistory>> GetChatHistoryAsync(
        long userId,
        long? vehicleId,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, user_id, vehicle_id, conversation_id, question, answer, references_json, create_time
            FROM tb_chat_history
            WHERE user_id = @user_id
              AND (@vehicle_id IS NULL OR vehicle_id = @vehicle_id)
            ORDER BY create_time DESC;
            """);

        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.Add("vehicle_id", NpgsqlDbType.Bigint).Value = DbValue(vehicleId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<ChatHistory>();

        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(ReadChatHistory(reader));
        }

        return history;
    }

    public async Task<IReadOnlyList<ChatHistory>> GetRecentChatHistoryAsync(
        long userId,
        long vehicleId,
        string conversationId,
        int take,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT id, user_id, vehicle_id, conversation_id, question, answer, references_json, create_time
            FROM tb_chat_history
            WHERE user_id = @user_id
              AND vehicle_id = @vehicle_id
              AND conversation_id = @conversation_id
            ORDER BY create_time DESC
            LIMIT @take;
            """);

        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("vehicle_id", vehicleId);
        command.Parameters.AddWithValue("conversation_id", conversationId);
        command.Parameters.AddWithValue("take", take);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<ChatHistory>();

        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(ReadChatHistory(reader));
        }

        return history
            .OrderBy(item => item.CreateTime)
            .ToList();
    }

    public async Task<ChatHistory> AddChatHistoryAsync(
        ChatRequest request,
        ChatResponse response,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO tb_chat_history (
                user_id,
                vehicle_id,
                conversation_id,
                question,
                answer,
                references_json
            )
            VALUES (
                @user_id,
                @vehicle_id,
                @conversation_id,
                @question,
                @answer,
                @references_json
            )
            RETURNING id, user_id, vehicle_id, conversation_id, question, answer, references_json, create_time;
            """);

        command.Parameters.AddWithValue("user_id", request.UserId);
        command.Parameters.AddWithValue("vehicle_id", request.VehicleId);
        command.Parameters.AddWithValue("conversation_id", request.ConversationId ?? "");
        command.Parameters.AddWithValue("question", request.Question);
        command.Parameters.AddWithValue("answer", response.Answer);
        command.Parameters.Add("references_json", NpgsqlDbType.Jsonb)
            .Value = JsonSerializer.Serialize(response.References, JsonOptions);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return ReadChatHistory(reader);
    }

    private async Task<bool> TryEnableVectorExtensionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteNonQueryAsync("CREATE EXTENSION IF NOT EXISTS vector;", cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            // pgvector 不是后端基础业务表运行的硬前提。
            // 如果当前数据库没有安装 vector 扩展，业务数据仍然可以落库；
            // 后续真实向量检索可以在安装 pgvector 后重新执行 database/schema.sql。
            _logger.LogWarning(exception, "pgvector extension is not available");
            return false;
        }
    }

    private async Task SeedBaseDataAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            INSERT INTO tb_user (id, username, password_hash)
            VALUES (1, 'demo', 'dev-password-hash')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO tb_vehicle (id, brand, model, year, engine, configuration)
            VALUES (1, '丰田', '凯美瑞', 2026, '智能电混双擎', '通用版')
            ON CONFLICT (id)
            DO UPDATE SET
                brand = EXCLUDED.brand,
                model = EXCLUDED.model,
                year = EXCLUDED.year,
                engine = EXCLUDED.engine,
                configuration = EXCLUDED.configuration;

            SELECT setval('tb_user_id_seq', GREATEST((SELECT MAX(id) FROM tb_user), 1));
            SELECT setval('tb_vehicle_id_seq', GREATEST((SELECT MAX(id) FROM tb_vehicle), 1));
            """,
            cancellationToken);
    }

    private async Task ImportInitialManualAsync(CancellationToken cancellationToken)
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

        await using (var command = _dataSource.CreateCommand(
            """
            INSERT INTO tb_manual (
                id,
                vehicle_id,
                file_name,
                file_path,
                pdf_url,
                source_type,
                status,
                total_pages,
                generated_page_images,
                knowledge_chunks
            )
            VALUES (
                @id,
                @vehicle_id,
                @file_name,
                @file_path,
                @pdf_url,
                'official',
                @status,
                @total_pages,
                @generated_page_images,
                @knowledge_chunks
            )
            ON CONFLICT (id)
            DO UPDATE SET
                vehicle_id = EXCLUDED.vehicle_id,
                file_name = EXCLUDED.file_name,
                file_path = EXCLUDED.file_path,
                pdf_url = EXCLUDED.pdf_url,
                source_type = EXCLUDED.source_type,
                status = EXCLUDED.status,
                total_pages = EXCLUDED.total_pages,
                generated_page_images = EXCLUDED.generated_page_images,
                knowledge_chunks = EXCLUDED.knowledge_chunks;
            """))
        {
            command.Parameters.AddWithValue("id", InitialManualId);
            command.Parameters.AddWithValue("vehicle_id", InitialVehicleId);
            command.Parameters.AddWithValue("file_name", InitialManualFileName);
            command.Parameters.AddWithValue("file_path", pdfPath);
            command.Parameters.AddWithValue("pdf_url", $"/manuals/{InitialManualId}/original.pdf");
            command.Parameters.AddWithValue(
                "status",
                manifest is null ? ManualStatus.Uploaded : ManualStatus.Completed);
            command.Parameters.AddWithValue("total_pages", manifest?.TotalPages ?? 0);
            command.Parameters.AddWithValue("generated_page_images", manifest?.GeneratedPageImages ?? 0);
            command.Parameters.AddWithValue("knowledge_chunks", manifest?.KnowledgeChunks ?? 0);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (
            manifest is not null &&
            await GetManualPageCountAsync(InitialManualId, cancellationToken) != manifest.Pages.Count)
        {
            await ReplaceManualPagesAsync(InitialManualId, manifest.Pages, cancellationToken);
        }

        await ExecuteNonQueryAsync(
            """
            SELECT setval('tb_manual_id_seq', GREATEST((SELECT MAX(id) FROM tb_manual), 1));
            SELECT setval('tb_manual_page_id_seq', GREATEST((SELECT MAX(id) FROM tb_manual_page), 1));
            """,
            cancellationToken);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetManualPageCountAsync(
        long manualId,
        CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT COUNT(*) FROM tb_manual_page WHERE manual_id = @manual_id;");
        command.Parameters.AddWithValue("manual_id", manualId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static User ReadUser(NpgsqlDataReader reader)
    {
        return new User
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Username = reader.GetString(reader.GetOrdinal("username")),
            CreateTime = GetTimestamp(reader, reader.GetOrdinal("create_time"))
        };
    }

    private static Vehicle ReadVehicle(NpgsqlDataReader reader)
    {
        return new Vehicle
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Brand = reader.GetString(reader.GetOrdinal("brand")),
            Model = reader.GetString(reader.GetOrdinal("model")),
            Year = reader.GetInt32(reader.GetOrdinal("year")),
            Engine = GetNullableString(reader, reader.GetOrdinal("engine")),
            Configuration = GetNullableString(reader, reader.GetOrdinal("configuration")),
            CreateTime = GetTimestamp(reader, reader.GetOrdinal("create_time"))
        };
    }

    private static UserVehicle ReadUserVehicle(NpgsqlDataReader reader)
    {
        var buyDateOrdinal = reader.GetOrdinal("buy_date");

        return new UserVehicle
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            UserId = reader.GetInt64(reader.GetOrdinal("user_id")),
            VehicleId = reader.GetInt64(reader.GetOrdinal("vehicle_id")),
            BuyDate = reader.IsDBNull(buyDateOrdinal)
                ? null
                : DateOnly.FromDateTime(reader.GetDateTime(buyDateOrdinal)),
            Mileage = GetNullableInt32(reader, reader.GetOrdinal("mileage")),
            CreateTime = GetTimestamp(reader, reader.GetOrdinal("create_time"))
        };
    }

    private static Manual ReadManual(NpgsqlDataReader reader)
    {
        return new Manual
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            VehicleId = reader.GetInt64(reader.GetOrdinal("vehicle_id")),
            FileName = reader.GetString(reader.GetOrdinal("file_name")),
            PdfUrl = GetNullableString(reader, reader.GetOrdinal("pdf_url")),
            PhysicalPath = GetNullableString(reader, reader.GetOrdinal("file_path")),
            SourceType = GetNullableString(reader, reader.GetOrdinal("source_type")),
            SourceUrl = GetNullableString(reader, reader.GetOrdinal("source_url")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            TotalPages = reader.GetInt32(reader.GetOrdinal("total_pages")),
            GeneratedPageImages = reader.GetInt32(reader.GetOrdinal("generated_page_images")),
            KnowledgeChunks = reader.GetInt32(reader.GetOrdinal("knowledge_chunks")),
            CreateTime = GetTimestamp(reader, reader.GetOrdinal("create_time"))
        };
    }

    private static ChatHistory ReadChatHistory(NpgsqlDataReader reader)
    {
        var referencesJson = reader.GetString(reader.GetOrdinal("references_json"));
        var references = JsonSerializer.Deserialize<List<KnowledgeReference>>(
            referencesJson,
            JsonOptions) ?? [];

        return new ChatHistory
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            UserId = reader.GetInt64(reader.GetOrdinal("user_id")),
            VehicleId = reader.GetInt64(reader.GetOrdinal("vehicle_id")),
            ConversationId = GetNullableString(reader, reader.GetOrdinal("conversation_id")),
            Question = reader.GetString(reader.GetOrdinal("question")),
            Answer = reader.GetString(reader.GetOrdinal("answer")),
            References = references,
            CreateTime = GetTimestamp(reader, reader.GetOrdinal("create_time"))
        };
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
            question.Contains("公里", StringComparison.OrdinalIgnoreCase))
        {
            keywords.AddRange(["长途", "检查", "保养"]);
        }

        return keywords;
    }

    private static string GetNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset GetTimestamp(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return new DateTimeOffset(utcValue);
    }

    private static object DbValue<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }
}
