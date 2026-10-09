using System.Text.Json;

namespace WukongBench;

/// <summary>
/// Профиль бенчмарка. Уровни качества заданы по шкале игры 1..5 (1 = низкое ... 5 = реалистичное),
/// ключи — те имена, под которыми игра хранит их в UISettingData.
/// </summary>
public class Preset
{
    public string Description { get; set; } = "";
    /// <summary>Выходное разрешение: "native" или "ШxВ".</summary>
    public string Resolution { get; set; } = "native";
    /// <summary>Внутренний масштаб рендера в процентах (ползунок 25..100 в игре).</summary>
    public int RenderScale { get; set; } = 100;
    /// <summary>ViewDistance, AntiAliasing, PostProcessing, ShadowQuality, TextureQuality, FxQuality, MaterialQuality, VegetationQuality, GlobalIllumination, ReflectionQuality.</summary>
    public Dictionary<string, int> Quality { get; set; } = new();
    /// <summary>Прочие «сырые» значения UISettingData (например, InsertFrame = генерация кадров, Rtx, Vsync).</summary>
    public Dictionary<string, string> Ui { get; set; } = new();
    /// <summary>Дополнительные значения UISettingData только для видеокарт NVIDIA RTX (полная трассировка лучей доступна лишь на NVIDIA).</summary>
    public Dictionary<string, string> UiIfNvidiaRtx { get; set; } = new();
}

public class Step
{
    public string Type { get; set; } = "Wait";   // Wait | WaitForMenu | Key | Click
    public double Seconds { get; set; }
    public string Key { get; set; } = "";         // Enter, Space, Tab, Esc, Up, Down, Left, Right, F1..F12, буквы
    public double X { get; set; }                 // Click: 0..1 относительно клиентской области окна
    public double Y { get; set; }
}

public class Settings
{
    public int AppId { get; set; } = 3132990;
    public string ExeNameHint { get; set; } = "b1-Win64-Shipping";
    public int LaunchTimeoutSec { get; set; } = 180;
    public int MenuTimeoutSec { get; set; } = 180;
    public int BenchmarkTimeoutSec { get; set; } = 900;
    public int OcrStartAfterSec { get; set; } = 100;
    public int OcrIntervalSec { get; set; } = 5;
    public int ResultsSettleSec { get; set; } = 3;
    public List<string> ExtraSearchDirs { get; set; } = new();
    public List<Step> StartSteps { get; set; } = new();
    public Dictionary<string, Preset> Presets { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static Settings Load(string path) =>
        JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Opts) ?? throw new InvalidDataException(path);
}

public class SystemSnapshot
{
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string CpuCores { get; set; } = "";
    public List<string> Gpus { get; set; } = new();
    public string Ram { get; set; } = "";
    public string Display { get; set; } = "";
    public string Motherboard { get; set; } = "";
}

public class BenchMetrics
{
    public double AvgFps { get; set; }
    public double MaxFps { get; set; }
    public double MinFps { get; set; }
    public double? P5Fps { get; set; }          // FPS «5-й перцентиль», показанный инструментом
    public double? VramGb { get; set; }         // использованная видеопамять
}

public class RunResult
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public Dictionary<string, string> AppliedSettings { get; set; } = new();
    public BenchMetrics? Metrics { get; set; }
    public List<string> ToolSettingsText { get; set; } = new();   // панель настроек с экрана результатов инструмента (OCR)
    public string? ScreenshotPath { get; set; }
    public string? Error { get; set; }
    public double DurationSec { get; set; }
}

public class Report
{
    public DateTime Date { get; set; } = DateTime.Now;
    public SystemSnapshot System { get; set; } = new();
    public RunResult? Cpu { get; set; }
    public RunResult? Gpu { get; set; }
}
