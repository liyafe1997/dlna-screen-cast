using System.Runtime.InteropServices;

namespace DesktopDlnaCast.Media.Interop.Native;

internal static class NativeMethods
{
    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_create(
        in NativeStreamConfiguration configuration,
        out NativeMediaSessionHandle result);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_start(NativeMediaSessionHandle handle);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_read(
        NativeMediaSessionHandle handle,
        [Out] byte[] buffer,
        int bufferCapacity,
        out int bytesWritten,
        out long timestamp100Nanoseconds,
        out int packetFlags,
        uint timeoutMilliseconds);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_get_statistics(
        NativeMediaSessionHandle handle,
        ref NativeSessionStatistics statistics);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_get_encoder_diagnostics(
        NativeMediaSessionHandle handle,
        ref NativeEncoderDiagnostics diagnostics,
        [Out] byte[] encoderNameUtf8,
        int encoderNameCapacity,
        out int encoderNameBytesWritten);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_stop(NativeMediaSessionHandle handle);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeResult ddc_session_get_last_error(
        NativeMediaSessionHandle handle,
        [Out] byte[] utf8Buffer,
        int bufferCapacity,
        out int bytesWritten);

    [DllImport(NativeAbi.LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ddc_session_destroy(IntPtr handle);
}
