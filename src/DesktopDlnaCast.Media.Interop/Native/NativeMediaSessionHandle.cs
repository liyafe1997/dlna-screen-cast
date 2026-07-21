using Microsoft.Win32.SafeHandles;

namespace DesktopDlnaCast.Media.Interop.Native;

internal sealed class NativeMediaSessionHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeMediaSessionHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            NativeMethods.ddc_session_destroy(handle);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
