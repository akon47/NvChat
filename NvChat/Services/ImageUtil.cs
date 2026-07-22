using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NvChat.Services
{
    /// <summary>
    /// 이미지 파일 → (다운스케일된) data URI 변환, data URI → 표시용 ImageSource 디코딩.
    /// 비전 모델에 인라인으로 넣을 수 있도록 큰 이미지는 최대 변으로 축소해 JPEG 로 인코딩한다.
    /// </summary>
    public static class ImageUtil
    {
        private const int MaxDimension = 1024;

        public static string FileToDataUri(string path)
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = decoder.Frames[0];

            var longest = Math.Max(source.PixelWidth, source.PixelHeight);
            if (longest > MaxDimension)
            {
                var scale = (double)MaxDimension / longest;
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var output = new MemoryStream();
            encoder.Save(output);
            return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
        }

        public static ImageSource FromDataUri(string dataUri)
        {
            try
            {
                if (string.IsNullOrEmpty(dataUri))
                    return null;

                var comma = dataUri.IndexOf(',');
                var base64 = comma >= 0 ? dataUri.Substring(comma + 1) : dataUri;
                var bytes = Convert.FromBase64String(base64);

                var image = new BitmapImage();
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
}
