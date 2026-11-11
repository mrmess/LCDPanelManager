using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StripView.Device;
using StripView.Model;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace StripView;

public partial class MainWindow : Window
{
    private readonly AppState _state = new();
    private readonly AppPreferences _preferences;
    private Forms.NotifyIcon? _tray;
    private Drawing.Icon? _trayIcon;
    private bool _exiting;

    public bool StartHidden => _preferences.StartMinimized;

    public MainWindow()
    {
        _preferences = AppPreferences.Load();
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
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) HideToTray();
        };
        Closing += OnClosing;
        Application.Current.SessionEnding += (_, _) =>
        {
            _exiting = true;
        };

        InitializeTray();
        if (_preferences.StartWithWindows)
        {
            try { WindowsStartup.Set(true); } catch (Exception ex) { _state.Log("Could not update startup registration: " + ex.Message); }
        }
        _state.Start();
        UpdateChrome();
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _preferences.Save();
        _state.SaveLayout();
        _state.Dispose();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _trayIcon?.Dispose();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void InitializeTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Case LCD", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var minimized = new Forms.ToolStripMenuItem("Start minimized")
        {
            CheckOnClick = true,
            Checked = _preferences.StartMinimized,
        };
        minimized.CheckedChanged += (_, _) =>
        {
            _preferences.StartMinimized = minimized.Checked;
            _preferences.Save();
        };
        menu.Items.Add(minimized);

        var startup = new Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = _preferences.StartWithWindows,
        };
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                WindowsStartup.Set(startup.Checked);
                _preferences.StartWithWindows = startup.Checked;
                _preferences.Save();
            }
            catch (Exception ex)
            {
                startup.Checked = _preferences.StartWithWindows;
                _state.Log("Could not update startup registration: " + ex.Message);
            }
        };
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = Drawing.Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "StripView.exe"));
        _tray = new Forms.NotifyIcon
        {
            Text = "Case LCD",
            Icon = _trayIcon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
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
