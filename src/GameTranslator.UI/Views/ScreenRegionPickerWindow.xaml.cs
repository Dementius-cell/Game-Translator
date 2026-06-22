using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameTranslator.UI.Services;

namespace GameTranslator.UI.Views;

public partial class ScreenRegionPickerWindow : Window
{
    private const double MinimumSelectionSize = 4;
    private Point selectionStart;
    private bool isSelecting;

    public ScreenRegionPickerWindow()
    {
        InitializeComponent();
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Loaded += OnLoaded;
    }

    public ScreenRegionSelectionResult? SelectedRegion { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        selectionStart = e.GetPosition(SelectionCanvas);
        isSelecting = true;
        SelectionBorder.Visibility = Visibility.Visible;
        UpdateSelection(selectionStart, selectionStart);
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!isSelecting)
        {
            return;
        }

        UpdateSelection(selectionStart, e.GetPosition(SelectionCanvas));
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isSelecting)
        {
            return;
        }

        isSelecting = false;
        Mouse.Capture(null);

        var selection = CreateDipSelection(selectionStart, e.GetPosition(SelectionCanvas));
        if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        SelectedRegion = CreateResult(selection);
        DialogResult = true;
        Close();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        DialogResult = false;
        Close();
        e.Handled = true;
    }

    private void UpdateSelection(Point start, Point end)
    {
        var selection = CreateDipSelection(start, end);
        Canvas.SetLeft(SelectionBorder, selection.X);
        Canvas.SetTop(SelectionBorder, selection.Y);
        SelectionBorder.Width = selection.Width;
        SelectionBorder.Height = selection.Height;
    }

    private static Rect CreateDipSelection(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);

        return new Rect(left, top, right - left, bottom - top);
    }

    private ScreenRegionSelectionResult CreateResult(Rect selection)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var referenceWidth = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX, MidpointRounding.AwayFromZero));
        var referenceHeight = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY, MidpointRounding.AwayFromZero));
        var left = ClampToRange(
            (int)Math.Round(selection.X * dpi.DpiScaleX, MidpointRounding.AwayFromZero),
            0,
            referenceWidth - 1);
        var top = ClampToRange(
            (int)Math.Round(selection.Y * dpi.DpiScaleY, MidpointRounding.AwayFromZero),
            0,
            referenceHeight - 1);
        var right = ClampToRange(
            (int)Math.Round((selection.X + selection.Width) * dpi.DpiScaleX, MidpointRounding.AwayFromZero),
            left + 1,
            referenceWidth);
        var bottom = ClampToRange(
            (int)Math.Round((selection.Y + selection.Height) * dpi.DpiScaleY, MidpointRounding.AwayFromZero),
            top + 1,
            referenceHeight);

        return new ScreenRegionSelectionResult(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top),
            referenceWidth,
            referenceHeight);
    }

    private static int ClampToRange(int value, int minimum, int maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }
}
