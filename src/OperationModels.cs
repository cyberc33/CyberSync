namespace CyberSync;

public sealed class DestinationSelection
{
    public bool Success { get; init; }
    public string SelectedPath { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

public sealed class DriveResolution
{
    public bool Success { get; init; }
    public string RootPath { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public string ResolvedPath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

public sealed class CommandResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; } = -1;
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class SyncResult
{
    public bool Success { get; init; }
    public bool Warning { get; init; }
    public int ExitCode { get; init; } = -1;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string LogFilePath { get; init; } = string.Empty;
    public string ResolvedDestinationPath { get; init; } = string.Empty;
    public DateTimeOffset FinishedAt { get; init; }
}

public sealed class SyncProgress
{
    public bool IsIndeterminate { get; init; }
    public long TotalBytesToCopy { get; init; }
    public long CompletedBytes { get; init; }
    public long CurrentFileBytes { get; init; }
    public int CurrentFilePercent { get; init; }
    public int TotalSourceCount { get; init; }
    public int CompletedSourceCount { get; init; }
    public string CurrentSourcePath { get; init; } = string.Empty;
    public string CurrentFilePath { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }

    public double OverallPercent =>
        TotalBytesToCopy <= 0
            ? 0
            : Math.Clamp((double)CompletedBytes / TotalBytesToCopy * 100.0, 0, 100);
}
