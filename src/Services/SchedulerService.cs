using System.Diagnostics;

namespace CyberSync.Services;

public sealed class SchedulerService
{
    public static string TaskName() => "CyberSync Daily";

    public async Task<CommandResult> CreateOrUpdateDailyTaskAsync(TimeOnly time)
    {
        string executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not resolve the executable path.");
        string taskAction = $"\"{executablePath}\" --run-scheduled";

        string[] arguments = [
            "/Create",
            "/SC", "DAILY",
            "/TN", TaskName(),
            "/TR", taskAction,
            "/ST", time.ToString("HH:mm"),
            "/F"
        ];

        CommandResult result = await RunSchtasksAsync(arguments);
        return new CommandResult {
            Success = result.Success,
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr,
            Message = result.Success
                ? $"Scheduled daily sync at {time.ToString("HH:mm")}."
                : "Could not create or update the scheduled task."
        };
    }

    public async Task<CommandResult> QueryTaskAsync()
    {
        CommandResult result = await RunSchtasksAsync(["/Query", "/TN", TaskName()]);
        return new CommandResult {
            Success = result.Success,
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = result.StdErr,
            Message = result.Success
                ? "Scheduled task exists."
                : "Scheduled task is not registered."
        };
    }

    private static async Task<CommandResult> RunSchtasksAsync(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }

        try {
            using var process = Process.Start(startInfo);
            if (process is null) {
                return new CommandResult {
                    Message = "Could not start schtasks."
                };
            }

            string stdOut = await process.StandardOutput.ReadToEndAsync();
            string stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            bool success = process.ExitCode == 0;
            return new CommandResult {
                Success = success,
                ExitCode = process.ExitCode,
                StdOut = stdOut.Trim(),
                StdErr = stdErr.Trim(),
                Message = success
                    ? "Scheduled task command completed."
                    : "Scheduled task command failed."
            };
        } catch (Exception ex) {
            return new CommandResult {
                Success = false,
                Message = "Could not start schtasks.",
                StdErr = ex.Message
            };
        }
    }
}
