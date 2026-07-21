namespace DesktopDlnaCast.App.ViewModels;

public sealed record VideoOutputProfileViewModel(
    string DisplayName,
    int Width,
    int Height,
    int VideoBitrate,
    int AudioBitrate);
