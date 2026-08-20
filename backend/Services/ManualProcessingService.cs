using CarManualAssistant.Api.Models;

namespace CarManualAssistant.Api.Services;

public sealed class ManualProcessingService
{
    private readonly AiAssistantClient _aiClient;
    private readonly IAppStore _store;
    private readonly ILogger<ManualProcessingService> _logger;

    public ManualProcessingService(
        AiAssistantClient aiClient,
        IAppStore store,
        ILogger<ManualProcessingService> logger)
    {
        _aiClient = aiClient;
        _store = store;
        _logger = logger;
    }

    public async Task ProcessAsync(
        Manual manual,
        string physicalPath,
        string pageImageDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _aiClient.ProcessManualAsync(
                manual.Id,
                manual.VehicleId,
                manual.FileName,
                physicalPath,
                pageImageDirectory,
                cancellationToken);

            await _store.ReplaceManualPagesAsync(manual.Id, result.Pages, cancellationToken);
            await _store.UpdateManualStatsAsync(
                manual.Id,
                ManualStatus.Completed,
                result.TotalPages,
                result.GeneratedPageImages,
                result.KnowledgeChunks,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process manual {ManualId}", manual.Id);

            try
            {
                await _store.UpdateManualStatusAsync(
                    manual.Id,
                    ManualStatus.Failed,
                    CancellationToken.None);
            }
            catch (Exception statusException)
            {
                _logger.LogError(
                    statusException,
                    "Failed to mark manual {ManualId} as failed",
                    manual.Id);
            }
        }
    }
}
