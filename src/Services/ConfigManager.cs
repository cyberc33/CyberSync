using System.Text.Json;

namespace CyberSync.Services;

public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppConfig Load()
    {
        string path = ConfigFilePath();
        if (!File.Exists(path)) {
            return new AppConfig();
        }

        try {
            string json = File.ReadAllText(path);
            ConfigDocument? document = JsonSerializer.Deserialize<ConfigDocument>(json);
            if (document is null) {
                return new AppConfig();
            }

            TimeOnly dailyTime = TimeOnly.TryParseExact(document.DailyTime, "HH:mm", out TimeOnly parsedTime)
                ? parsedTime
                : new TimeOnly(9, 0);

            DateTimeOffset? lastRunAt = null;
            if (!string.IsNullOrWhiteSpace(document.LastRunAt) &&
                DateTimeOffset.TryParse(document.LastRunAt, out DateTimeOffset parsedDate)) {
                lastRunAt = parsedDate;
            }

            return new AppConfig {
                SourcePaths = LoadSourcePaths(document),
                DestinationVolumeLabel = document.DestinationVolumeLabel ?? string.Empty,
                DestinationRelativePath = document.DestinationRelativePath ?? string.Empty,
                DailyTime = dailyTime,
                CloseToTrayOnClose = document.CloseToTrayOnClose ?? true,
                LastRunStatus = document.LastRunStatus ?? string.Empty,
                LastRunAt = lastRunAt
            };
        } catch {
            return new AppConfig();
        }
    }

    public bool Save(AppConfig config, out string errorMessage)
    {
        errorMessage = string.Empty;
        string appDataPath = AppDataDirPath();
        if (!EnsureDirectory(appDataPath, out errorMessage)) {
            return false;
        }

        var document = new ConfigDocument {
            SourcePath = config.SourcePaths.FirstOrDefault() ?? string.Empty,
            SourcePaths = config.NormalizedSourcePaths().ToList(),
            DestinationVolumeLabel = config.DestinationVolumeLabel,
            DestinationRelativePath = config.DestinationRelativePath,
            DailyTime = config.DailyTime.ToString("HH:mm"),
            CloseToTrayOnClose = config.CloseToTrayOnClose,
            LastRunStatus = config.LastRunStatus,
            LastRunAt = config.LastRunAt?.ToString("O") ?? string.Empty
        };

        string targetPath = ConfigFilePath();
        string tempPath = Path.Combine(appDataPath, "config.json.tmp");

        try {
            string json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, targetPath, true);
            return true;
        } catch (Exception ex) {
            errorMessage = $"Could not write config file: {targetPath}\n{ex.Message}";
            return false;
        }
    }

    public bool UpdateLastRun(string status, DateTimeOffset at, out string errorMessage)
    {
        AppConfig config = Load();
        config.LastRunStatus = status;
        config.LastRunAt = at;
        return Save(config, out errorMessage);
    }

    public static string AppDataDirPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CyberSync");
    }

    public static string ConfigFilePath()
    {
        return Path.Combine(AppDataDirPath(), "config.json");
    }

    public static string LogsDirPath()
    {
        return Path.Combine(AppDataDirPath(), "logs");
    }

    private static bool EnsureDirectory(string path, out string errorMessage)
    {
        errorMessage = string.Empty;

        try {
            Directory.CreateDirectory(path);
            return true;
        } catch (Exception ex) {
            errorMessage = $"Could not create directory: {path}\n{ex.Message}";
            return false;
        }
    }

    private sealed class ConfigDocument
    {
        public string? SourcePath { get; set; }
        public List<string>? SourcePaths { get; set; }
        public string? DestinationVolumeLabel { get; set; }
        public string? DestinationRelativePath { get; set; }
        public string? DailyTime { get; set; }
        public bool? CloseToTrayOnClose { get; set; }
        public string? LastRunStatus { get; set; }
        public string? LastRunAt { get; set; }
    }

    private static List<string> LoadSourcePaths(ConfigDocument document)
    {
        List<string> paths = document.SourcePaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        if (paths.Count == 0 && !string.IsNullOrWhiteSpace(document.SourcePath)) {
            paths.Add(document.SourcePath.Trim());
        }

        return paths;
    }
}
