using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace EzvizLocalMonitor;

public partial class App : Application
{
    private TrayIcons? _trayIcons;
    private TrayIcon? _trayIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            SetupTray(window);
            desktop.Exit += (_, _) => DisposeTray();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray(MainWindow window)
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "EZVIZ Local Monitor — đang chạy nền",
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://EzvizLocalMonitor/Assets/ezviz-local-monitor.ico")))
        };

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem { Header = "Mở EZVIZ Local Monitor" };
        var hideItem = new NativeMenuItem { Header = "Ẩn vào khay thông báo" };
        var exitItem = new NativeMenuItem { Header = "Thoát hoàn toàn" };
        showItem.Click += (_, _) => window.ShowFromTray();
        hideItem.Click += (_, _) => window.HideToTray();
        exitItem.Click += (_, _) => window.ExitFromTray();
        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);
        _trayIcon.Menu = menu;
        _trayIcon.Clicked += (_, _) => window.ShowFromTray();

        _trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, _trayIcons);
    }

    private void DisposeTray()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIcons = null;
    }
}
