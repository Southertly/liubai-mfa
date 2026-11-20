using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

namespace LiubaiMfa.Windows.Native;

public sealed class QrImageException(string code, string message) : Exception(message)
{ public string Code { get; } = code; }

public sealed class QrImageDecoder
{
    private const int MaximumBytes = 25 * 1024 * 1024;
    public async Task<IReadOnlyList<string>> DecodeAsync(string path)
    {
        byte[]? encoded = null, pixels = null;
        try
        {
            // 同一个禁止写入/替换的文件句柄完成有界读取，避免检查后路径被替换。
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length > MaximumBytes) throw TooLarge();
            if (file.Length == 0) throw Invalid();
            encoded = new byte[checked((int)file.Length)];
            await file.ReadExactlyAsync(encoded);
            if (await file.ReadAsync(new byte[1]) != 0) throw TooLarge();
            using var memory = new MemoryStream(encoded, writable: false);
            using var random = memory.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(random);
            if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId && decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId && decoder.DecoderInformation.CodecId != BitmapDecoder.BmpDecoderId) throw Invalid();
            var width = decoder.PixelWidth; var height = decoder.PixelHeight;
            if (width == 0 || height == 0 || width > 10000 || height > 10000 || (ulong)width * height > 16_000_000) throw TooLarge();
            // 元数据检查先于像素分配，最多分配 16M 个 BGRA 像素。
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            pixels = data.DetachPixelData();
            // CPU 转换与识别另起任务，确保同步完成的 WinRT 操作也不会阻塞协议读取和锁定。
            return await Task.Run<IReadOnlyList<string>>(() =>
            {
                for (var index = 0; index < pixels.Length; index += 4)
                {
                    var alpha = pixels[index + 3];
                    for (var channel = 0; channel < 3; channel++) pixels[index + channel] = (byte)((pixels[index + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
                    pixels[index + 3] = 255;
                }
                var luminance = new RGBLuminanceSource(pixels, checked((int)width), checked((int)height), RGBLuminanceSource.BitmapFormat.BGRA32);
                try
                {
                    var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] } };
                    return reader.DecodeMultiple(luminance)?.Select(result => result.Text).ToArray() ?? [];
                }
                finally { CryptographicOperations.ZeroMemory(luminance.Matrix); }
            });
        }
        catch (QrImageException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or OverflowException)
        { throw Invalid(); }
        finally
        {
            if (encoded is not null) CryptographicOperations.ZeroMemory(encoded);
            if (pixels is not null) CryptographicOperations.ZeroMemory(pixels);
        }
    }
    private static QrImageException Invalid() => new("QR_IMAGE_INVALID", "无法读取图片，请选择 PNG、JPEG 或 BMP 图片。");
    private static QrImageException TooLarge() => new("QR_TOO_LARGE", "图片过大，请使用不超过 25 MiB、1600 万像素且边长不超过 10000 的图片。");
}
