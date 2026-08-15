using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EzvizLocalMonitor.Services;

namespace EzvizLocalMonitor;

public sealed class UpdatePromptWindow : Window
{
    private static readonly IBrush WindowBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush HeadingForeground = new SolidColorBrush(Color.Parse("#093B5A"));
    private static readonly IBrush BodyForeground = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush PrimaryButtonBackground = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush PrimaryButtonHoverBackground = new SolidColorBrush(Color.Parse("#1D4ED8"));
    private static readonly IBrush SecondaryButtonBackground = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush SecondaryButtonHoverBackground = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush ButtonForeground = Brushes.White;
    private static readonly IBrush ButtonBorder = new SolidColorBrush(Color.Parse("#0F172A"));

    public UpdatePromptWindow(AppUpdateInfo update)
    {
        Title = "Có bản cập nhật EZVIZ Local Monitor";
        Width = 560;
        Height = 340;
        MinWidth = 560;
        MinHeight = 340;
        MaxWidth = 560;
        MaxHeight = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBackground;

        var title = new TextBlock
        {
            Text = $"Đã có phiên bản {update.LatestVersion.ToString(3)}",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeadingForeground,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var details = new TextBlock
        {
            Text = $"Phiên bản hiện tại: {update.CurrentVersion.ToString(3)}\nBản cập nhật được tải từ kênh public và được xác minh SHA-256 trước khi cài.\nỨng dụng sẽ mở updater GUI riêng, sau đó có thể tự khởi động lại.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = BodyForeground,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var updateButton = CreateActionButton("Cập nhật ngay", 132, PrimaryButtonBackground, PrimaryButtonHoverBackground);
        var laterButton = CreateActionButton("Để sau", 100, SecondaryButtonBackground, SecondaryButtonHoverBackground);
        var releaseButton = CreateActionButton("Mở trang release", 138, SecondaryButtonBackground, SecondaryButtonHoverBackground);
        updateButton.Click += (_, _) => Close(UpdatePromptChoice.Update);
        laterButton.Click += (_, _) => Close(UpdatePromptChoice.Later);
        releaseButton.Click += (_, _) => Close(UpdatePromptChoice.OpenRelease);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { releaseButton, laterButton, updateButton }
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };
        Grid.SetRow(title, 0);
        Grid.SetRow(details, 1);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(title);
        layout.Children.Add(details);
        layout.Children.Add(buttons);

        Content = new Border
        {
            Padding = new Thickness(24),
            Background = WindowBackground,
            Child = layout
        };
    }

    private static Button CreateActionButton(string text, double width, IBrush background, IBrush hoverBackground)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 38,
            MinWidth = width,
            Padding = new Thickness(12, 6),
            Background = background,
            Foreground = ButtonForeground,
            BorderBrush = ButtonBorder,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            Focusable = true
        };

        button.PointerEntered += (_, _) => button.Background = hoverBackground;
        button.PointerExited += (_, _) => button.Background = background;
        return button;
    }
}

