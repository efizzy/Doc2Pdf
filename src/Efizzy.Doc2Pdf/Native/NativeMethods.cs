using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Efizzy.Doc2Pdf.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeOptions
{
    [MarshalAs(UnmanagedType.I1)] public bool IncludeMarkup;
    [MarshalAs(UnmanagedType.I1)] public bool Comments;
    [MarshalAs(UnmanagedType.I1)] public bool Tagged;
    [MarshalAs(UnmanagedType.I1)] public bool Compress;
    public float PaperWidthPt;
    public float PaperHeightPt;
    public int NumeralMode;
    public IntPtr Title;
    public IntPtr Author;
    public IntPtr Password;
    public IntPtr Subject;
    public IntPtr Keywords;
    public IntPtr WatermarkText;
    public IntPtr WatermarkColor;
    [MarshalAs(UnmanagedType.I1)] public bool WatermarkDiagonal;
    public float WatermarkOpacity;
    public IntPtr PageRange;
    public IntPtr CustomFontsDir;
}

internal ref struct Utf8Scope
{
    private IntPtr _ptr;

    public unsafe byte* Pointer => (byte*)_ptr;

    public Utf8Scope(string? text)
    {
        if (text == null)
        {
            _ptr = IntPtr.Zero;
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        _ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, _ptr, bytes.Length);
        Marshal.WriteByte(_ptr, bytes.Length, 0);
    }

    public void Dispose()
    {
        if (_ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_ptr);
            _ptr = IntPtr.Zero;
        }
    }
}

internal static class NativeMethods
{
    public const string LibraryName = "doc2pdf_native";

    static NativeMethods()
    {
        NativeLibraryResolver.Initialize();
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int doc2pdf_convert_buffer(
        byte* inBytes,
        nuint inLen,
        byte* formatHint,
        in NativeOptions options,
        out byte* outBytes,
        out nuint outLen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int doc2pdf_convert_file(
        byte* inPath,
        byte* outPath,
        in NativeOptions options);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe void doc2pdf_free_buffer(byte* ptr, nuint len);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int doc2pdf_get_last_error(byte* buffer, nuint maxLen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int doc2pdf_is_format_supported(byte* ext);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr doc2pdf_version();

    public static unsafe string GetLastError()
    {
        byte[] buf = new byte[2048];
        fixed (byte* p = buf)
        {
            if (doc2pdf_get_last_error(p, (nuint)buf.Length) >= 0)
            {
                int len = 0;
                while (len < buf.Length && buf[len] != 0) len++;
                return Encoding.UTF8.GetString(buf, 0, len);
            }
        }
        return "Unknown error";
    }

    public static unsafe string GetVersion()
    {
        IntPtr ptr = doc2pdf_version();
        if (ptr == IntPtr.Zero) return "1.1.0";
        byte* p = (byte*)ptr;
        int len = 0;
        while (p[len] != 0) len++;
        return Encoding.UTF8.GetString(p, len);
    }

    public static unsafe bool IsFormatSupported(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        using var scope = new Utf8Scope(extension);
        return doc2pdf_is_format_supported(scope.Pointer) != 0;
    }
}
