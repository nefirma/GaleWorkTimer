using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using GaleWorkTimer.Services;
using GaleWorkTimer.ViewModels;
using GaleWorkTimer.Views;

namespace GaleWorkTimer;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            TimerService? service = null;
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(root))
                    throw new InvalidOperationException("Local application-data location is unavailable.");
                var directory = Path.Combine(root, "NeFirma", "GaleWorkTimer");
                Directory.CreateDirectory(directory);
                service = new TimerService(Path.Combine(directory, "data.db"));
                desktop.MainWindow = new MainWindow(new MainWindowViewModel(service, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                desktop.Exit += (_, _) => service.Dispose(); // Never Stop sessions on exit.
            }
            catch (Exception)
            {
                service?.Dispose();
                var exit = new Button { Content = "Exit", HorizontalAlignment = HorizontalAlignment.Right };
                exit.Click += (_, _) => desktop.Shutdown(1);
                desktop.MainWindow = new Window
                {
                    Title = "GaleWorkTimer — startup failed", Width = 460, SizeToContent = SizeToContent.Height,
                    CanResize = false,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(24), Spacing = 18,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Could not open the application database. No timer data is available. Check storage access and free space, then start the application again.",
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            exit
                        }
                    }
                };
                desktop.Exit += (_, e) => e.ApplicationExitCode = 1;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
