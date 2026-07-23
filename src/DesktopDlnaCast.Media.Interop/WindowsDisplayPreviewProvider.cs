using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Media.Interop;

public sealed partial class WindowsDisplayPreviewProvider : IDisplayPreviewProvider
{
    internal const int MaximumPreviewWidth = 240;
    internal const int MaximumPreviewHeight = 135;
    private const int DibRgbColors = 0;
    private const int SourceCopy = 0x00CC0020;
    private const int Halftone = 4;

    public Task<DisplayPreviewFrame> CaptureAsync(
        DisplayCaptureSource display,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(display);
        return Task.Run(() => Capture(display, cancellationToken), cancellationToken);
    }

    internal static (int Width, int Height) CalculatePreviewSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        double scale = Math.Min(
            (double)MaximumPreviewWidth / width,
            (double)MaximumPreviewHeight / height);
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static DisplayPreviewFrame Capture(
        DisplayCaptureSource display,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (int previewWidth, int previewHeight) = CalculatePreviewSize(display.Width, display.Height);
        nint screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not open the desktop for preview capture.");
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create a preview drawing context.");
            }

            BitmapInfo bitmapInfo = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = previewWidth,
                    Height = -previewHeight,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            bitmap = CreateDIBSection(
                memoryDc,
                in bitmapInfo,
                DibRgbColors,
                out nint pixels,
                nint.Zero,
                0);
            if (bitmap == nint.Zero || pixels == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not allocate a display preview.");
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not select the display preview bitmap.");
            }

            _ = SetStretchBltMode(memoryDc, Halftone);
            cancellationToken.ThrowIfCancellationRequested();
            if (!StretchBlt(
                    memoryDc,
                    0,
                    0,
                    previewWidth,
                    previewHeight,
                    screenDc,
                    display.Left,
                    display.Top,
                    display.Width,
                    display.Height,
                    SourceCopy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not copy the display into its preview.");
            }

            byte[] result = new byte[checked(previewWidth * previewHeight * 4)];
            Marshal.Copy(pixels, result, 0, result.Length);
            return new(previewWidth, previewHeight, result);
        }
        finally
        {
            if (previousBitmap != nint.Zero && memoryDc != nint.Zero)
            {
                _ = SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint deviceContext);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(nint deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateDIBSection(
        nint deviceContext,
        in BitmapInfo bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint deviceContext, nint value);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);

    [LibraryImport("gdi32.dll")]
    private static partial int SetStretchBltMode(nint deviceContext, int mode);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StretchBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        nint source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int rasterOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }
}
