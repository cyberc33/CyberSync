using CyberSync.Services;

namespace CyberSync;

public sealed class AppRuntime
{
    private NotificationService? _notificationService;

    public ConfigManager ConfigManager { get; } = new();
    public SchedulerService SchedulerService { get; } = new();
    public SyncService SyncService { get; } = new();
    public NotificationService NotificationService => _notificationService ??= NotificationService.CreateSafe();

    public async Task<int> RunScheduledAsync()
    {
        AppConfig config = ConfigManager.Load();
        SyncResult result = await SyncService.RunSyncAsync(config);

        ConfigManager.UpdateLastRun(result.Status, result.FinishedAt, out _);
        NotificationService.ShowSyncResult(result);
        return result.Success ? (result.Warning ? 2 : 0) : 1;
    }
}
