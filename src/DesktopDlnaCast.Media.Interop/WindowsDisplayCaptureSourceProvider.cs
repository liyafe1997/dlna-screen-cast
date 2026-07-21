using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Media.Interop;

public sealed partial class WindowsDisplayCaptureSourceProvider : IDisplayCaptureSourceProvider
{
    private const uint MonitorInfoPrimary = 0x1;

    public IReadOnlyList<DisplayCaptureSource> GetDisplays()
    {
        List<MonitorSnapshot> snapshots = [];
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not describe a display monitor.");
            }

            snapshots.Add(new(
                monitor.ToInt64(),
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top,
                (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        };

        if (!EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate display monitors.");
        }

        return CreateSources(snapshots);
    }

    internal static IReadOnlyList<DisplayCaptureSource> CreateSources(
        IEnumerable<MonitorSnapshot> snapshots) => snapshots
            .Where(static monitor => monitor.Handle != 0 && monitor.Width > 0 && monitor.Height > 0)
            .OrderByDescending(static monitor => monitor.IsPrimary)
            .ThenBy(static monitor => monitor.Left)
            .ThenBy(static monitor => monitor.Top)
            .Select(static (monitor, index) => new DisplayCaptureSource(
                monitor.Handle,
                index + 1,
                monitor.Left,
                monitor.Top,
                monitor.Width,
                monitor.Height,
                monitor.IsPrimary))
            .ToArray();

    internal sealed record MonitorSnapshot(
        long Handle,
        int Left,
        int Top,
        int Width,
        int Height,
        bool IsPrimary);

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint deviceContext,
        nint monitorRectangle,
        nint data);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        MonitorEnumProc callback,
        nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
