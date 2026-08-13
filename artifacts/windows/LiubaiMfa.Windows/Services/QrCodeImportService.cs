using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

namespace LiubaiMfa.Windows.Services;

public static class QrCodeImportService
{
    public static async Task<string?> DecodeAsync(StorageFile file)
    {
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 25 * 1024 * 1024)
        {
            throw new System.FormatException("二维码图片过大。");
        }

        using var fileStream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(fileStream);
        if (decoder.PixelWidth == 0 ||
            decoder.PixelHeight == 0 ||
            decoder.PixelWidth > 10_000 ||
            decoder.PixelHeight > 10_000 ||
            decoder.PixelWidth * decoder.PixelHeight > 40_000_000)
        {
            throw new System.FormatException("二维码图片尺寸无效或过大。");
        }
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var pixels = pixelData.DetachPixelData();
        var luminance = new RGBLuminanceSource(
            pixels,
            checked((int)decoder.PixelWidth),
            checked((int)decoder.PixelHeight),
            RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE },
            },
        };
        return reader.Decode(luminance)?.Text;
    }
}
