using System.IO;
using System.Windows;
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
        return BitmapToDataUri(ApplyExifOrientation(decoder.Frames[0]));
    }

    /// <summary>WPF 디코더는 EXIF 방향 태그를 무시하므로(휴대폰 사진이 누워서 보인다) 직접 돌려 준다.</summary>
    private static BitmapSource ApplyExifOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata || !metadata.ContainsQuery("/app1/ifd/{ushort=274}")) return frame;

            double angle = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}")) switch
            {
                3 => 180,
                6 => 90,
                8 => 270,
                _ => 0,
            };
            return angle == 0 ? frame : new TransformedBitmap(frame, new RotateTransform(angle));
        }
        catch
        {
            return frame; // 메타데이터가 없거나 형식이 지원하지 않으면 그대로
        }
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
        encoder.Frames.Add(BitmapFrame.Create(FlattenAlpha(source)));

        using MemoryStream output = new();
        encoder.Save(output);
        return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    /// JPEG 에는 알파가 없어 투명 영역이 검게 나온다(투명 배경 PNG·스크린샷 캡처 도구의 결과물에서 흔하다).
    /// 알파가 있는 형식이면 흰 배경 위에 합성해 불투명 비트맵으로 바꾼다.
    /// </summary>
    private static BitmapSource FlattenAlpha(BitmapSource source)
    {
        var format = source.Format;
        bool hasAlpha = format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64
            || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float || format == PixelFormats.Indexed8 || format == PixelFormats.Indexed4
            || format == PixelFormats.Indexed2 || format == PixelFormats.Indexed1;
        if (!hasAlpha) return source;

        DrawingVisual visual = new();
        using (var context = visual.RenderOpen())
        {
            Rect bounds = new(0, 0, source.PixelWidth, source.PixelHeight);
            context.DrawRectangle(Brushes.White, null, bounds);
            context.DrawImage(source, bounds);
        }

        RenderTargetBitmap flattened = new(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        flattened.Render(visual);
        flattened.Freeze();
        return flattened;
    }

    /// <summary>data URI 를 표시용 이미지로 디코딩한다. <paramref name="decodeWidth"/> 를 주면 그 폭으로 줄여 디코딩해 메모리를 아낀다(썸네일용).</summary>
    public static ImageSource? FromDataUri(string? dataUri, int decodeWidth = 0)
    {
        try
        {
            if (string.IsNullOrEmpty(dataUri)) return null;

            int comma = dataUri.IndexOf(',');
            byte[] bytes = Convert.FromBase64String(comma >= 0 ? dataUri[(comma + 1)..] : dataUri);

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
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
