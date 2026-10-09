using System.Globalization;
using System.Text.RegularExpressions;

namespace WukongBench;

/// <summary>
/// Разбирает экран результатов Benchmark Tool по словам OCR. Разбор позиционный и не зависит от языка:
/// в левой колонке значения FPS идут в фиксированном порядке (среднее, максимум, минимум, 5-й перцентиль), затем VRAM.
/// </summary>
public static class ResultParser
{
    static readonly Regex Fps = new(@"(\d{1,4}(?:[.,]\d{1,2})?)\s*F[P]?[S5]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Vram = new(@"(\d{1,3}(?:[.,]\d)?)\s*(?:г[бb6]|gb|g8)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex BareNumber = new(@"^(\d{1,4}(?:[.,]\d{1,2})?)(?:\s*\p{L}{1,4})?$", RegexOptions.Compiled);

    static double Num(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    public static List<(double y, string text)> Rows(IEnumerable<OcrWord> words, Func<OcrWord, bool> filter)
    {
        var rows = new List<(double cy, double h, List<OcrWord> ws)>();
        foreach (var w in words.Where(filter).OrderBy(w => w.Cy))
        {
            var r = rows.Count > 0 ? rows[^1] : default;
            if (rows.Count > 0 && Math.Abs(w.Cy - r.cy) < 0.5 * Math.Max(r.h, w.H)) r.ws.Add(w);
            else rows.Add((w.Cy, w.H, new List<OcrWord> { w }));
        }
        return rows.Select(r => (r.cy, string.Join(" ", r.ws.OrderBy(w => w.X).Select(w => w.Text)))).ToList();
    }

    /// <returns>метрики, если экран результатов на виду, иначе null.</returns>
    public static (BenchMetrics? metrics, List<string> toolSettings) ParseResultScreen(List<OcrWord> words, double imgW, double imgH)
    {
        var left = Rows(words, w => w.X < 0.30 * imgW);
        var fps = new List<double>();
        double? vram = null;
        double firstFpsRowY = -1;
        foreach (var (y, text) in left)
        {
            var ms = Fps.Matches(text);
            if (ms.Count > 0 && firstFpsRowY < 0) firstFpsRowY = y;
            foreach (Match m in ms) fps.Add(Num(m.Groups[1].Value));
            if (fps.Count >= 3 && vram == null && Vram.Match(text) is { Success: true } v) vram = Num(v.Groups[1].Value);
        }

        // У крупного среднего значения OCR часто теряет суффикс «FPS» (или разрывает число: «1 79»):
        // принимаем «голое» число в полосе чуть выше строки максимум/минимум.
        if (fps.Count == 3 && firstFpsRowY > 0)
        {
            foreach (var (y, text) in left)
            {
                if (y < firstFpsRowY - 0.14 * imgH || y > firstFpsRowY - 0.05 * imgH) continue;
                var t = Regex.IsMatch(text, @"^\d+(\s\d+)+$") ? text.Replace(" ", "") : text.Trim();
                // «179», «1 79» или число с искажённой единицей измерения («36 vps»)
                if (BareNumber.Match(t) is { Success: true } b) { fps.Insert(0, Num(b.Groups[1].Value)); break; }
            }
        }

        var settings = SettingsPairs(words, imgW, imgH);
        // Порядок чтения: среднее, максимум, минимум, 5-й перцентиль. Любое число значений, кроме четырёх, значит,
        // что символ пропущен: сообщаем «не распознано», и вызывающий код перечитает экран.
        if (fps.Count != 4 || fps.Any(f => f <= 0 || f > 5000)) return (null, settings);
        var (avg, max, min, p5) = (fps[0], fps[1], fps[2], fps[3]);
        if (!(max >= avg && avg >= p5 && p5 >= min)) return (null, settings);   // проверка здравого смысла: max >= avg >= p5 >= min
        return (new BenchMetrics { AvgFps = avg, MaxFps = max, MinFps = min, P5Fps = p5, VramGb = vram }, settings);
    }

    /// <summary>В правой панели две колонки пар строк «название / значение»; возвращает их в виде «название: значение».</summary>
    static List<string> SettingsPairs(List<OcrWord> words, double imgW, double imgH)
    {
        var res = new List<string>();
        foreach (var (lo, hi) in new[] { (0.58, 0.735), (0.735, 1.0) })
        {
            var rows = Rows(words, w => w.X >= lo * imgW && w.X < hi * imgW && w.Cy < 0.90 * imgH && w.Cy > 0.24 * imgH)
                .Select(r => r.text).ToList();
            if (lo < 0.6 && rows.Count > 0) rows.RemoveAt(0);          // заголовок панели («Настройки графики»)
            for (int i = 0; i + 1 < rows.Count; i += 2) res.Add($"{rows[i]}: {rows[i + 1]}");
        }
        return res;
    }

    public static Func<List<OcrWord>, bool> IsResultScreen(double imgW, double imgH) =>
        w => ParseResultScreen(w, imgW, imgH).metrics != null;

    /// <summary>Модальный диалог подтверждения: вопрос и две кнопки в центре экрана (на меню и загрузке там текста нет).</summary>
    public static bool HasDialog(List<OcrWord> words, double imgW, double imgH) =>
        words.Count(w => w.X + w.W / 2 is var cx && cx > 0.30 * imgW && cx < 0.70 * imgW && w.Cy > 0.38 * imgH && w.Cy < 0.62 * imgH) >= 4;

    /// <summary>Главное меню — короткий вертикальный список пунктов у левого края.</summary>
    public static bool LooksLikeMainMenu(List<OcrWord> words, double imgW, double imgH) =>
        Rows(words, w => w.X < 0.30 * imgW && w.Cy > 0.38 * imgH && w.Cy < 0.68 * imgH).Count >= 3;
}
