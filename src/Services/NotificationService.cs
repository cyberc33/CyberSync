using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CyberSync.Services;

public sealed class NotificationService
{
    private readonly bool _registered;

    public NotificationService()
    {
        try {
            if (!AppNotificationManager.IsSupported()) {
                _registered = false;
                return;
            }

            AppNotificationManager.Default.Register();
            _registered = true;
        } catch {
            _registered = false;
        }
    }

    public static NotificationService CreateSafe()
    {
        try {
            return new NotificationService();
        } catch {
            return new NotificationService(false);
        }
    }

    public void ShowInfo(string title, string message)
    {
        ShowMessage(title, message);
    }

    public void ShowWarning(string title, string message)
    {
        ShowMessage(title, message);
    }

    public void ShowError(string title, string message)
    {
        ShowMessage(title, message);
    }

    public void ShowSyncResult(SyncResult result)
    {
        if (!result.Success) {
            ShowError("CyberSync failed", result.Message);
            return;
        }

        if (result.Warning) {
            ShowWarning("CyberSync warning", result.Message);
            return;
        }

        ShowInfo("CyberSync finished", result.Message);
    }

    private void ShowMessage(string title, string message)
    {
        if (!_registered) {
            return;
        }

        try {
            AppNotification notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        } catch {
        }
    }

    private NotificationService(bool registered)
    {
        _registered = registered;
    }
}
