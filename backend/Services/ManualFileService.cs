using CarManualAssistant.Api.Models;
using Microsoft.Extensions.Options;

namespace CarManualAssistant.Api.Services;

public sealed class ManualFileService
{
    private static readonly byte[] PdfHeader = "%PDF-"u8.ToArray();
    private readonly IWebHostEnvironment _environment;
    private readonly AppStorageOptions _options;

    public ManualFileService(IWebHostEnvironment environment, IOptions<AppStorageOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public string ManualRoot
    {
        get
        {
            if (Path.IsPathRooted(_options.ManualRoot))
            {
                return _options.ManualRoot;
            }

            return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.ManualRoot));
        }
    }

    public async Task<SavedManualFile> SaveOriginalPdfAsync(
        long manualId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var manualDirectory = GetManualDirectory(manualId);
        var pageImageDirectory = GetPageImageDirectory(manualId);

        Directory.CreateDirectory(manualDirectory);
        Directory.CreateDirectory(pageImageDirectory);

        var physicalPath = Path.Combine(manualDirectory, "original.pdf");

        await using var stream = File.Create(physicalPath);
        await file.CopyToAsync(stream, cancellationToken);

        return new SavedManualFile(
            PhysicalPath: physicalPath,
            RelativePdfUrl: $"/manuals/{manualId}/original.pdf",
            PageImageDirectory: pageImageDirectory);
    }

    public async Task<bool> HasPdfHeaderAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[(int)Math.Min(1024, file.Length)];
        var totalRead = 0;

        while (totalRead < header.Length)
        {
            var bytesRead = await stream.ReadAsync(
                header.AsMemory(totalRead, header.Length - totalRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        return header.AsSpan(0, totalRead).IndexOf(PdfHeader) >= 0;
    }

    public string GetManualDirectory(long manualId)
    {
        return Path.Combine(ManualRoot, manualId.ToString());
    }

    public string GetPageImageDirectory(long manualId)
    {
        return Path.Combine(GetManualDirectory(manualId), "pages");
    }

    public string GetOriginalPdfPath(long manualId)
    {
        return Path.Combine(GetManualDirectory(manualId), "original.pdf");
    }

    public string GetManifestPath(long manualId)
    {
        return Path.Combine(GetManualDirectory(manualId), "manifest.json");
    }

    public void DeleteManualDirectory(long manualId)
    {
        var directory = GetManualDirectory(manualId);

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
