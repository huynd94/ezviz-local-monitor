using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EzvizLocalMonitor;

public sealed class CleanupConfirmWindow : Window
{
    private static readonly IBrush WindowBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush HeadingForeground = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush BodyForeground = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush MutedForeground = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush CancelBackground = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush CancelHoverBackground = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush ConfirmBackground = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush ConfirmHoverBackground = new SolidColorBrush(Color.Parse("#1D4ED8"));
    private static readonly IBrush ButtonForeground = Brushes.White;
    private static readonly IBrush ButtonBorder = new SolidColorBrush(Color.Parse("#0F172A"));

    public CleanupConfirmWindow(string title, string warning)
    {
        Title = $"Xác nhận dọn dữ liệu — {title}";
        Width = 560;
        Height = 300;
        MinWidth = 560;
        MinHeight = 300;
        MaxWidth = 560;
        MaxHeight = 300;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBackground;

        var heading = new TextBlock
        {
            Text = "Xác nhận thao tác dọn dữ liệu",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeadingForeground,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var body = new TextBlock
        {
            Text = warning,
            TextWrapping = TextWrapping.Wrap,
            Foreground = BodyForeground,
            LineHeight = 20
        };
        var note = new TextBlock
        {
            Text = "Các file đang được ứng dụng khác sử dụng có thể được giữ lại và báo trong kết quả.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedForeground,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = CreateButton("Hủy", 92, CancelBackground, CancelHoverBackground);
        var confirm = CreateButton("Đồng ý dọn", 124, ConfirmBackground, ConfirmHoverBackground);
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { cancel, confirm }
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };
        Grid.SetRow(heading, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(note, 2);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(heading);
        layout.Children.Add(body);
        layout.Children.Add(note);
        layout.Children.Add(buttons);

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = layout
        };
    }

    private static Button CreateButton(string text, double width, IBrush background, IBrush hoverBackground)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            MinHeight = 36,
            Padding = new Thickness(12, 6),
            Background = background,
            Foreground = ButtonForeground,
            BorderBrush = ButtonBorder,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.PointerEntered += (_, _) => button.Background = hoverBackground;
        button.PointerExited += (_, _) => button.Background = background;
        return button;
    }
}
