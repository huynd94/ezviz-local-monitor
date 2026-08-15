using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EzvizLocalMonitor;

public sealed class CleanupConfirmWindow : Window
{
    public CleanupConfirmWindow(string title, string warning)
    {
        Title = $"Xác nhận dọn dữ liệu — {title}";
        Width = 560;
        Height = 260;
        MinWidth = 560;
        MinHeight = 260;
        MaxWidth = 560;
        MaxHeight = 260;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#F8FAFC"));

        var heading = new TextBlock
        {
            Text = "Xác nhận thao tác dọn dữ liệu",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var body = new TextBlock
        {
            Text = warning,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            LineHeight = 20
        };
        var note = new TextBlock
        {
            Text = "Các file đang được ứng dụng khác sử dụng có thể được giữ lại và báo trong kết quả.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#64748B")),
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = "Hủy", Width = 90, Padding = new Thickness(12, 6) };
        var confirm = new Button { Content = "Đồng ý dọn", Width = 120, Padding = new Thickness(12, 6) };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { cancel, confirm }
        };
        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
                Children = { heading, body, note, buttons }
            }
        };
        Grid.SetRow(body, 1);
        Grid.SetRow(note, 2);
        Grid.SetRow(buttons, 3);
    }
}
