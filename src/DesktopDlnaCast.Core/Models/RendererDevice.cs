using System.Net;

namespace DesktopDlnaCast.Core.Models;

public sealed record RendererDevice(
    string Udn,
    string FriendlyName,
    string? Manufacturer,
    string? ModelName,
    IPAddress Address,
    Uri DescriptionUri);

