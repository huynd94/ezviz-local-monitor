using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace EzvizLocalMonitor;

public sealed class OnboardingWindow : Window
{
    public bool Completed { get; private set; }

    public OnboardingWindow()
    {
        Title = "Thiết lập EZVIZ Local Monitor";
        Width = 620;
        Height = 470;
        MinWidth = 560;
        MinHeight = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var steps = new StackPanel { Spacing = 14 };
        steps.Children.Add(new TextBlock
        {
            Text = "Bắt đầu với EZVIZ Local Monitor",
            FontSize = 24,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = Avalonia.Media.Brushes.DarkSlateGray
        });
        steps.Children.Add(new TextBlock
        {
            Text = "Ứng dụng xử lý video và nhận diện người cục bộ trong mạng LAN. Bạn có thể hoàn thành từng bước ngay bây giờ hoặc bỏ qua và cấu hình sau.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.DimGray
        });
        steps.Children.Add(Step("1", "Tìm camera trong LAN", "Mở tab Camera, bấm Tìm camera trong LAN và chọn C6N/H8C được phát hiện."));
        steps.Children.Add(Step("2", "Nhập mã xác thực", "Nhập mã xác thực trên nhãn camera. Không gửi mã này qua chat hoặc đưa vào log."));
        steps.Children.Add(Step("3", "Kiểm tra RTSP/ONVIF", "Bấm Kiểm tra RTSP và xác nhận camera đang ở cùng mạng LAN với máy Windows."));
        steps.Children.Add(Step("4", "Cấu hình cảnh báo", "Mở tab Cảnh báo, nhập Telegram/Zalo, bấm Kiểm tra toàn bộ cấu hình rồi Gửi thử thủ công."));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var later = new Button { Content = "Để sau" };
        var done = new Button { Content = "Hoàn tất thiết lập" };
        later.Click += (_, _) => Close();
        done.Click += (_, _) => { Completed = true; Close(); };
        actions.Children.Add(later);
        actions.Children.Add(done);
        steps.Children.Add(actions);
        Content = new Border
        {
            Padding = new Thickness(24),
            Background = Avalonia.Media.Brushes.White,
            Child = new ScrollViewer { Content = steps }
        };
    }

    private static Control Step(string number, string title, string description)
    {
        return new Border
        {
            Background = Avalonia.Media.Brushes.AliceBlue,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Border
                    {
                        Width = 30,
                        Height = 30,
                        CornerRadius = new CornerRadius(15),
                        Background = Avalonia.Media.Brushes.SteelBlue,
                        Child = new TextBlock { Text = number, Foreground = Avalonia.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    },
                    new StackPanel
                    {
                        [Grid.ColumnProperty] = 1,
                        Spacing = 3,
                        Children =
                        {
                            new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.DarkSlateGray },
                            new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brushes.DimGray }
                        }
                    }
                }
            }
        };
    }
}
