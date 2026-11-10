using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StripView.Device;

namespace StripView;

public partial class MainWindow : Window
{
    private readonly AppState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        HomePage.Attach(_state);
        DesignPage.Attach(_state);
        DiagPage.Attach(_state);
        HomePage.RequestDesign += () => TabDesign.IsChecked = true;

        TabHome.Checked += (_, _) => Show(HomePage);
        TabDesign.Checked += (_, _) => Show(DesignPage);
        TabDiag.Checked += (_, _) => Show(DiagPage);

        _state.StateChanged += UpdateChrome;
        PreviewKeyDown += OnShortcut;
        Closing += (_, _) =>
        {
            _state.SaveLayout();
            _state.Dispose();
        };
        _state.Start();
        UpdateChrome();
    }

    private void Show(UIElement page)
    {
        foreach (var p in new UIElement[] { HomePage, DesignPage, DiagPage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (HomePage.Visibility != Visibility.Visible || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            _state.TogglePause();
            e.Handled = true;
        }
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _state.PushOnce();
            e.Handled = true;
        }
    }

    private void UpdateChrome()
    {
        var paused = _state.StreamPaused;
        var link = _state.Link;
        var sending = link.CanPush || link.PushBusy;
        SyncText.Text = paused ? "PAUSED" : sending ? $"SYNCED · {_state.CadenceSeconds}s" : $"PREVIEW · {_state.CadenceSeconds}s";
        SyncDot.Opacity = paused ? 0.35 : 1;
        var target = _state.Target;
        var where = link.SessionOpen
            ? $"{DeviceIdentity.StripName} connected on {link.PortName}"
            : target is null ? $"{DeviceIdentity.StripName} not detected" : $"{DeviceIdentity.StripName} on {target.PortName}, not connected";
        StatusText.Text = $"{_state.Status}  ·  {where}";
        StatusDot.Fill = (Brush)FindResource(link.TxHealthy ? "Ok" : target is null || link.TxLockedOut ? "Bad" : "Warn");
    }
}
