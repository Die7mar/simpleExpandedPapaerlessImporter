using System.Text.Json;
using SimpleExpandedPaperlessImporter.Configuration;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Reads and writes user-editable settings to a JSON override file.
/// After saving, calls IConfigurationRoot.Reload() so IOptionsMonitor picks up
/// the new values immediately without restarting the app.
/// </summary>
public class SettingsService(IConfiguration configuration, ILogger<SettingsService> logger)
{
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private string SettingsFilePath =>
        configuration["SettingsFilePath"] ?? Path.Combine(AppContext.BaseDirectory, "settings.json");

    public PaperlessSettings GetCurrent()
    {
        var s = new PaperlessSettings();
        configuration.GetSection("Paperless").Bind(s);
        return s;
    }

    public async Task SaveAsync(PaperlessSettings settings)
    {
        var filePath = SettingsFilePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Wrap in the "Paperless" section key that ASP.NET config expects
        var wrapper = new { Paperless = settings };
        var json = JsonSerializer.Serialize(wrapper, WriteOpts);
        await File.WriteAllTextAsync(filePath, json);
        logger.LogInformation("Settings saved to {Path}", filePath);

        // Trigger live reload so IOptionsMonitor<PaperlessSettings> picks up changes
        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
            logger.LogInformation("Configuration reloaded");
        }
    }
}
