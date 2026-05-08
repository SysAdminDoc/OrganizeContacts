using System.Buffers.Binary;
using OrganizeContacts.Core.Models;

namespace OrganizeContacts.Core.Photos;

/// <summary>
/// Minimal-dep EXIF/metadata stripper for JPEG and PNG.
/// Walks the byte format directly so no image-decoding dependency is required.
/// JPEG: drops APP1..APP15 marker segments (EXIF, XMP, IPTC). Keeps APP0/JFIF and image data.
/// PNG : drops ancillary chunks tEXt, iTXt, zTXt, eXIf, time, gAMA, cHRM, iCCP, sRGB.
/// Other formats are returned unchanged.
/// </summary>
public static class PhotoSanitizer
{
    /// <summary>Maximum decompressed photo size we'll accept inline. Above this we drop the photo.</summary>
    public const int MaxPhotoBytes = 4 * 1024 * 1024;

    public static byte[] StripMetadata(byte[] input, string? mime = null)
    {
        if (input is null || input.Length == 0) return Array.Empty<byte>();
        if (input.Length > MaxPhotoBytes) return Array.Empty<byte>();

        if (IsJpeg(input)) return StripJpeg(input);
        if (IsPng(input)) return StripPng(input);
        return input;
    }

    /// <summary>Strip metadata from every contact's photo. Returns the count actually changed.</summary>
    public static int Apply(IEnumerable<Contact> contacts)
    {
        var changed = 0;
        foreach (var c in contacts)
        {
            if (c.PhotoBytes is null || c.PhotoBytes.Length == 0) continue;
            var stripped = StripMetadata(c.PhotoBytes, c.PhotoMimeType);
            if (stripped.Length == 0)
            {
                c.PhotoBytes = null;
                c.PhotoMimeType = null;
                changed++;
                continue;
            }
            if (stripped.Length != c.PhotoBytes.Length)
            {
                c.PhotoBytes = stripped;
                changed++;
            }
        }
        return changed;
    }

    public static bool IsJpeg(byte[] b) => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
    public static bool IsPng(byte[] b)
    {
        if (b.Length < 8) return false;
        return b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
               b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
    }

    /// <summary>Walk JPEG markers; copy everything except APP1..APP15 + COM segments.</summary>
    private static byte[] StripJpeg(byte[] input)
    {
        using var ms = new MemoryStream(input.Length);
        ms.WriteByte(0xFF); ms.WriteByte(0xD8);
        var i = 2;
        while (i + 1 < input.Length)
        {
            if (input[i] != 0xFF) break;
            byte marker = input[i + 1];
            i += 2;
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                ms.WriteByte(0xFF);
                ms.WriteByte(marker);
                if (marker == 0xD9) // EOI — done
                    return ms.ToArray();
                continue;
            }
            if (marker == 0xDA) // SOS — image data follows; copy everything to EOI
            {
                ms.WriteByte(0xFF);
                ms.WriteByte(marker);
                ms.Write(input, i, input.Length - i);
                return ms.ToArray();
            }
            if (i + 1 >= input.Length) break;
            int segLen = (input[i] << 8) | input[i + 1];
            if (segLen < 2 || i + segLen > input.Length) break;

            // Drop APPn (1..15) + COM. Keep APP0 (JFIF) and everything else (frame, quantization, huffman).
            var drop = (marker >= 0xE1 && marker <= 0xEF) || marker == 0xFE;
            if (!drop)
            {
                ms.WriteByte(0xFF);
                ms.WriteByte(marker);
                ms.Write(input, i, segLen);
            }
            i += segLen;
        }
        return ms.ToArray();
    }

    /// <summary>Walk PNG chunks; copy critical chunks + IDAT/PLTE; drop ancillary metadata.</summary>
    private static byte[] StripPng(byte[] input)
    {
        using var ms = new MemoryStream(input.Length);
        ms.Write(input, 0, 8);
        var i = 8;
        while (i + 8 <= input.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(i, 4));
            string type = System.Text.Encoding.ASCII.GetString(input, i + 4, 4);
            int total = 4 + 4 + (int)length + 4; // length + type + data + crc
            if (i + total > input.Length) break;

            if (KeepPngChunk(type))
            {
                ms.Write(input, i, total);
            }
            i += total;

            if (type == "IEND") break;
        }
        return ms.ToArray();
    }

    private static bool KeepPngChunk(string type) => type switch
    {
        "IHDR" or "PLTE" or "IDAT" or "IEND" or "tRNS" or "bKGD" or "pHYs" or "sBIT" or "hIST" => true,
        // Drop metadata-bearing ancillary chunks
        "tEXt" or "iTXt" or "zTXt" or "tIME" or "eXIf" or "gAMA" or "cHRM" or "iCCP" or "sRGB" => false,
        // Anything else: keep, lowercase-first-letter ancillary chunks (PNG spec) so safe to keep is unknown,
        // but we err on the side of preserving display fidelity.
        _ => char.IsUpper(type[0]),
    };
}
