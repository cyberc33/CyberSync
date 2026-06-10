using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyberSync;

public sealed class AppConfig
{
    public List<string> SourcePaths { get; set; } = [];
    public string DestinationVolumeLabel { get; set; } = string.Empty;
    public string DestinationRelativePath { get; set; } = string.Empty;
    public TimeOnly DailyTime { get; set; } = new(9, 0);
    public bool CloseToTrayOnClose { get; set; } = true;
    public string LastRunStatus { get; set; } = string.Empty;
    public DateTimeOffset? LastRunAt { get; set; }

    public bool IsValid =>
        SourcePaths.Count > 0 &&
        !string.IsNullOrWhiteSpace(DestinationVolumeLabel);

    public IEnumerable<string> NormalizedSourcePaths()
    {
        return SourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
