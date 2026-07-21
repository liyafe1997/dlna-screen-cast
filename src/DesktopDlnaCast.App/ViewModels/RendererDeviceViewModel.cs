using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.App.ViewModels;

public sealed class RendererDeviceViewModel(RendererDevice device)
{
    public RendererDevice Device { get; } = device;

    public string DisplayName =>
        $"{Device.FriendlyName} — {Device.Manufacturer ?? "-"} {Device.ModelName ?? "-"} — {Device.Address}";
}

