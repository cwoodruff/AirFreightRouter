using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace AirFreightRouter.Views;

public enum MessageBoxResult { Ok, Yes, No }

public partial class AppMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.Ok;

    public AppMessageBox()
    {
        InitializeComponent();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.No;
        Close();
    }

    // ------------------------------------------------------------------
    // Static factory methods
    // ------------------------------------------------------------------

    public static Task ShowErrorAsync(Window owner, string title, string message) =>
        ShowAsync(owner, title, message, yesNo: false);

    public static Task ShowWarningAsync(Window owner, string title, string message) =>
        ShowAsync(owner, title, message, yesNo: false);

    public static async Task<MessageBoxResult> ShowYesNoAsync(Window owner, string title, string message)
        => await ShowAsync(owner, title, message, yesNo: true);

    private static async Task<MessageBoxResult> ShowAsync(
        Window owner, string title, string message, bool yesNo)
    {
        var dlg = new AppMessageBox();
        dlg.TbTitle.Text   = title;
        dlg.TbMessage.Text = message;

        if (yesNo)
        {
            var yesBtn = MakeButton("Yes",  isPrimary: true);
            var noBtn  = MakeButton("No",   isPrimary: false);

            yesBtn.Click += (_, _) => { dlg._result = MessageBoxResult.Yes; dlg.Close(); };
            noBtn.Click  += (_, _) => { dlg._result = MessageBoxResult.No;  dlg.Close(); };

            dlg.ButtonPanel.Children.Add(noBtn);
            dlg.ButtonPanel.Children.Add(yesBtn);
        }
        else
        {
            var okBtn = MakeButton("OK", isPrimary: true);
            okBtn.Click += (_, _) => { dlg._result = MessageBoxResult.Ok; dlg.Close(); };
            dlg.ButtonPanel.Children.Add(okBtn);
        }

        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private static Button MakeButton(string content, bool isPrimary)
    {
        var btn = new Button { Content = content, Width = 80 };
        var themeKey = isPrimary ? "PrimaryButton" : "SecondaryButton";

        if (Application.Current!.TryGetResource(themeKey, null, out var theme) &&
            theme is ControlTheme ct)
        {
            btn.Theme = ct;
        }

        return btn;
    }
}
