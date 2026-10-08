using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using GaleWorkTimer.ViewModels;
using GaleWorkTimer.Domain;

namespace GaleWorkTimer.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel model;

    public MainWindow(MainWindowViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        NewColor.SelectedItem = TimerPalette.Get(model.SuggestedColorKey);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static TimerRow Row(object? sender) => (TimerRow)((Control)sender!).DataContext!;

    private void MinimizeWindow(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ExitApplication(object? sender, RoutedEventArgs e) => Close();

    private void AddTimer(object? sender, RoutedEventArgs e)
    {
        if (model.CreateTimer(NewName.Text ?? "", NowMs(), (NewColor.SelectedItem as TimerColor)?.ColorKey))
        {
            NewName.Text = "";
            NewColor.SelectedItem = TimerPalette.Get(model.SuggestedColorKey);
        }
    }

    private void StartTimer(object? sender, RoutedEventArgs e) => model.StartTimer(Row(sender).Id, NowMs());
    private void PauseTimer(object? sender, RoutedEventArgs e) => model.PauseTimer(Row(sender).Id, NowMs());
    private void ResumeTimer(object? sender, RoutedEventArgs e) => model.ResumeTimer(Row(sender).Id, NowMs());
    private void StopTimer(object? sender, RoutedEventArgs e) => model.StopTimer(Row(sender).Id, NowMs());
    private void ArchiveTimer(object? sender, RoutedEventArgs e) => model.ArchiveTimer(Row(sender).Id, NowMs());
    private void RestoreTimer(object? sender, RoutedEventArgs e) => model.RestoreTimer(Row(sender).Id, NowMs());
    private void RefreshHistory(object? sender, RoutedEventArgs e) => model.RefreshHistory(NowMs());
    private void RefreshTimers(object? sender, RoutedEventArgs e) => model.RefreshTimers(NowMs());

    private void Navigate(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Navigation) return;
        var snapshotAtMs = NowMs();
        if (Navigation.SelectedIndex == 0) model.RefreshTimers(snapshotAtMs);
        else if (Navigation.SelectedIndex == 1) model.RefreshHistory(snapshotAtMs);
    }

    private async void EditTimer(object? sender, RoutedEventArgs e)
    {
        var timer = Row(sender);
        var name = new TextBox { Text = timer.Name };
        Avalonia.Automation.AutomationProperties.SetName(name, "Timer name");
        var color = new ComboBox { ItemsSource = TimerPalette.Colors, SelectedItem = TimerPalette.Get(timer.ColorKey) };
        Avalonia.Automation.AutomationProperties.SetName(color, "Timer color");
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window
        {
            Title = "Edit timer", Width = 440, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Name" }, name, new TextBlock { Text = "Color" }, color, error,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 12,
                        HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel }
                    }
                }
            }
        };
        save.Click += (_, _) =>
        {
            if (color.SelectedItem is not TimerColor selected)
                error.Text = "Choose a color from the palette.";
            else if (model.EditTimer(timer.Id, name.Text ?? "", selected.ColorKey, NowMs())) dialog.Close();
            else error.Text = model.ErrorMessage;
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        await dialog.ShowDialog(this);
    }
}
