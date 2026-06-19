using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;

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
    private const double PreviewPadding = 2;
    private const double MinReadableItemWidth = 40;
    private const double MinReadableItemHeight = 16;
    private static readonly nint HtTransparent = new(-1);

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowSnapshot(OverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        if (!IsVisible)
        {
            Show();
        }

        DataContext = CreateViewModel(snapshot);
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

    private OverlayWindowSnapshotViewModel CreateViewModel(OverlaySnapshot snapshot)
    {
        var transformFromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;

        return new OverlayWindowSnapshotViewModel(
            snapshot.MaskItems.Select(item => OverlayWindowMaskItemViewModel.FromDevicePixels(item, transformFromDevice)),
            snapshot.TextItems.Select(item => OverlayWindowTextItemViewModel.FromDevicePixels(item, transformFromDevice)));
    }

    private sealed class OverlayWindowSnapshotViewModel
    {
        public OverlayWindowSnapshotViewModel(
            IEnumerable<OverlayWindowMaskItemViewModel> maskItems,
            IEnumerable<OverlayWindowTextItemViewModel> textItems)
        {
            MaskItems = maskItems.ToArray();
            TextItems = textItems.ToArray();
        }

        public IReadOnlyList<OverlayWindowMaskItemViewModel> MaskItems { get; }

        public IReadOnlyList<OverlayWindowTextItemViewModel> TextItems { get; }
    }

    private sealed class OverlayWindowMaskItemViewModel
    {
        private OverlayWindowMaskItemViewModel(
            double x,
            double y,
            double width,
            double height,
            string mode,
            Brush brush,
            double opacity)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Mode = mode;
            Brush = brush;
            Opacity = opacity;
        }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public string Mode { get; }

        public Brush Brush { get; }

        public double Opacity { get; }

        public static OverlayWindowMaskItemViewModel FromDevicePixels(
            OverlayMaskItem item,
            Matrix transformFromDevice)
        {
            var topLeft = transformFromDevice.Transform(new Point(item.X, item.Y));
            var bottomRight = transformFromDevice.Transform(new Point(item.X + item.Width, item.Y + item.Height));

            return new OverlayWindowMaskItemViewModel(
                Math.Max(0, topLeft.X),
                Math.Max(0, topLeft.Y),
                Math.Max(1, bottomRight.X - topLeft.X),
                Math.Max(1, bottomRight.Y - topLeft.Y),
                item.Mode.ToString(),
                CreateMaskBrush(item),
                item.Opacity);
        }

        private static Brush CreateMaskBrush(OverlayMaskItem item)
        {
            var color = (Color)ColorConverter.ConvertFromString(item.Color);

            var brush = new SolidColorBrush(color);
            brush.Freeze();

            return brush;
        }
    }

    private sealed class OverlayWindowTextItemViewModel
    {
        private OverlayWindowTextItemViewModel(string text, double x, double y, double width, double height)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public string Text { get; }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public static OverlayWindowTextItemViewModel FromDevicePixels(
            OverlayTextItem item,
            Matrix transformFromDevice)
        {
            var topLeft = transformFromDevice.Transform(new Point(item.X, item.Y));
            var bottomRight = transformFromDevice.Transform(new Point(item.X + item.Width, item.Y + item.Height));
            var rawWidth = Math.Max(1, bottomRight.X - topLeft.X);
            var rawHeight = Math.Max(1, bottomRight.Y - topLeft.Y);
            var width = Math.Max(MinReadableItemWidth, rawWidth + PreviewPadding * 2);
            var height = Math.Max(MinReadableItemHeight, rawHeight + PreviewPadding * 2);

            return new OverlayWindowTextItemViewModel(
                item.Text,
                Math.Max(0, topLeft.X - (width - rawWidth) / 2),
                Math.Max(0, topLeft.Y - (height - rawHeight) / 2),
                width,
                height);
        }
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
