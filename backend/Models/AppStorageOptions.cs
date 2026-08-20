namespace CarManualAssistant.Api.Models;

public sealed class AppStorageOptions
{
    public string ManualRoot { get; set; } = "../storage/manuals";

    public long MaxUploadBytes { get; set; } = 200 * 1024 * 1024;
}
