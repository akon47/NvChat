using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NvChat.Services;

/// <summary>
/// 이미지 파일/비트맵 → (다운스케일된) data URI 변환, data URI → 표시용 ImageSource 디코딩.
/// 비전 모델에 인라인으로 넣을 수 있도록 큰 이미지는 최대 변으로 축소해 JPEG 로 인코딩한다.
/// </summary>
public static class ImageUtil
{
    private const int MaxDimension = 1024;
    private const int JpegQuality = 85;

    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    public static bool IsSupportedFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string FileToDataUri(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return BitmapToDataUri(decoder.Frames[0]);
    }

    /// <summary>클립보드/드래그로 들어온 비트맵을 data URI 로. 1024px 이 넘으면 축소한다.</summary>
    public static string BitmapToDataUri(BitmapSource source)
    {
        int longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest > MaxDimension)
        {
            double scale = (double)MaxDimension / longest;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        JpegBitmapEncoder encoder = new() { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(source));

        using MemoryStream output = new();
        encoder.Save(output);
        return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
    }

    public static ImageSource? FromDataUri(string? dataUri)
    {
        try
        {
            if (string.IsNullOrEmpty(dataUri)) return null;

            int comma = dataUri.IndexOf(',');
            byte[] bytes = Convert.FromBase64String(comma >= 0 ? dataUri[(comma + 1)..] : dataUri);

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
