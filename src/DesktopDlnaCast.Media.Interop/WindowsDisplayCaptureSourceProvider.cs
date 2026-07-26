using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Media.Interop;

public sealed partial class WindowsDisplayCaptureSourceProvider : IDisplayCaptureSourceProvider
{
    private const uint MonitorInfoPrimary = 0x1;
    private const int DwmWindowAttributeCloaked = 14;
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;

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

        List<DisplayCaptureSource> sources = [.. CreateSources(snapshots)];
        sources.AddRange(GetWindowSources());
        return sources;
    }

    private static IReadOnlyList<DisplayCaptureSource> GetWindowSources()
    {
        List<WindowSnapshot> snapshots = [];
        uint currentProcessId = (uint)Environment.ProcessId;
        EnumWindowsProc callback = (window, data) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(window, out uint processId);
            int titleLength = GetWindowTextLength(window);
            if (processId == currentProcessId || titleLength <= 0 ||
                (GetWindowLongPtr(window, ExtendedStyleIndex).ToInt64() & ToolWindowStyle) != 0)
            {
                return true;
            }

            int cloaked = 0;
            if (DwmGetWindowAttribute(
                    window,
                    DwmWindowAttributeCloaked,
                    out cloaked,
                    sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            if (!GetWindowRect(window, out NativeRectangle rectangle))
            {
                return true;
            }

            int width = rectangle.Right - rectangle.Left;
            int height = rectangle.Bottom - rectangle.Top;
            if (width <= 1 || height <= 1)
            {
                return true;
            }

            string? title = ReadWindowTitle(window, titleLength);
            if (title is not null)
            {
                snapshots.Add(new(
                    window.ToInt64(),
                    title,
                    rectangle.Left,
                    rectangle.Top,
                    width,
                    height));
            }

            return true;
        };

        if (!EnumWindows(callback, nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate application windows.");
        }

        return CreateWindowSources(snapshots);
    }

    private static unsafe string? ReadWindowTitle(nint window, int titleLength)
    {
        char[] buffer = new char[titleLength + 1];
        fixed (char* bufferPointer = buffer)
        {
            int copied = GetWindowText(window, bufferPointer, buffer.Length);
            return copied > 0 ? new string(bufferPointer, 0, copied) : null;
        }
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

    internal static IReadOnlyList<DisplayCaptureSource> CreateWindowSources(
        IEnumerable<WindowSnapshot> snapshots) => snapshots
            .Where(static window =>
                window.Handle != 0 &&
                !string.IsNullOrWhiteSpace(window.Title) &&
                window.Width > 1 &&
                window.Height > 1)
            .GroupBy(static window => window.Handle)
            .Select(static group => group.First())
            .OrderBy(static window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(static (window, index) => new DisplayCaptureSource(
                window.Handle,
                index + 1,
                window.Left,
                window.Top,
                window.Width,
                window.Height,
                false,
                CaptureSourceKind.Window,
                window.Title.Trim()))
            .ToArray();

    internal sealed record MonitorSnapshot(
        long Handle,
        int Left,
        int Top,
        int Width,
        int Height,
        bool IsPrimary);

    internal sealed record WindowSnapshot(
        long Handle,
        string Title,
        int Left,
        int Top,
        int Width,
        int Height);

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint deviceContext,
        nint monitorRectangle,
        nint data);

    private delegate bool EnumWindowsProc(nint window, nint data);

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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out NativeRectangle rectangle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    private static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true)]
    private static unsafe partial int GetWindowText(
        nint window,
        char* text,
        int maximumCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int valueSize);

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
