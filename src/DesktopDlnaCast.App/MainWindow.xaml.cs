using System.Runtime.InteropServices;
using DesktopDlnaCast.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DesktopDlnaCast.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 780;
    private const int DefaultHeight = 500;
    private bool suppressOutputProfileSelection;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ResourceLoader resources = new();
        Title = resources.GetString("WindowTitle");
        TitleBarText.Text = Title;
        // The content area follows the resolved UI language; the title bar stays
        // left-to-right because the system caption buttons do not mirror.
        if (string.Equals(resources.GetString("LayoutDirection"), "RTL", StringComparison.Ordinal))
        {
            ContentRoot.FlowDirection = FlowDirection.RightToLeft;
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyInitialSize();
    }

    public MainViewModel ViewModel { get; }

    private async void DisplayPicker_DropDownOpened(object sender, object e)
    {
        await ViewModel.RefreshDisplayPreviewsAsync();
    }

    private async void OutputProfilePicker_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (suppressOutputProfileSelection ||
            OutputProfilePicker.SelectedItem is not VideoOutputProfileViewModel { IsCustomCommand: true })
        {
            return;
        }

        VideoOutputProfileViewModel? previous = e.RemovedItems
            .OfType<VideoOutputProfileViewModel>()
            .FirstOrDefault(profile => !profile.IsCustomCommand);
        ResourceLoader resources = new();
        TextBox widthInput = new()
        {
            Header = resources.GetString("CustomResolutionWidthLabel"),
            PlaceholderText = "1920",
            Text = previous?.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) ??
                string.Empty,
        };
        TextBox heightInput = new()
        {
            Header = resources.GetString("CustomResolutionHeightLabel"),
            PlaceholderText = "1080",
            Text = previous?.Height.ToString(System.Globalization.CultureInfo.InvariantCulture) ??
                string.Empty,
        };
        TextBlock error = new()
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid dimensions = new() { ColumnSpacing = 12 };
        dimensions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        dimensions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        dimensions.Children.Add(widthInput);
        Grid.SetColumn(heightInput, 1);
        dimensions.Children.Add(heightInput);
        StackPanel content = new() { Spacing = 8, MinWidth = 320 };
        content.Children.Add(dimensions);
        content.Children.Add(error);
        ContentDialog dialog = new()
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = resources.GetString("CustomResolutionTitle"),
            Content = content,
            PrimaryButtonText = resources.GetString("ConfirmButton"),
            CloseButtonText = resources.GetString("CancelButton"),
            DefaultButton = ContentDialogButton.Primary,
        };

        bool accepted = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!int.TryParse(widthInput.Text.Trim(), out int width) ||
                !int.TryParse(heightInput.Text.Trim(), out int height) ||
                width is < 2 or > 7680 ||
                height is < 2 or > 4320 ||
                (width & 1) != 0 ||
                (height & 1) != 0)
            {
                args.Cancel = true;
                error.Text = resources.GetString("CustomResolutionInvalid");
                return;
            }

            accepted = true;
            suppressOutputProfileSelection = true;
            ViewModel.AddCustomOutputProfile(width, height);
            suppressOutputProfileSelection = false;
        };

        await dialog.ShowAsync();
        if (!accepted)
        {
            suppressOutputProfileSelection = true;
            ViewModel.SelectedOutputProfile = previous ?? ViewModel.OutputProfiles[0];
            suppressOutputProfileSelection = false;
        }
    }

    private void ApplyInitialSize()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(DefaultWidth * scale),
            (int)(DefaultHeight * scale)));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
