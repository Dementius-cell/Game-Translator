using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Views;

public partial class ShellView : UserControl
{
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
}
