using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CyberSync.Services;

public sealed class SyncService
{
    private static readonly Regex ProgressRegex = new(@"^\s*(\d{1,3})%\s*$", RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"(\d[\d\.\,\s]*)\s*$", RegexOptions.Compiled);

    public async Task<SyncResult> RunSyncAsync(AppConfig config, IProgress<SyncProgress>? progress = null)
    {
        string logFilePath = CreateLogFilePath();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        AppendLogLine(logFilePath, $"[{DateTimeOffset.Now:O}] CyberSync started.");

        if (!config.IsValid) {
            const string message = "CyberSync is not configured yet. Save at least one source folder and the destination drive first.";
            AppendLogLine(logFilePath, message);
            return FailureResult(logFilePath, message);
        }

        string[] sourcePaths = config.NormalizedSourcePaths().ToArray();
        if (sourcePaths.Length == 0) {
            const string message = "No valid source folders are configured.";
            AppendLogLine(logFilePath, message);
            return FailureResult(logFilePath, message);
        }

        string? duplicateName = FindDuplicateDestinationFolderName(sourcePaths);
        if (!string.IsNullOrWhiteSpace(duplicateName)) {
            string message = $"Two source folders would map to the same destination folder name: {duplicateName}";
            AppendLogLine(logFilePath, message);
            return FailureResult(logFilePath, message);
        }

        DriveResolution resolution = DriveResolver.ResolveConfiguredDestination(config);
        if (!resolution.Success) {
            AppendLogLine(logFilePath, resolution.ErrorMessage);
            return FailureResult(logFilePath, resolution.ErrorMessage);
        }

        AppendLogLine(logFilePath, $"Resolved destination: {resolution.ResolvedPath}");
        AppendLogLine(logFilePath, $"Source folder count: {sourcePaths.Length}");

        List<PlannedCopyEntry> planEntries = await BuildSyncPlanAsync(sourcePaths, resolution.ResolvedPath, logFilePath);
        long totalBytesToCopy = planEntries.Sum(entry => entry.Bytes);
        progress?.Report(new SyncProgress {
            IsIndeterminate = totalBytesToCopy == 0,
            TotalBytesToCopy = totalBytesToCopy,
            CompletedBytes = 0,
            TotalSourceCount = sourcePaths.Length,
            CompletedSourceCount = 0,
            StatusText = totalBytesToCopy == 0
                ? "Scanning complete. No changed files detected yet."
                : $"Ready to copy {FormatBytes(totalBytesToCopy)} of changes.",
            StartedAt = startedAt
        });

        int successCount = 0;
        int warningCount = 0;
        int failureCount = 0;
        int highestExitCode = 0;
        int completedSourceCount = 0;
        long completedBytes = 0;

        foreach (string sourcePath in sourcePaths) {
            if (!Directory.Exists(sourcePath)) {
                failureCount++;
                AppendLogLine(logFilePath, $"Source folder not found: {sourcePath}");
                completedSourceCount++;
                continue;
            }

            string destinationPath = Path.Combine(
                resolution.ResolvedPath,
                GetDestinationFolderName(sourcePath));
            Directory.CreateDirectory(destinationPath);

            AppendLogLine(logFilePath, $"Syncing '{sourcePath}' -> '{destinationPath}'");

            IReadOnlyDictionary<string, long> plannedFiles = planEntries
                .Where(entry => string.Equals(entry.SourceRootPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(entry => entry.SourceFilePath, entry => entry.Bytes, StringComparer.OrdinalIgnoreCase);

            CommandResult commandResult = await RunRobocopyAsync(
                sourcePath,
                destinationPath,
                logFilePath,
                plannedFiles,
                totalBytesToCopy,
                completedBytes,
                sourcePaths.Length,
                completedSourceCount,
                startedAt,
                progress);
            highestExitCode = Math.Max(highestExitCode, commandResult.ExitCode);
            completedBytes += plannedFiles.Values.Sum();
            completedSourceCount++;

            if (commandResult.ExitCode >= 8 || !commandResult.Success) {
                failureCount++;
                AppendLogLine(logFilePath, $"Source sync failed: {sourcePath}");
                progress?.Report(new SyncProgress {
                    IsIndeterminate = totalBytesToCopy == 0,
                    TotalBytesToCopy = totalBytesToCopy,
                    CompletedBytes = Math.Min(completedBytes, totalBytesToCopy),
                    TotalSourceCount = sourcePaths.Length,
                    CompletedSourceCount = completedSourceCount,
                    CurrentSourcePath = sourcePath,
                    StatusText = $"Source failed: {Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath))}",
                    StartedAt = startedAt
                });
                continue;
            }

            successCount++;
            if (commandResult.ExitCode > 0) {
                warningCount++;
            }

            progress?.Report(new SyncProgress {
                IsIndeterminate = totalBytesToCopy == 0,
                TotalBytesToCopy = totalBytesToCopy,
                CompletedBytes = Math.Min(completedBytes, totalBytesToCopy),
                TotalSourceCount = sourcePaths.Length,
                CompletedSourceCount = completedSourceCount,
                CurrentSourcePath = sourcePath,
                StatusText = $"Finished {Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath))}",
                StartedAt = startedAt
            });
        }

        return BuildAggregateResult(
            logFilePath,
            resolution.ResolvedPath,
            sourcePaths.Length,
            successCount,
            warningCount,
            failureCount,
            highestExitCode);
    }

    private static async Task<CommandResult> RunRobocopyAsync(
        string sourcePath,
        string destinationPath,
        string logFilePath,
        IReadOnlyDictionary<string, long> plannedFiles,
        long totalBytesToCopy,
        long completedBytesBeforeSource,
        int totalSourceCount,
        int completedSourceCount,
        DateTimeOffset startedAt,
        IProgress<SyncProgress>? progress)
    {
        var startInfo = new ProcessStartInfo("robocopy") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        string[] arguments = [
            sourcePath,
            destinationPath,
            "/MIR",
            "/FFT",
            "/R:2",
            "/W:5",
            "/XJ",
            "/BYTES",
            "/FP",
            "/ETA",
            "/NJH",
            "/NJS",
            $"/LOG+:{logFilePath}"
        ];

        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        try {
            using var process = Process.Start(startInfo);
            if (process is null) {
                AppendLogLine(logFilePath, "Could not start robocopy.");
                return new CommandResult {
                    Success = false,
                    ExitCode = 16,
                    Message = "Could not start robocopy."
                };
            }

            var tracker = new RobocopyProgressTracker(
                sourcePath,
                plannedFiles,
                totalBytesToCopy,
                completedBytesBeforeSource,
                totalSourceCount,
                completedSourceCount,
                startedAt,
                progress);

            Task<string> stdOutTask = ReadAndTrackOutputAsync(
                process.StandardOutput,
                line => {
                    AppendLogLine(logFilePath, line);
                    tracker.ProcessLine(line);
                });
            Task<string> stdErrTask = ReadAndTrackOutputAsync(
                process.StandardError,
                line => {
                    AppendLogLine(logFilePath, line);
                    tracker.ProcessLine(line);
                });

            await process.WaitForExitAsync();
            string stdOut = await stdOutTask;
            string stdErr = await stdErrTask;
            tracker.MarkSourceFinished();

            AppendLogLine(logFilePath, $"robocopy exit code: {process.ExitCode}");

            return new CommandResult {
                Success = process.ExitCode < 8,
                ExitCode = process.ExitCode,
                StdOut = stdOut.Trim(),
                StdErr = stdErr.Trim(),
                Message = process.ExitCode < 8
                    ? "robocopy completed."
                    : "robocopy failed."
            };
        } catch (Exception ex) {
            string message = $"Could not start robocopy: {ex.Message}";
            AppendLogLine(logFilePath, message);
            return new CommandResult {
                Success = false,
                ExitCode = 16,
                Message = message,
                StdErr = ex.Message
            };
        }
    }

    private static SyncResult FailureResult(string logFilePath, string message)
    {
        return new SyncResult {
            Success = false,
            Warning = false,
            ExitCode = 1,
            Status = "failure",
            Message = message,
            LogFilePath = logFilePath,
            FinishedAt = DateTimeOffset.Now
        };
    }

    private static string CreateLogFilePath()
    {
        Directory.CreateDirectory(ConfigManager.LogsDirPath());
        string fileName = $"cybersync-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log";
        return Path.Combine(ConfigManager.LogsDirPath(), fileName);
    }

    private static void AppendLogLine(string logFilePath, string line)
    {
        File.AppendAllText(logFilePath, line + Environment.NewLine);
    }

    private static async Task<List<PlannedCopyEntry>> BuildSyncPlanAsync(
        IEnumerable<string> sourcePaths,
        string resolvedDestinationPath,
        string logFilePath)
    {
        var planEntries = new List<PlannedCopyEntry>();

        foreach (string sourcePath in sourcePaths) {
            string destinationPath = Path.Combine(
                resolvedDestinationPath,
                GetDestinationFolderName(sourcePath));

            foreach (PlannedCopyEntry entry in await GetPlannedCopyEntriesAsync(sourcePath, destinationPath, logFilePath)) {
                planEntries.Add(entry);
            }
        }

        return planEntries;
    }

    private static async Task<List<PlannedCopyEntry>> GetPlannedCopyEntriesAsync(
        string sourcePath,
        string destinationPath,
        string logFilePath)
    {
        var startInfo = new ProcessStartInfo("robocopy") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        string[] arguments = [
            sourcePath,
            destinationPath,
            "/L",
            "/MIR",
            "/FFT",
            "/R:0",
            "/W:0",
            "/XJ",
            "/BYTES",
            "/FP",
            "/NJH",
            "/NJS",
            "/NDL"
        ];

        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null) {
            return [];
        }

        string output = await ReadAndTrackOutputAsync(process.StandardOutput, line => AppendLogLine(logFilePath, $"[plan] {line}"));
        string errors = await ReadAndTrackOutputAsync(process.StandardError, line => AppendLogLine(logFilePath, $"[plan] {line}"));
        await process.WaitForExitAsync();

        var entries = new List<PlannedCopyEntry>();
        foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)) {
            if (TryParsePlannedFileLine(line, sourcePath, out string filePath, out long bytes)) {
                entries.Add(new PlannedCopyEntry(sourcePath, filePath, bytes));
            }
        }

        if (!string.IsNullOrWhiteSpace(errors)) {
            AppendLogLine(logFilePath, $"[plan] {errors.Trim()}");
        }

        return entries;
    }

    private static async Task<string> ReadAndTrackOutputAsync(StreamReader reader, Action<string> onLine)
    {
        var builder = new StringBuilder();
        var buffer = new char[1];
        var lineBuilder = new StringBuilder();

        while (await reader.ReadAsync(buffer, 0, 1) > 0) {
            char current = buffer[0];
            builder.Append(current);

            if (current is '\r' or '\n') {
                if (lineBuilder.Length > 0) {
                    string line = lineBuilder.ToString();
                    onLine(line);
                    lineBuilder.Clear();
                }

                continue;
            }

            lineBuilder.Append(current);
        }

        if (lineBuilder.Length > 0) {
            string line = lineBuilder.ToString();
            onLine(line);
        }

        return builder.ToString().Replace("\r", Environment.NewLine);
    }

    private static bool TryParsePlannedFileLine(string line, string sourceRootPath, out string filePath, out long bytes)
    {
        return TryParseFileLine(line, sourceRootPath, out filePath, out bytes);
    }

    private static bool TryParseFileLine(string line, string sourceRootPath, out string filePath, out long bytes)
    {
        filePath = string.Empty;
        bytes = 0;

        int pathIndex = line.IndexOf(sourceRootPath, StringComparison.OrdinalIgnoreCase);
        if (pathIndex < 0) {
            return false;
        }

        filePath = line[pathIndex..].Trim();
        string prefix = line[..pathIndex];
        Match match = SizeRegex.Match(prefix);
        if (!match.Success) {
            return false;
        }

        string digitsOnly = new(match.Groups[1].Value.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digitsOnly) || !long.TryParse(digitsOnly, out bytes)) {
            return false;
        }

        return true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1) {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string GetDestinationFolderName(string sourcePath)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        string name = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(name)) {
            return name;
        }

        return normalized
            .Replace(':', '_')
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static string? FindDuplicateDestinationFolderName(IEnumerable<string> sourcePaths)
    {
        return sourcePaths
            .GroupBy(GetDestinationFolderName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
    }

    private static SyncResult BuildAggregateResult(
        string logFilePath,
        string resolvedDestinationPath,
        int totalSourceCount,
        int successCount,
        int warningCount,
        int failureCount,
        int highestExitCode)
    {
        DateTimeOffset finishedAt = DateTimeOffset.Now;

        if (successCount == 0) {
            string failureMessage = $"No source folders were synced. {failureCount} failed. See log: {logFilePath}";
            AppendLogLine(logFilePath, failureMessage);
            return new SyncResult {
                Success = false,
                Warning = false,
                ExitCode = highestExitCode == 0 ? 1 : highestExitCode,
                Status = "failure",
                Message = failureMessage,
                LogFilePath = logFilePath,
                ResolvedDestinationPath = resolvedDestinationPath,
                FinishedAt = finishedAt
            };
        }

        bool hasWarnings = warningCount > 0 || failureCount > 0;
        string message = hasWarnings
            ? $"Synced {successCount} of {totalSourceCount} source folders with warnings. Destination: {resolvedDestinationPath}"
            : $"Synced {successCount} source folders successfully. Destination: {resolvedDestinationPath}";
        AppendLogLine(logFilePath, message);

        return new SyncResult {
            Success = true,
            Warning = hasWarnings,
            ExitCode = highestExitCode,
            Status = hasWarnings ? "warning" : "success",
            Message = message,
            LogFilePath = logFilePath,
            ResolvedDestinationPath = resolvedDestinationPath,
            FinishedAt = finishedAt
        };
    }

    private sealed record PlannedCopyEntry(string SourceRootPath, string SourceFilePath, long Bytes);

    private sealed class RobocopyProgressTracker
    {
        private readonly string _sourceRootPath;
        private readonly IReadOnlyDictionary<string, long> _plannedFiles;
        private readonly long _totalBytesToCopy;
        private readonly long _completedBytesBeforeSource;
        private readonly int _totalSourceCount;
        private readonly int _completedSourceCount;
        private readonly DateTimeOffset _startedAt;
        private readonly IProgress<SyncProgress>? _progress;
        private readonly HashSet<string> _completedFiles = new(StringComparer.OrdinalIgnoreCase);

        private string _currentFilePath = string.Empty;
        private long _currentFileBytes;
        private int _currentPercent;
        private long _completedBytesInSource;

        public RobocopyProgressTracker(
            string sourceRootPath,
            IReadOnlyDictionary<string, long> plannedFiles,
            long totalBytesToCopy,
            long completedBytesBeforeSource,
            int totalSourceCount,
            int completedSourceCount,
            DateTimeOffset startedAt,
            IProgress<SyncProgress>? progress)
        {
            _sourceRootPath = sourceRootPath;
            _plannedFiles = plannedFiles;
            _totalBytesToCopy = totalBytesToCopy;
            _completedBytesBeforeSource = completedBytesBeforeSource;
            _totalSourceCount = totalSourceCount;
            _completedSourceCount = completedSourceCount;
            _startedAt = startedAt;
            _progress = progress;
        }

        public void ProcessLine(string line)
        {
            if (_progress is null) {
                return;
            }

            if (TryParseFileLine(line, _sourceRootPath, out string filePath, out long bytes)) {
                FinalizeCurrentFileIfNeeded();
                _currentFilePath = filePath;
                _currentFileBytes = _plannedFiles.TryGetValue(filePath, out long plannedBytes)
                    ? plannedBytes
                    : bytes;
                _currentPercent = 0;
                Report($"Copying {Path.GetFileName(filePath)}");
                return;
            }

            Match match = ProgressRegex.Match(line);
            if (!match.Success) {
                return;
            }

            _currentPercent = Math.Clamp(int.Parse(match.Groups[1].Value), 0, 100);
            if (_currentPercent >= 100) {
                FinalizeCurrentFileIfNeeded(forceComplete: true);
                return;
            }

            Report($"Copying {Path.GetFileName(_currentFilePath)}");
        }

        public void MarkSourceFinished()
        {
            FinalizeCurrentFileIfNeeded(forceComplete: true);
        }

        private void FinalizeCurrentFileIfNeeded(bool forceComplete = false)
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath) || _completedFiles.Contains(_currentFilePath)) {
                if (forceComplete) {
                    _currentPercent = 100;
                }
                return;
            }

            if (!forceComplete && _currentPercent <= 0) {
                return;
            }

            _completedFiles.Add(_currentFilePath);
            _completedBytesInSource += _currentFileBytes;
            _currentPercent = 100;
            Report($"Finished {Path.GetFileName(_currentFilePath)}");
            _currentFilePath = string.Empty;
            _currentFileBytes = 0;
            _currentPercent = 0;
        }

        private void Report(string status)
        {
            long currentFileCopiedBytes = _currentFileBytes > 0
                ? (long)Math.Round(_currentFileBytes * (_currentPercent / 100.0))
                : 0;
            long completedBytes = Math.Min(
                _completedBytesBeforeSource + _completedBytesInSource + currentFileCopiedBytes,
                _totalBytesToCopy);

            _progress?.Report(new SyncProgress {
                IsIndeterminate = _totalBytesToCopy == 0,
                TotalBytesToCopy = _totalBytesToCopy,
                CompletedBytes = completedBytes,
                CurrentFileBytes = _currentFileBytes,
                CurrentFilePercent = _currentPercent,
                CurrentSourcePath = _sourceRootPath,
                CurrentFilePath = _currentFilePath,
                TotalSourceCount = _totalSourceCount,
                CompletedSourceCount = _completedSourceCount,
                StatusText = status,
                StartedAt = _startedAt
            });
        }
    }
}
