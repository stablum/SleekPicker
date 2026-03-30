using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SleekPicker.App;

internal static class BlurHelper
{
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void TryEnableBlur(Window window, AppLogger logger)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var handle = helper.Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var accent = new AccentPolicy
            {
                AccentState = AccentEnableBlurBehind,
                AccentFlags = 0,
                GradientColor = unchecked((int)0x22101010),
                AnimationId = 0,
            };

            var accentSize = Marshal.SizeOf<AccentPolicy>();
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    SizeOfData = accentSize,
                    Data = accentPtr,
                };

                _ = SetWindowCompositionAttribute(handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to enable blur effect: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;

        public int AccentFlags;

        public int GradientColor;

        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;

        public IntPtr Data;

        public int SizeOfData;
    }
}
