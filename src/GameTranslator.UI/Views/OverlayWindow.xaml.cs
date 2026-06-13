using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GameTranslator.Application.Overlay;

namespace GameTranslator.UI.Views;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoOwnerZOrder = 0x0200;
    private const int WmNcHitTest = 0x0084;
    private static readonly nint HtTransparent = new(-1);

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowSnapshot(OverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        DataContext = snapshot;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        if (!IsVisible)
        {
            Show();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(OnWindowMessage);
        ApplyClickThroughStyles(handle);
    }

    private static nint OnWindowMessage(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != WmNcHitTest)
        {
            return nint.Zero;
        }

        handled = true;
        return HtTransparent;
    }

    private static void ApplyClickThroughStyles(nint handle)
    {
        var currentStyles = GetWindowLongPtr(handle, GwlExStyle);
        var overlayStyles = currentStyles
            | (nint)WsExTransparent
            | (nint)WsExToolWindow
            | (nint)WsExLayered
            | (nint)WsExNoActivate;

        SetWindowLongPtr(handle, GwlExStyle, overlayStyles);
        SetWindowPos(
            handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpNoOwnerZOrder);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);
}
