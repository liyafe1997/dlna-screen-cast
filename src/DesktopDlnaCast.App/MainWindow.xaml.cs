using System.Runtime.InteropServices;
using DesktopDlnaCast.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DesktopDlnaCast.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 780;
    private const int DefaultHeight = 500;

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
