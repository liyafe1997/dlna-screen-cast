using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.App.ViewModels;

public sealed record AspectRatioOptionViewModel(
    string DisplayName,
    AspectRatioMode Mode,
    string IllustrationPath);
