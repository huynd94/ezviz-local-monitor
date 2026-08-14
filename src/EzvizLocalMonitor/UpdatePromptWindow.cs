using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor;

public sealed class UpdatePromptWindow : Window
{
    public UpdatePromptWindow(AppUpdateInfo update)
    {
        Title = "Có bản cập nhật EZVIZ Local Monitor";
        Width = 520;
        Height = 270;
        MinWidth = 520;
        MinHeight = 270;
        MaxWidth = 520;
        MaxHeight = 270;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var title = new TextBlock
        {
            Text = $"Đã có phiên bản {update.LatestVersion.ToString(3)}",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#093B5A"))
        };

        var details = new TextBlock
        {
            Text = $"Phiên bản hiện tại: {update.CurrentVersion.ToString(3)}\nBản cập nhật được tải từ kênh public và được xác minh SHA-256 trước khi cài.\nỨng dụng sẽ mở updater GUI riêng, sau đó có thể tự khởi động lại.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#52606D")),
            Margin = new Thickness(0, 12, 0, 20)
        };

        var updateButton = new Button { Content = "Cập nhật ngay", MinWidth = 120 };
        var laterButton = new Button { Content = "Để sau", MinWidth = 100 };
        var releaseButton = new Button { Content = "Mở trang release", MinWidth = 125 };
        updateButton.Click += (_, _) => Close(UpdatePromptChoice.Update);
        laterButton.Click += (_, _) => Close(UpdatePromptChoice.Later);
        releaseButton.Click += (_, _) => Close(UpdatePromptChoice.OpenRelease);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { releaseButton, laterButton, updateButton }
        };

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Children = { title, details, buttons }
            }
        };
    }
}
