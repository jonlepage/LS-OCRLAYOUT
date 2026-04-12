using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ScreenSearchOverlay;

public partial class MainWindow : Window
{
    private void ApplySearchBarPosition()
    {
        var app = (App)System.Windows.Application.Current;
        var x = app.SearchBarX;
        var y = app.SearchBarY;

        // Default: center top
        if (x < 0 || y < 0)
        {
            SearchBarBorder.UpdateLayout();
            x = (ActualWidth - SearchBarBorder.ActualWidth) / 2;
            y = 30;
        }

        // Clamp to screen bounds using actual bar size
        SearchBarBorder.UpdateLayout();
        var scale = (SearchBarBorder.LayoutTransform as ScaleTransform)?.ScaleX ?? 1.0;
        var barW = SearchBarBorder.ActualWidth * scale;
        var barH = SearchBarBorder.ActualHeight * scale;
        x = Math.Max(0, Math.Min(x, ActualWidth - barW));
        y = Math.Max(0, Math.Min(y, ActualHeight - barH));

        SearchBarBorder.Margin = new Thickness(x, y, 0, 0);
    }

    private void SearchBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Don't drag if clicking on interactive elements
        if (e.OriginalSource is System.Windows.Controls.TextBox
            or System.Windows.Controls.Primitives.ButtonBase)
            return;

        _isDragging = true;
        _dragOffset = e.GetPosition(SearchBarBorder);
        SearchBarBorder.CaptureMouse();
        e.Handled = true;
    }

    private void SearchBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        SearchBarBorder.ReleaseMouseCapture();

        // Save position
        var app = (App)System.Windows.Application.Current;
        app.SearchBarX = SearchBarBorder.Margin.Left;
        app.SearchBarY = SearchBarBorder.Margin.Top;
        app.SaveSettings();
        e.Handled = true;
    }

    private void SearchBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;

        var pos = e.GetPosition(RootGrid);
        var newX = pos.X - _dragOffset.X;
        var newY = pos.Y - _dragOffset.Y;

        // Clamp to keep the bar fully visible
        var barW = SearchBarBorder.ActualWidth * (SearchBarBorder.LayoutTransform as ScaleTransform)?.ScaleX ?? SearchBarBorder.ActualWidth;
        var barH = SearchBarBorder.ActualHeight * (SearchBarBorder.LayoutTransform as ScaleTransform)?.ScaleY ?? SearchBarBorder.ActualHeight;
        newX = Math.Max(0, Math.Min(newX, ActualWidth - barW));
        newY = Math.Max(0, Math.Min(newY, ActualHeight - barH));

        SearchBarBorder.Margin = new Thickness(newX, newY, 0, 0);
    }

    private void ApplySearchBarSize()
    {
        var app = (App)System.Windows.Application.Current;
        var scale = app.SearchBarSize switch
        {
            "large" => 1.4,
            "medium" => 1.0,
            "small" => 0.75,
            "tiny" => 0.55,
            _ => 1.0
        };
        SearchBarBorder.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var history = app.SearchHistory;

        if (e.Key == Key.Up && history.Count > 0)
        {
            _historyIndex = Math.Min(_historyIndex + 1, history.Count - 1);
            SearchBox.Text = history[_historyIndex];
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _historyIndex--;
            if (_historyIndex < 0)
            {
                _historyIndex = -1;
                SearchBox.Text = "";
            }
            else
            {
                SearchBox.Text = history[_historyIndex];
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var query = SearchBox.Text.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                app.AddToSearchHistory(query);
                _historyIndex = -1;
            }
            e.Handled = true;
        }
        else if (e.Key != Key.Escape)
        {
            _historyIndex = -1;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            CloseAndSaveQuery();
    }

    // Right-click anywhere on the overlay = same as Escape, EXCEPT when the
    // click lands on the search bar (so the user can still right-click into
    // the search box for paste, etc.).
    //
    // We swallow BOTH Down and Up events so the underlying app never gets a
    // dangling button release. If we close the window on Down (while the user
    // is still holding the button), the Up gets delivered to whatever ends up
    // under the cursor — and DefWindowProc turns that Up into a WM_CONTEXTMENU
    // for the underlying app. Handling Up (and consuming Down) prevents that.
    private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsInsideSearchBar(src))
            return;
        // Mark handled but DON'T close yet — we close on Up so the release
        // event is fully consumed before our window is destroyed.
        e.Handled = true;
    }

    private void Window_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && IsInsideSearchBar(src))
            return;
        e.Handled = true;
        CloseAndSaveQuery();
    }

    private bool IsInsideSearchBar(DependencyObject obj)
    {
        while (obj != null)
        {
            if (ReferenceEquals(obj, SearchBarBorder)) return true;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj)
                  ?? System.Windows.LogicalTreeHelper.GetParent(obj);
        }
        return false;
    }

    private void CloseAndSaveQuery()
    {
        var app = (App)System.Windows.Application.Current;
        var query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
            app.AddToSearchHistory(query);
        Close();
    }
}
