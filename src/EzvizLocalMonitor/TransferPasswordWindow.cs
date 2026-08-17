using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EzvizLocalMonitor;

public sealed class TransferPasswordWindow : Window
{
    private readonly TextBox _passwordBox = new() { PasswordChar = '●', Watermark = "Ít nhất 8 ký tự" };
    private readonly TextBox? _confirmBox;
    private readonly int _minimumLength;
    private readonly bool _digitsOnly;

    public TransferPasswordWindow(string title, string description, bool confirmPassword, int minimumLength = 8, bool digitsOnly = false)
    {
        _minimumLength = minimumLength;
        _digitsOnly = digitsOnly;
        Title = title;
        Width = 520;
        Height = confirmPassword ? 330 : 285;
        MinWidth = 520;
        MinHeight = Height;
        MaxWidth = 520;
        MaxHeight = Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#F8FAFC"));

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A"))
        };
        var body = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            Margin = new Thickness(0, 10, 0, 14)
        };
        var passwordLabel = new TextBlock { Text = "Mật khẩu backup", FontWeight = FontWeight.SemiBold };
        _passwordBox.Margin = new Thickness(0, 5, 0, 10);
        _passwordBox.MinHeight = 36;

        var rows = new StackPanel { Spacing = 3 };
        rows.Children.Add(heading);
        rows.Children.Add(body);
        rows.Children.Add(passwordLabel);
        rows.Children.Add(_passwordBox);

        if (confirmPassword)
        {
            var confirmLabel = new TextBlock { Text = "Nhập lại mật khẩu", FontWeight = FontWeight.SemiBold };
            _confirmBox = new TextBox { PasswordChar = '●', Watermark = "Nhập lại để xác nhận", MinHeight = 36 };
            rows.Children.Add(confirmLabel);
            rows.Children.Add(_confirmBox);
        }

        var status = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#B42318")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = CreateButton("Hủy", "#475569", "#334155");
        var accept = CreateButton(confirmPassword ? "Tạo backup" : "Khôi phục", "#2563EB", "#1D4ED8");
        cancel.Click += (_, _) => Close(null);
        accept.Click += (_, _) =>
        {
            var password = _passwordBox.Text ?? string.Empty;
            if (password.Length < _minimumLength || (_digitsOnly && password.Any(c => c < '0' || c > '9')))
            {
                status.Text = _digitsOnly
                    ? $"PIN phải gồm ít nhất {_minimumLength} chữ số."
                    : $"Mật khẩu phải có ít nhất {_minimumLength} ký tự.";
                return;
            }
            if (confirmPassword && !string.Equals(password, _confirmBox?.Text, StringComparison.Ordinal))
            {
                status.Text = "Hai mật khẩu chưa giống nhau.";
                return;
            }
            Close(password);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, accept }
        };
        rows.Children.Add(status);
        rows.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(22), Child = rows };
    }

    private static Button CreateButton(string text, string background, string hover)
    {
        var normal = new SolidColorBrush(Color.Parse(background));
        var button = new Button
        {
            Content = text,
            Width = 112,
            MinHeight = 36,
            Padding = new Thickness(12, 6),
            Background = normal,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            BorderBrush = new SolidColorBrush(Color.Parse("#0F172A")),
            BorderThickness = new Thickness(1)
        };
        var over = new SolidColorBrush(Color.Parse(hover));
        button.PointerEntered += (_, _) => button.Background = over;
        button.PointerExited += (_, _) => button.Background = normal;
        return button;
    }
}
