using System.Buffers.Binary;
using System.Runtime.InteropServices;
using static NeonCompanion.UI.ConsoleInputNative;

namespace NeonCompanion.UI;

/// <summary>
/// The clipboard as text, both ways, and as a picture, read only. <see cref="TryReadText"/> and
/// <see cref="TryReadImage"/> are the input line's own paste — a right click, Ctrl+V where the
/// terminal lets it through, Alt+V — and <see cref="TrySetText"/> is <c>/copy</c>. Raw Win32 (no
/// STA, no WinForms): open, read or write, close. Nothing here throws; a busy clipboard (another
/// process holds it open for a moment) reads as null and writes as false — the next try will do.
/// </summary>
public static unsafe class WindowsClipboard
{
    /// <summary>The 14-byte <c>BITMAPFILEHEADER</c> a <c>CF_DIB</c> block lacks.</summary>
    public const int BmpFileHeaderLength = 14;

    /// <summary>The smallest DIB header (<c>BITMAPINFOHEADER</c>); V4 and V5 are longer and say so in their first field.</summary>
    public const int DibInfoHeaderLength = 40;

    private const int BiBitFields = 3;

    // Registered once per process; 0 when the registration failed, which never matches a format.
    private static readonly uint PngFormat = OperatingSystem.IsWindows() ? RegisterClipboardFormatW(ClipboardPngFormatName) : 0;

    public static string? TryReadText()
    {
        if (!OperatingSystem.IsWindows() || !OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            if (!IsClipboardFormatAvailable(ClipboardUnicodeText))
            {
                return null;
            }

            IntPtr data = GetClipboardData(ClipboardUnicodeText);
            if (data == IntPtr.Zero)
            {
                return null;
            }

            IntPtr text = GlobalLock(data);
            if (text == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(text);
            }
            finally
            {
                GlobalUnlock(data);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// The picture on the clipboard as the bytes of an image file — what the codecs decode — or
    /// null when there is none (or the clipboard is busy). A <c>PNG</c> block (a screenshot, a
    /// browser's Copy image) is taken as it is, lossless and with its alpha; otherwise the
    /// <c>CF_DIB</c> the system synthesises for every bitmap becomes a BMP through
    /// <see cref="BmpFromDib"/>. No size cap here: <see cref="Files.ImageFile"/> judges the bytes.
    /// </summary>
    public static byte[]? TryReadImage()
    {
        if (!OperatingSystem.IsWindows() || !OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            if (PngFormat != 0 && IsClipboardFormatAvailable(PngFormat) && ReadBlock(PngFormat) is { } png)
            {
                return png;
            }

            if (IsClipboardFormatAvailable(ClipboardDib) && ReadBlock(ClipboardDib) is { } dib)
            {
                return BmpFromDib(dib);
            }

            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // A copy of the whole global block behind a format, null when the handle is bad or empty.
    private static byte[]? ReadBlock(uint format)
    {
        IntPtr data = GetClipboardData(format);
        if (data == IntPtr.Zero)
        {
            return null;
        }

        nuint size = GlobalSize(data);
        if (size == 0 || size > int.MaxValue)
        {
            return null;
        }

        IntPtr bytes = GlobalLock(data);
        if (bytes == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return new ReadOnlySpan<byte>((void*)bytes, (int)size).ToArray();
        }
        finally
        {
            GlobalUnlock(data);
        }
    }

    /// <summary>
    /// A <c>CF_DIB</c> block as a BMP file: the 14-byte file header in front, its pixel offset
    /// past the info header, the three colour masks a 40-byte header with <c>BI_BITFIELDS</c>
    /// keeps after itself (a V4/V5 header holds them inside its own length), and the colour
    /// table (<c>biClrUsed</c> entries, or every one of a palette depth when that is 0). Null
    /// for a block too short to be a DIB or one whose header claims more than the block holds.
    /// Pure; pinned.
    /// </summary>
    public static byte[]? BmpFromDib(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < DibInfoHeaderLength)
        {
            return null;
        }

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(dib);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
        int coloursUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        if (headerLength < DibInfoHeaderLength || headerLength > dib.Length || coloursUsed < 0)
        {
            return null;
        }

        int masks = headerLength == DibInfoHeaderLength && compression == BiBitFields ? 12 : 0;
        int colours = coloursUsed > 0 ? coloursUsed : bitCount is > 0 and <= 8 ? 1 << bitCount : 0;
        long pixelOffset = (long)BmpFileHeaderLength + headerLength + masks + (long)colours * 4;
        if (pixelOffset > BmpFileHeaderLength + dib.Length)
        {
            return null;
        }

        var bmp = new byte[BmpFileHeaderLength + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), (int)pixelOffset);
        dib.CopyTo(bmp.AsSpan(BmpFileHeaderLength));
        return bmp;
    }

    /// <summary>
    /// Replaces the clipboard's contents with <paramref name="text"/>. The memory is a moveable
    /// global block the system takes ownership of on a successful <c>SetClipboardData</c>; it is
    /// freed here only on the failure paths. False when the clipboard could not be opened or
    /// the write failed.
    /// </summary>
    public static bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!OperatingSystem.IsWindows() || !OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            // The characters and the terminating NUL, in UTF-16 as CF_UNICODETEXT expects.
            nuint bytes = (nuint)(text.Length + 1) * sizeof(char);
            IntPtr block = GlobalAlloc(GlobalMoveable, bytes);
            if (block == IntPtr.Zero)
            {
                return false;
            }

            IntPtr target = GlobalLock(block);
            if (target == IntPtr.Zero)
            {
                GlobalFree(block);
                return false;
            }

            var span = new Span<char>((void*)target, text.Length + 1);
            text.AsSpan().CopyTo(span);
            span[text.Length] = (char)0;
            GlobalUnlock(block);

            if (SetClipboardData(ClipboardUnicodeText, block) == IntPtr.Zero)
            {
                GlobalFree(block);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }
}
