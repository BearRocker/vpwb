using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WukongBench;

/// <summary>Одно распознанное слово с рамкой в пикселях изображения.</summary>
public record OcrWord(string Text, double X, double Y, double W, double H)
{
    public double Cy => Y + H / 2;
}

public static class Ocr
{
    static readonly Dictionary<string, OcrEngine> Engines = new();

    // Движки Windows OCR привязаны к языку; русский читает и латиницу с цифрами, поэтому он в приоритете
    // (игра следует языку Steam, на таких машинах он русский).
    static OcrEngine Engine(string tag)
    {
        if (!Engines.TryGetValue(tag, out var e))
            Engines[tag] = e = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag))
                               ?? OcrEngine.TryCreateFromUserProfileLanguages()
                               ?? throw new InvalidOperationException("Windows OCR engine is not available (install an OCR language pack)");
        return e;
    }

    public static async Task<List<OcrWord>> ReadWordsAsync(Bitmap bmp, string lang = "ru")
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(ms.ToArray().AsBuffer());
        ras.Seek(0);
        var dec = await BitmapDecoder.CreateAsync(ras);
        using var sb = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var res = await Engine(lang).RecognizeAsync(sb);
        return res.Lines.SelectMany(l => l.Words)
            .Select(w => new OcrWord(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height))
            .ToList();
    }

    /// <summary>OCR увеличенной области (крупные цифры на пёстром фоне иначе легко теряются); координаты пересчитываются в исходное изображение.</summary>
    public static async Task<List<OcrWord>> ReadRegionAsync(Bitmap src, Rectangle region, double scale, string lang = "ru")
    {
        using var crop = new Bitmap((int)(region.Width * scale), (int)(region.Height * scale));
        using (var g = Graphics.FromImage(crop))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(0, 0, crop.Width, crop.Height), region, GraphicsUnit.Pixel);
        }
        var words = await ReadWordsAsync(crop, lang);
        return words.Select(w => new OcrWord(w.Text, region.X + w.X / scale, region.Y + w.Y / scale, w.W / scale, w.H / scale)).ToList();
    }

    /// <summary>Светлый текст на тёмной плашке -> чёрный на белом: так Windows OCR читает заметно надёжнее.</summary>
    public static Bitmap Binarize(Bitmap src, int threshold)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        var r = new Rectangle(0, 0, src.Width, src.Height);
        var sd = src.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dd = dst.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[sd.Stride * sd.Height];
        System.Runtime.InteropServices.Marshal.Copy(sd.Scan0, buf, 0, buf.Length);
        for (int i = 0; i < buf.Length; i += 4)
        {
            double lum = 0.114 * buf[i] + 0.587 * buf[i + 1] + 0.299 * buf[i + 2];
            byte v = lum > threshold ? (byte)0 : (byte)255;
            buf[i] = buf[i + 1] = buf[i + 2] = v; buf[i + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, dd.Scan0, buf.Length);
        src.UnlockBits(sd); dst.UnlockBits(dd);
        return dst;
    }

    /// <summary>
    /// OCR всего кадра. Левая колонка (результаты) перечитывается в увеличенном виде, затем бинаризуется с разными
    /// порогами, пока не выполнится <paramref name="accept"/>; первый принятый вариант заменяет слова колонки.
    /// </summary>
    public static async Task<List<OcrWord>> ReadScreenAsync(Bitmap bmp, Func<List<OcrWord>, bool>? accept = null)
    {
        var full = await ReadWordsAsync(bmp);
        var left = new Rectangle(0, (int)(bmp.Height * 0.18), (int)(bmp.Width * 0.30), (int)(bmp.Height * 0.46));
        List<OcrWord> Merge(List<OcrWord> col) =>
            full.Where(w => w.X >= left.Right || w.Y < left.Top || w.Y > left.Bottom).Concat(col).ToList();

        var best = Merge(await ReadRegionAsync(bmp, left, 2.5));
        if (accept == null || accept(best)) return best;
        // Бинаризованные проходы находят крупные цифры, которые обычный проход пропускает; добавляем только слова, не пересекающиеся с уже найденными.
        using var crop = bmp.Clone(left, PixelFormat.Format32bppArgb);
        foreach (var thr in new[] { 100, 130, 90, 150, 110, 170, 120, 180, 80 })
            foreach (var scale in new[] { 1.0, 2.0 })
            {
                using var bin = Binarize(crop, thr);
                var extra = (await ReadRegionAsync(bin, new Rectangle(0, 0, bin.Width, bin.Height), scale))
                    .Select(w => new OcrWord(w.Text, w.X + left.X, w.Y + left.Y, w.W, w.H))
                    .Where(w => !best.Any(b => w.X < b.X + b.W && b.X < w.X + w.W && w.Y < b.Y + b.H && b.Y < w.Y + w.H));
                var merged = best.Concat(extra).ToList();
                if (accept(merged)) return merged;
            }
        return best;
    }
}
