using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SleekPicker.App;

internal static class WindowIconProvider
{
    public static ImageSource? GetIcon(IntPtr hWnd, string? executablePath)
    {
        var iconHandle = GetWindowIconHandle(hWnd);
        if (iconHandle != IntPtr.Zero)
        {
            var image = CreateImageSource(iconHandle);
            if (image is not null)
            {
                return image;
            }
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            return CreateImageSource(icon.Handle);
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr GetWindowIconHandle(IntPtr hWnd)
    {
        var iconHandle = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_SMALL2), IntPtr.Zero);
        if (iconHandle != IntPtr.Zero)
        {
            return iconHandle;
        }

        iconHandle = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_SMALL), IntPtr.Zero);
        if (iconHandle != IntPtr.Zero)
        {
            return iconHandle;
        }

        iconHandle = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_BIG), IntPtr.Zero);
        if (iconHandle != IntPtr.Zero)
        {
            return iconHandle;
        }

        iconHandle = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICONSM);
        if (iconHandle != IntPtr.Zero)
        {
            return iconHandle;
        }

        return NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICON);
    }

    private static ImageSource? CreateImageSource(IntPtr iconHandle)
    {
        if (iconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(20, 20));

            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
