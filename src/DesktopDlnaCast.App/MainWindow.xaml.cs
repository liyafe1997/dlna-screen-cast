using System.Reflection;
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
    private const string ProjectHomepage = "https://github.com/liyafe1997/dlna-screen-cast";
    private const string DonationPage = "https://ko-fi.com/strawing";
    private bool suppressOutputProfileSelection;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ResourceLoader resources = new();
        Title = resources.GetString("WindowTitle");
        TitleBarText.Text = Title;
        MainDonateLinkText.Text = resources.GetString("AboutDonateLink");
        MainDonateDescriptionText.Text = resources.GetString("AboutDonateDescription");
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

    private async void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        ResourceLoader resources = new();
        Assembly assembly = typeof(MainWindow).Assembly;
        string version = assembly.GetName().Version?.ToString(3) ?? resources.GetString("AboutUnknownValue");
        string buildDate = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildDate")?
            .Value ?? resources.GetString("AboutUnknownValue");

        StackPanel productIdentity = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 4,
        };
        productIdentity.Children.Add(new FontIcon
        {
            Glyph = "\uE7F4",
            FontSize = 40,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.CornflowerBlue),
        });
        productIdentity.Children.Add(new TextBlock
        {
            Text = resources.GetString("WindowTitle"),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        productIdentity.Children.Add(new TextBlock
        {
            Text = resources.GetString("AboutTagline"),
            Opacity = 0.7,
            TextAlignment = TextAlignment.Center,
        });

        Grid details = new() { ColumnSpacing = 24, RowSpacing = 8 };
        details.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        AddAboutDetail(details, 0, resources.GetString("AboutVersionLabel"), version);
        AddAboutDetail(details, 1, resources.GetString("AboutBuildDateLabel"), buildDate);

        HyperlinkButton homepage = new()
        {
            Content = string.Concat(
                resources.GetString("AboutSourceLabel"),
                "liyafe1997/dlna-screen-cast"),
            NavigateUri = new Uri(ProjectHomepage),
            Padding = new Thickness(0, 4, 0, 4),
        };
        StackPanel donationContent = new() { Spacing = 2 };
        donationContent.Children.Add(new TextBlock
        {
            Text = resources.GetString("AboutDonateLink"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        donationContent.Children.Add(new TextBlock
        {
            Text = resources.GetString("AboutDonateDescription"),
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });
        HyperlinkButton donate = new()
        {
            Content = donationContent,
            NavigateUri = new Uri(DonationPage),
            Padding = new Thickness(0, 4, 0, 4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        StackPanel links = new() { Spacing = 2 };
        links.Children.Add(new TextBlock
        {
            Text = resources.GetString("AboutLinksLabel"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        links.Children.Add(homepage);
        links.Children.Add(donate);

        StackPanel content = new()
        {
            MinWidth = 360,
            Spacing = 18,
        };
        content.Children.Add(productIdentity);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray),
            Opacity = 0.25,
        });
        content.Children.Add(details);
        content.Children.Add(links);

        ContentDialog dialog = new()
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = resources.GetString("AboutDialogTitle"),
            Content = content,
            CloseButtonText = resources.GetString("AboutCloseButton"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static void AddAboutDetail(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        TextBlock labelBlock = new()
        {
            Text = label,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock valueBlock = new()
        {
            Text = value,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
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
