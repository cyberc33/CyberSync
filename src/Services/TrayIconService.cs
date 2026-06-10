using System.Runtime.InteropServices;

namespace CyberSync.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x44;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIF_SHOWTIP = 0x00000080;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const int IconId = 1001;
    private const uint MenuItemOpen = 2001;
    private const uint MenuItemSyncNow = 2002;
    private const uint MenuItemExit = 2003;
    private const int IDI_APPLICATION = 32512;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_NULL = 0x0000;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;

    private static readonly SubclassProcDelegate SubclassProcInstance = SubclassProc;

    private readonly IntPtr _windowHandle;
    private readonly GCHandle _selfHandle;
    private bool _iconAdded;
    private bool _disposed;

    public event EventHandler? Activated;
    public event EventHandler? SyncNowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _selfHandle = GCHandle.Alloc(this);

        if (!SetWindowSubclass(
                _windowHandle,
                SubclassProcInstance,
                (UIntPtr)IconId,
                (UIntPtr)GCHandle.ToIntPtr(_selfHandle).ToInt64())) {
            throw new InvalidOperationException("Could not attach the tray icon window subclass.");
        }
    }

    public void ShowIcon()
    {
        if (_iconAdded) {
            return;
        }

        var data = CreateBaseData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;

        if (!Shell_NotifyIcon(NIM_ADD, ref data)) {
            throw new InvalidOperationException("Could not create the notification area icon.");
        }

        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
        _iconAdded = true;
    }

    public void HideIcon()
    {
        if (!_iconAdded) {
            return;
        }

        var data = CreateBaseData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _iconAdded = false;
    }

    public void ShowBackgroundHint()
    {
        if (!_iconAdded) {
            return;
        }

        var data = CreateBaseData();
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = NIIF_INFO;
        data.szInfoTitle = "CyberSync is still running";
        data.szInfo = "CyberSync was moved to the notification area. Click the icon to reopen it.";
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        HideIcon();
        RemoveWindowSubclass(_windowHandle, SubclassProcInstance, (UIntPtr)IconId);
        if (_selfHandle.IsAllocated) {
            _selfHandle.Free();
        }

        _disposed = true;
    }

    private NOTIFYICONDATA CreateBaseData()
    {
        return new NOTIFYICONDATA {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = IconId,
            uCallbackMessage = CallbackMessage,
            hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION),
            szTip = "CyberSync",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static IntPtr SubclassProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr uIdSubclass,
        UIntPtr dwRefData)
    {
        if (msg == CallbackMessage) {
            var handle = GCHandle.FromIntPtr(new IntPtr(unchecked((long)dwRefData)));
            if (handle.Target is TrayIconService service) {
                int notification = unchecked((short)lParam.ToInt64());
                if (notification is WM_LBUTTONUP or WM_LBUTTONDBLCLK) {
                    service.Activated?.Invoke(service, EventArgs.Empty);
                    return IntPtr.Zero;
                }

                if (notification == WM_RBUTTONUP) {
                    service.ShowContextMenu();
                    return IntPtr.Zero;
                }
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) {
            return;
        }

        try {
            AppendMenu(menu, MF_STRING, (UIntPtr)MenuItemOpen, "Open CyberSync");
            AppendMenu(menu, MF_STRING, (UIntPtr)MenuItemSyncNow, "Sync now");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)MenuItemExit, "Exit app");

            GetCursorPos(out POINT point);
            SetForegroundWindow(_windowHandle);
            uint command = TrackPopupMenu(
                menu,
                TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD | TPM_RIGHTBUTTON,
                point.X,
                point.Y,
                0,
                _windowHandle,
                IntPtr.Zero);
            PostMessage(_windowHandle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command) {
                case MenuItemOpen:
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuItemSyncNow:
                    SyncNowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuItemExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        } finally {
            DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr SubclassProcDelegate(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr uIdSubclass,
        UIntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProcDelegate pfnSubclass,
        UIntPtr uIdSubclass,
        UIntPtr dwRefData);

    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProcDelegate pfnSubclass,
        UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu")]
    private static extern uint TrackPopupMenu(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        IntPtr hWnd,
        IntPtr prcRect);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
