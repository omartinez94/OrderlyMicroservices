using System.Buffers.Binary;

namespace Catalog.API.Features.MenuItems.UploadMenuItemImage;

public sealed record ImageValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? DetectedFormat = null,
    int Width = 0,
    int Height = 0);

/// <summary>
/// Validates uploaded image files by inspecting file extension, content length,
/// MIME type, magic bytes, and header-encoded dimensions without decoding pixels.
/// </summary>
public static class ImageHeaderValidator
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    public const int MaxDimensionPixels = 8000;
    public const int MinDimensionPixels = 1;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
    };

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    public static async Task<ImageValidationResult> ValidateAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return new ImageValidationResult(false, "No file was provided or file is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return new ImageValidationResult(
                false,
                $"File size ({file.Length} bytes) exceeds the maximum allowed limit of {MaxFileSizeBytes} bytes (5 MB).");
        }

        string extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            return new ImageValidationResult(
                false,
                $"File extension '{extension}' is not allowed. Supported extensions: .jpg, .jpeg, .png, .webp.");
        }

        if (!AllowedMimeTypes.Contains(file.ContentType))
        {
            return new ImageValidationResult(
                false,
                $"Content-Type '{file.ContentType}' is not permitted. Supported MIME types: image/jpeg, image/png, image/webp.");
        }

        // Sniff header bytes (up to 64 KB is enough to locate dimension headers in virtually any JPEG/PNG/WebP)
        byte[] buffer = new byte[Math.Min(65536, file.Length)];
        await using Stream stream = file.OpenReadStream();
        int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);

        if (bytesRead < 16)
        {
            return new ImageValidationResult(false, "Image file header is truncated or invalid.");
        }

        ReadOnlySpan<byte> header = buffer.AsSpan(0, bytesRead);

        if (IsPng(header))
        {
            return ValidatePng(header);
        }

        if (IsJpeg(header))
        {
            return ValidateJpeg(header);
        }

        if (IsWebp(header))
        {
            return ValidateWebp(header);
        }

        return new ImageValidationResult(false, "Image magic bytes do not match any permitted image format (JPEG, PNG, WebP).");
    }

    private static bool IsPng(ReadOnlySpan<byte> header)
    {
        return header.Length >= 8
            && header[0] == 0x89
            && header[1] == 0x50
            && header[2] == 0x4E
            && header[3] == 0x47
            && header[4] == 0x0D
            && header[5] == 0x0A
            && header[6] == 0x1A
            && header[7] == 0x0A;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> header)
    {
        return header.Length >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;
    }

    private static bool IsWebp(ReadOnlySpan<byte> header)
    {
        return header.Length >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 // RIFF
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50; // WEBP
    }

    private static ImageValidationResult ValidatePng(ReadOnlySpan<byte> header)
    {
        // PNG IHDR chunk is located at bytes 8-23
        if (header.Length < 24)
        {
            return new ImageValidationResult(false, "PNG IHDR chunk header is truncated.");
        }

        if (header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R')
        {
            return new ImageValidationResult(false, "Invalid PNG structure: missing initial IHDR chunk.");
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));

        return CheckDimensions(width, height, "PNG");
    }

    private static ImageValidationResult ValidateJpeg(ReadOnlySpan<byte> header)
    {
        int offset = 2; // skip 0xFF 0xD8

        while (offset < header.Length - 8)
        {
            if (header[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            byte marker = header[offset + 1];

            // SOS (Start of Scan) or EOI (End of Image) reached without finding SOF
            if (marker == 0xDA || marker == 0xD9)
            {
                break;
            }

            // SOF markers (baseline, progressive, extended)
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (offset + 8 >= header.Length)
                {
                    break;
                }

                int height = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(offset + 5, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(offset + 7, 2));

                return CheckDimensions(width, height, "JPEG");
            }

            // Skip marker by reading length
            if (offset + 3 >= header.Length)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(offset + 2, 2));
            if (length < 2)
            {
                break;
            }

            offset += 2 + length;
        }

        return new ImageValidationResult(false, "Could not determine dimensions from JPEG SOF markers.");
    }

    private static ImageValidationResult ValidateWebp(ReadOnlySpan<byte> header)
    {
        if (header.Length < 30)
        {
            return new ImageValidationResult(false, "WebP header is truncated.");
        }

        string format = System.Text.Encoding.ASCII.GetString(header.Slice(12, 4));

        if (format == "VP8 ")
        {
            if (header.Length >= 30 && header[23] == 0x9D && header[24] == 0x01 && header[25] == 0x2A)
            {
                int width = (header[26] | (header[27] << 8)) & 0x3FFF;
                int height = (header[28] | (header[29] << 8)) & 0x3FFF;
                return CheckDimensions(width, height, "WebP (VP8)");
            }
        }
        else if (format == "VP8L")
        {
            if (header.Length >= 25 && header[20] == 0x2F)
            {
                uint b0 = header[21];
                uint b1 = header[22];
                uint b2 = header[23];
                uint b3 = header[24];

                int width = (int)(1 + (b0 | ((b1 & 0x3F) << 8)));
                int height = (int)(1 + (((b1 >> 6) | (b2 << 2) | ((b3 & 0x0F) << 10))));
                return CheckDimensions(width, height, "WebP (VP8L)");
            }
        }
        else if (format == "VP8X")
        {
            if (header.Length >= 30)
            {
                int width = 1 + (header[24] | (header[25] << 8) | (header[26] << 16));
                int height = 1 + (header[27] | (header[28] << 8) | (header[29] << 16));
                return CheckDimensions(width, height, "WebP (VP8X)");
            }
        }

        return new ImageValidationResult(false, "Unsupported or malformed WebP format chunk.");
    }

    private static ImageValidationResult CheckDimensions(int width, int height, string format)
    {
        if (width < MinDimensionPixels || height < MinDimensionPixels)
        {
            return new ImageValidationResult(
                false,
                $"Image dimensions ({width}x{height}) must be at least {MinDimensionPixels}x{MinDimensionPixels} pixels.",
                format,
                width,
                height);
        }

        if (width > MaxDimensionPixels || height > MaxDimensionPixels)
        {
            return new ImageValidationResult(
                false,
                $"Image dimensions ({width}x{height}) exceed maximum allowed dimensions of {MaxDimensionPixels}x{MaxDimensionPixels} pixels.",
                format,
                width,
                height);
        }

        return new ImageValidationResult(true, null, format, width, height);
    }
}
