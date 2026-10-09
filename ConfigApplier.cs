using System.Text.RegularExpressions;

namespace WukongBench;

/// <summary>
/// Записывает пресет в GameUserSettings.ini Benchmark Tool (до запуска игры) и после прогона возвращает
/// исходный файл пользователя. Игра читает эти значения при старте (проверено: UISettingData + sg.* + размер рендера).
/// </summary>
public sealed class ConfigApplier : IDisposable
{
    const string Gus = "/Script/GSGameSettings.GSGameUserSettings";
    const string Sg = "ScalabilityGroups";

    // ключ UISettingData из меню игры -> группа масштабируемости Unreal (уровень в UI 1..5 == sg 0..4)
    static readonly Dictionary<string, string> SgMap = new()
    {
        ["ViewDistance"] = "sg.ViewDistanceQuality",
        ["AntiAliasing"] = "sg.AntiAliasingQuality",
        ["PostProcessing"] = "sg.PostProcessQuality",
        ["ShadowQuality"] = "sg.ShadowQuality",
        ["TextureQuality"] = "sg.TextureQuality",
        ["FxQuality"] = "sg.EffectsQuality",
        ["MaterialQuality"] = "sg.ShadingQuality",
        ["VegetationQuality"] = "sg.FoliageQuality",
        ["GlobalIllumination"] = "sg.GlobalIlluminationQuality",
        ["ReflectionQuality"] = "sg.ReflectionQuality",
    };

    public string ConfigDir { get; }
    string IniPath => Path.Combine(ConfigDir, "GameUserSettings.ini");
    byte[]? _backup;
    bool _hasBackup;

    public ConfigApplier(string appDir) => ConfigDir = Path.Combine(appDir, "b1", "Saved", "Config", "Windows");

    static readonly Regex Pair = new("\\(\"([^\"]*)\",\\s*\"([^\"]*)\"\\)", RegexOptions.Compiled);

    /// <summary>Меняет значения в списке кортежей в стиле UE ((\"K\", \"V\"),...); отсутствующие ключи добавляются в конец.</summary>
    public static string PatchUiSettings(string tuple, IReadOnlyDictionary<string, string> set)
    {
        var left = new Dictionary<string, string>(set);
        var patched = Pair.Replace(tuple, m =>
        {
            var k = m.Groups[1].Value;
            if (!left.Remove(k, out var v)) return m.Value;
            return $"(\"{k}\", \"{v}\")";
        });
        foreach (var (k, v) in left)
            patched = patched.Insert(patched.LastIndexOf(')'), $",(\"{k}\", \"{v}\")");
        return patched;
    }

    /// <returns>Читаемый список того, что было записано.</returns>
    public Dictionary<string, string> Apply(Preset p, int outW, int outH, bool nvidiaRtx)
    {
        if (!_hasBackup) { _backup = File.Exists(IniPath) ? File.ReadAllBytes(IniPath) : null; _hasBackup = true; }
        if (File.Exists(IniPath)) File.SetAttributes(IniPath, FileAttributes.Normal);
        var ini = IniFile.Load(IniPath);

        int scale = Math.Clamp(p.RenderScale, 25, 100);
        int renderW = outW * scale / 100, renderH = outH * scale / 100;

        var ui = new Dictionary<string, string>(p.Ui) { ["ImageQuality"] = renderH.ToString() };
        if (nvidiaRtx) foreach (var kv in p.UiIfNvidiaRtx) ui[kv.Key] = kv.Value;
        foreach (var (k, lvl) in p.Quality)
        {
            if (!SgMap.TryGetValue(k, out var sg)) throw new ArgumentException($"Unknown quality key '{k}'");
            int l = Math.Clamp(lvl, 1, 5);
            ui[k] = l.ToString();
            ini.Set(Sg, sg, (l - 1).ToString());
        }
        ini.Set(Sg, "sg.ResolutionQuality", scale.ToString());

        var tuple = ini.Get(Gus, "UISettingData")
                    ?? throw new InvalidOperationException("GameUserSettings.ini has no UISettingData: start the Benchmark Tool once, then retry.");
        ini.Set(Gus, "UISettingData", PatchUiSettings(tuple, ui));

        foreach (var k in new[] { "ResolutionSizeX", "LastUserConfirmedResolutionSizeX" }) ini.Set(Gus, k, outW.ToString());
        foreach (var k in new[] { "ResolutionSizeY", "LastUserConfirmedResolutionSizeY" }) ini.Set(Gus, k, outH.ToString());
        foreach (var k in new[] { "DesiredScreenWidth", "LastUserConfirmedDesiredScreenWidth" }) ini.Set(Gus, k, renderW.ToString());
        foreach (var k in new[] { "DesiredScreenHeight", "LastUserConfirmedDesiredScreenHeight" }) ini.Set(Gus, k, renderH.ToString());
        ini.Save(IniPath);

        var applied = new Dictionary<string, string>
        {
            ["Output resolution"] = $"{outW}x{outH}",
            ["Render scale"] = $"{scale}% ({renderW}x{renderH})",
        };
        foreach (var (k, v) in ui.Where(kv => kv.Key != "ImageQuality")) applied[k] = v;
        return applied;
    }

    public void Restore()
    {
        if (!_hasBackup) return;
        try
        {
            if (File.Exists(IniPath)) File.SetAttributes(IniPath, FileAttributes.Normal);
            if (_backup == null) File.Delete(IniPath); else File.WriteAllBytes(IniPath, _backup);
            _hasBackup = false;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[warn] cannot restore {IniPath}: {ex.Message}"); }
    }

    public void Dispose() => Restore();
}
