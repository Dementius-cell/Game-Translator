using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Views;

public partial class ShellView : UserControl
{
    private const double WelcomeTourSpotlightPadding = 8;

    private enum ZoneSurfaceInteractionMode
    {
        None,
        Selecting,
        Moving,
        Resizing,
    }

    private readonly ShellViewModel viewModel;
    private ZoneSurfaceInteractionMode zoneSurfaceInteractionMode;
    private UIElement? activeZoneSurfaceElement;
    private string? welcomeTourSpotlightTargetName;
    private Rect welcomeTourSpotlightBounds = Rect.Empty;
    private Size welcomeTourOverlaySize = Size.Empty;
    private bool isUpdatingWelcomeTourSpotlight;
    private FrameworkElement? welcomeTourSpotlightTarget;
    private Grid? welcomeTourOverlay;
    private Path? welcomeTourDimmingPath;
    private Border? welcomeTourSpotlightBorder;
    private Border? welcomeTourCard;

    private Grid WelcomeTourOverlay =>
        welcomeTourOverlay ??= FindVisualDescendantByName<Grid>(this, "WelcomeTourOverlay")
            ?? throw new InvalidOperationException("Welcome tour overlay was not found in the visual tree.");

    private Path WelcomeTourDimmingPath =>
        welcomeTourDimmingPath ??= FindVisualDescendantByName<Path>(WelcomeTourOverlay, "WelcomeTourDimmingPath")
            ?? throw new InvalidOperationException("Welcome tour dimming path was not found in the visual tree.");

    private Border WelcomeTourSpotlightBorder =>
        welcomeTourSpotlightBorder ??= FindVisualDescendantByName<Border>(WelcomeTourOverlay, "WelcomeTourSpotlightBorder")
            ?? throw new InvalidOperationException("Welcome tour spotlight border was not found in the visual tree.");

    private Border WelcomeTourCard =>
        welcomeTourCard ??= FindVisualDescendantByName<Border>(WelcomeTourOverlay, "WelcomeTourCard")
            ?? throw new InvalidOperationException("Welcome tour card was not found in the visual tree.");

    public ShellView(ShellViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.InitializeAsync();
    }

    private void OnZoneSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel)
        {
            return;
        }

        zoneSurfaceInteractionMode = ZoneSurfaceInteractionMode.Selecting;
        activeZoneSurfaceElement = element;
        element.CaptureMouse();

        var position = e.GetPosition(element);
        mainViewModel.StartZoneSelection(position.X, position.Y);
        e.Handled = true;
    }

    private void OnZoneSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement element || activeZoneSurfaceElement is null || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel)
        {
            return;
        }

        var position = e.GetPosition(element);
        switch (zoneSurfaceInteractionMode)
        {
            case ZoneSurfaceInteractionMode.Selecting:
                mainViewModel.UpdateZoneSelection(position.X, position.Y);
                e.Handled = true;
                break;
            case ZoneSurfaceInteractionMode.Resizing:
                mainViewModel.UpdateSelectedZoneResize(position.X, position.Y);
                e.Handled = true;
                break;
            case ZoneSurfaceInteractionMode.Moving:
                mainViewModel.UpdateSelectedZoneMove(position.X, position.Y);
                e.Handled = true;
                break;
        }
    }

    private void OnZoneSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || activeZoneSurfaceElement is null || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel)
        {
            return;
        }

        var position = e.GetPosition(element);
        switch (zoneSurfaceInteractionMode)
        {
            case ZoneSurfaceInteractionMode.Selecting:
                mainViewModel.CompleteZoneSelection(position.X, position.Y);
                break;
            case ZoneSurfaceInteractionMode.Resizing:
                mainViewModel.CompleteSelectedZoneResize(position.X, position.Y);
                break;
            case ZoneSurfaceInteractionMode.Moving:
                mainViewModel.CompleteSelectedZoneMove(position.X, position.Y);
                break;
        }

        activeZoneSurfaceElement.ReleaseMouseCapture();
        activeZoneSurfaceElement = null;
        zoneSurfaceInteractionMode = ZoneSurfaceInteractionMode.None;
        e.Handled = true;
    }

    private void OnZoneSurfaceZoneMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel)
        {
            return;
        }

        if (element.Tag is string zoneId)
        {
            mainViewModel.SelectZone(zoneId);
        }

        var surfaceElement = FindTopmostVisualParent<Canvas>(element);
        if (surfaceElement is null)
        {
            return;
        }

        var position = e.GetPosition(surfaceElement);
        activeZoneSurfaceElement = surfaceElement;
        zoneSurfaceInteractionMode = ZoneSurfaceInteractionMode.Moving;
        surfaceElement.CaptureMouse();
        mainViewModel.StartSelectedZoneMove(position.X, position.Y);
        e.Handled = true;
    }

    private void OnProfileItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2
            || sender is not FrameworkElement element
            || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel
            || element.Tag is not string profileId)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject originalSource
            && FindVisualParent<TextBox>(originalSource) is not null)
        {
            return;
        }

        mainViewModel.BeginProfileRename(profileId);
        element.Dispatcher.BeginInvoke(
            () =>
            {
                var editor = FindVisualDescendantByName<TextBox>(element, "ProfileRenameTextBox");
                if (editor is not null)
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            },
            DispatcherPriority.Input);
        e.Handled = true;
    }

    private async void OnProfileRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await mainViewModel.CommitProfileRenameAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            mainViewModel.CancelProfileRename();
        }
    }

    private async void OnProfileRenameLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (viewModel.Navigation.CurrentViewModel is MainViewModel mainViewModel
            && mainViewModel.IsProfileRenameActive)
        {
            await mainViewModel.CommitProfileRenameAsync();
        }
    }

    private void OnWelcomeTourOverlayIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        welcomeTourOverlay = sender as Grid;
        if (WelcomeTourOverlay.Visibility != Visibility.Visible)
        {
            ResetWelcomeTourSpotlight();
            return;
        }

        WelcomeTourOverlay.Dispatcher.BeginInvoke(UpdateWelcomeTourSpotlight, DispatcherPriority.Loaded);
    }

    private void OnWelcomeTourOverlayLayoutUpdated(object? sender, EventArgs e)
    {
        welcomeTourOverlay ??= sender as Grid;
        UpdateWelcomeTourSpotlight();
    }

    private void UpdateWelcomeTourSpotlight()
    {
        if (isUpdatingWelcomeTourSpotlight
            || WelcomeTourOverlay.Visibility != Visibility.Visible
            || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel
            || WelcomeTourOverlay.ActualWidth <= 0
            || WelcomeTourOverlay.ActualHeight <= 0)
        {
            return;
        }

        isUpdatingWelcomeTourSpotlight = true;
        try
        {
            var targetName = mainViewModel.WelcomeTourTargetElementName;
            var target = string.Equals(welcomeTourSpotlightTargetName, targetName, StringComparison.Ordinal)
                ? welcomeTourSpotlightTarget
                : FindVisualDescendantByName<FrameworkElement>(this, targetName);
            if (target is null || target.Visibility != Visibility.Visible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
            {
                ShowWelcomeTourDimmingWithoutSpotlight();
                return;
            }

            if (!string.Equals(welcomeTourSpotlightTargetName, targetName, StringComparison.Ordinal))
            {
                welcomeTourSpotlightTargetName = targetName;
                welcomeTourSpotlightTarget = target;
                target.BringIntoView();
                WelcomeTourOverlay.Dispatcher.BeginInvoke(UpdateWelcomeTourSpotlight, DispatcherPriority.Loaded);
            }

            var overlayBounds = new Rect(0, 0, WelcomeTourOverlay.ActualWidth, WelcomeTourOverlay.ActualHeight);
            var targetBounds = target.TransformToVisual(WelcomeTourOverlay).TransformBounds(
                new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            targetBounds.Intersect(overlayBounds);
            if (targetBounds.IsEmpty || targetBounds.Width <= 0 || targetBounds.Height <= 0)
            {
                ShowWelcomeTourDimmingWithoutSpotlight();
                return;
            }

            var spotlightBounds = ExpandAndClamp(targetBounds, WelcomeTourSpotlightPadding, overlayBounds);
            ApplyWelcomeTourSpotlight(spotlightBounds, overlayBounds);
            PositionWelcomeTourCard(spotlightBounds, overlayBounds);
        }
        catch (InvalidOperationException)
        {
            welcomeTourSpotlightTargetName = null;
            welcomeTourSpotlightTarget = null;
            ShowWelcomeTourDimmingWithoutSpotlight();
        }
        finally
        {
            isUpdatingWelcomeTourSpotlight = false;
        }
    }

    private void ApplyWelcomeTourSpotlight(Rect spotlightBounds, Rect overlayBounds)
    {
        if (welcomeTourSpotlightBounds != spotlightBounds || welcomeTourOverlaySize != overlayBounds.Size)
        {
            var dimmingGeometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(overlayBounds),
                new RectangleGeometry(spotlightBounds, 10, 10));
            if (dimmingGeometry.CanFreeze)
            {
                dimmingGeometry.Freeze();
            }

            WelcomeTourDimmingPath.Data = dimmingGeometry;
            WelcomeTourSpotlightBorder.Width = spotlightBounds.Width;
            WelcomeTourSpotlightBorder.Height = spotlightBounds.Height;
            WelcomeTourSpotlightBorder.Margin = new Thickness(spotlightBounds.Left, spotlightBounds.Top, 0, 0);
            welcomeTourSpotlightBounds = spotlightBounds;
            welcomeTourOverlaySize = overlayBounds.Size;
        }

        WelcomeTourSpotlightBorder.Visibility = Visibility.Visible;
    }

    private void ShowWelcomeTourDimmingWithoutSpotlight()
    {
        var overlayBounds = new Rect(0, 0, WelcomeTourOverlay.ActualWidth, WelcomeTourOverlay.ActualHeight);
        if (overlayBounds.Width > 0 && overlayBounds.Height > 0)
        {
            WelcomeTourDimmingPath.Data = new RectangleGeometry(overlayBounds);
        }

        WelcomeTourSpotlightBorder.Visibility = Visibility.Collapsed;
        welcomeTourSpotlightBounds = Rect.Empty;
        welcomeTourOverlaySize = overlayBounds.Size;
        WelcomeTourCard.HorizontalAlignment = HorizontalAlignment.Center;
        WelcomeTourCard.VerticalAlignment = VerticalAlignment.Center;
    }

    private void ResetWelcomeTourSpotlight()
    {
        if (welcomeTourDimmingPath is not null)
        {
            welcomeTourDimmingPath.Data = null;
        }

        if (welcomeTourSpotlightBorder is not null)
        {
            welcomeTourSpotlightBorder.Visibility = Visibility.Collapsed;
        }

        if (welcomeTourCard is not null)
        {
            welcomeTourCard.HorizontalAlignment = HorizontalAlignment.Center;
            welcomeTourCard.VerticalAlignment = VerticalAlignment.Center;
        }

        welcomeTourSpotlightTargetName = null;
        welcomeTourSpotlightTarget = null;
        welcomeTourSpotlightBounds = Rect.Empty;
        welcomeTourOverlaySize = Size.Empty;
    }

    private void PositionWelcomeTourCard(Rect spotlightBounds, Rect overlayBounds)
    {
        var cardWidth = WelcomeTourCard.ActualWidth > 0 ? WelcomeTourCard.ActualWidth : WelcomeTourCard.Width;
        var cardHeight = WelcomeTourCard.ActualHeight > 0 ? WelcomeTourCard.ActualHeight : 360;
        const double outerMargin = 24;
        var left = outerMargin;
        var right = Math.Max(outerMargin, overlayBounds.Width - outerMargin - cardWidth);
        var top = outerMargin;
        var bottom = Math.Max(outerMargin, overlayBounds.Height - outerMargin - cardHeight);
        var placements = new[]
        {
            new WelcomeTourCardPlacement(HorizontalAlignment.Left, VerticalAlignment.Top, new Rect(left, top, cardWidth, cardHeight)),
            new WelcomeTourCardPlacement(HorizontalAlignment.Right, VerticalAlignment.Top, new Rect(right, top, cardWidth, cardHeight)),
            new WelcomeTourCardPlacement(HorizontalAlignment.Left, VerticalAlignment.Bottom, new Rect(left, bottom, cardWidth, cardHeight)),
            new WelcomeTourCardPlacement(HorizontalAlignment.Right, VerticalAlignment.Bottom, new Rect(right, bottom, cardWidth, cardHeight)),
        };
        var spotlightCenter = new Point(
            spotlightBounds.Left + (spotlightBounds.Width / 2),
            spotlightBounds.Top + (spotlightBounds.Height / 2));
        var bestPlacement = placements
            .OrderBy(placement => IntersectionArea(placement.Bounds, spotlightBounds))
            .ThenByDescending(placement => SquaredDistance(placement.Bounds, spotlightCenter))
            .First();

        WelcomeTourCard.HorizontalAlignment = bestPlacement.HorizontalAlignment;
        WelcomeTourCard.VerticalAlignment = bestPlacement.VerticalAlignment;
    }

    private static Rect ExpandAndClamp(Rect bounds, double padding, Rect limit)
    {
        var left = Math.Max(limit.Left, bounds.Left - padding);
        var top = Math.Max(limit.Top, bounds.Top - padding);
        var right = Math.Min(limit.Right, bounds.Right + padding);
        var bottom = Math.Min(limit.Bottom, bounds.Bottom + padding);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static double IntersectionArea(Rect first, Rect second)
    {
        var intersection = Rect.Intersect(first, second);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }

    private static double SquaredDistance(Rect bounds, Point point)
    {
        var horizontal = bounds.Left + (bounds.Width / 2) - point.X;
        var vertical = bounds.Top + (bounds.Height / 2) - point.Y;
        return (horizontal * horizontal) + (vertical * vertical);
    }

    private void OnZoneResizeHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || viewModel.Navigation.CurrentViewModel is not MainViewModel mainViewModel)
        {
            return;
        }

        if (element.Tag is string zoneId)
        {
            mainViewModel.SelectZone(zoneId);
        }

        var surfaceElement = FindTopmostVisualParent<Canvas>(element);
        if (surfaceElement is null)
        {
            return;
        }

        activeZoneSurfaceElement = surfaceElement;
        zoneSurfaceInteractionMode = ZoneSurfaceInteractionMode.Resizing;
        surfaceElement.CaptureMouse();
        mainViewModel.StartSelectedZoneResize();
        e.Handled = true;
    }

    private static TElement? FindVisualParent<TElement>(DependencyObject? child)
        where TElement : DependencyObject
    {
        var current = child;

        while (current is not null)
        {
            if (current is TElement match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static TElement? FindTopmostVisualParent<TElement>(DependencyObject? child)
        where TElement : DependencyObject
    {
        TElement? lastMatch = null;
        var current = child;

        while (current is not null)
        {
            if (current is TElement match)
            {
                lastMatch = match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return lastMatch;
    }

    private static TElement? FindVisualDescendantByName<TElement>(DependencyObject parent, string name)
        where TElement : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TElement match && string.Equals(match.Name, name, StringComparison.Ordinal))
            {
                return match;
            }

            var descendant = FindVisualDescendantByName<TElement>(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private sealed record WelcomeTourCardPlacement(
        HorizontalAlignment HorizontalAlignment,
        VerticalAlignment VerticalAlignment,
        Rect Bounds);
}
