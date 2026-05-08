using OrganizeContacts.Core.Photos;

namespace OrganizeContacts.Tests;

public class PhotoSanitizerTests
{
    [Fact]
    public void Returns_empty_for_empty_input()
    {
        Assert.Empty(PhotoSanitizer.StripMetadata(Array.Empty<byte>()));
    }

    [Fact]
    public void Returns_unchanged_for_unknown_format()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var result = PhotoSanitizer.StripMetadata(bytes);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public void Detects_jpeg_signature()
    {
        Assert.True(PhotoSanitizer.IsJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.False(PhotoSanitizer.IsJpeg(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Fact]
    public void Detects_png_signature()
    {
        Assert.True(PhotoSanitizer.IsPng(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
    }

    [Fact]
    public void Strips_jpeg_app1_exif_segment()
    {
        // Synthetic JPEG with SOI + APP1(EXIF) + SOS payload + EOI
        var jpeg = new List<byte> { 0xFF, 0xD8 };

        // APP1 marker, length 12 (incl length bytes), "Exif\0\0" + 4 bytes payload
        jpeg.AddRange(new byte[] { 0xFF, 0xE1, 0x00, 0x0E });
        jpeg.AddRange(System.Text.Encoding.ASCII.GetBytes("Exif\0\0"));
        jpeg.AddRange(new byte[] { 0x01, 0x02, 0x03, 0x04 });

        // APP0 marker, length 16, "JFIF\0" + 9 bytes
        jpeg.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
        jpeg.AddRange(System.Text.Encoding.ASCII.GetBytes("JFIF\0"));
        jpeg.AddRange(new byte[] { 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });

        // SOS + minimal payload + EOI
        jpeg.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0x42, 0x42, 0xFF, 0xD9 });

        var stripped = PhotoSanitizer.StripMetadata(jpeg.ToArray(), "image/jpeg");
        Assert.NotEmpty(stripped);
        // Output must keep SOI, APP0, SOS data, EOI but drop APP1.
        Assert.True(stripped.Length < jpeg.Count, "Expected output to be smaller than input");
        // Confirm APP1 marker is gone in the head bytes.
        var head = stripped.Take(20).ToList();
        for (int i = 0; i < head.Count - 1; i++)
        {
            Assert.False(head[i] == 0xFF && head[i + 1] == 0xE1, "APP1 marker should be stripped");
        }
    }

    [Fact]
    public void Strips_png_text_chunks()
    {
        // PNG signature + IHDR + tEXt + IEND
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // IHDR chunk: length 13, type "IHDR", 13 bytes data, 4 byte crc (zeroed for test)
        png.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D });
        png.AddRange(System.Text.Encoding.ASCII.GetBytes("IHDR"));
        for (int i = 0; i < 13; i++) png.Add(0);
        for (int i = 0; i < 4; i++) png.Add(0);

        // tEXt chunk: length 5, type "tEXt", "hello", crc 0x00000000
        png.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x05 });
        png.AddRange(System.Text.Encoding.ASCII.GetBytes("tEXt"));
        png.AddRange(System.Text.Encoding.ASCII.GetBytes("hello"));
        for (int i = 0; i < 4; i++) png.Add(0);

        // IEND
        png.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        png.AddRange(System.Text.Encoding.ASCII.GetBytes("IEND"));
        for (int i = 0; i < 4; i++) png.Add(0);

        var stripped = PhotoSanitizer.StripMetadata(png.ToArray(), "image/png");
        Assert.NotEmpty(stripped);
        var asString = System.Text.Encoding.ASCII.GetString(stripped);
        Assert.DoesNotContain("tEXt", asString);
        Assert.DoesNotContain("hello", asString);
        Assert.Contains("IHDR", asString);
        Assert.Contains("IEND", asString);
    }
}
