using System.ComponentModel;
using System.Runtime.CompilerServices;
using DesktopDlnaCast.Core.Models;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopDlnaCast.App.ViewModels;

public sealed class CaptureDisplayViewModel(
    DisplayCaptureSource source,
    string displayName) : INotifyPropertyChanged
{
    private WriteableBitmap? preview;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DisplayCaptureSource Source { get; } = source;

    public string DisplayName { get; } = displayName;

    public WriteableBitmap? Preview
    {
        get => preview;
        set
        {
            if (ReferenceEquals(preview, value))
            {
                return;
            }

            preview = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }
}
