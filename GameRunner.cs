using System.Diagnostics;
using System.Drawing;

namespace WukongBench;

/// <summary>Запускает Benchmark Tool через Steam, стартует тест из его меню, ждёт экран результатов и считывает его.</summary>
public class GameRunner
{
    readonly Settings _s;
    readonly string _outDir;

    public GameRunner(Settings s, string outDir) { _s = s; _outDir = outDir; }

    static void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

    List<Process> GameProcesses() => Process.GetProcesses().Where(p =>
    {
        try { return p.ProcessName.Contains(_s.ExeNameHint, StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }).ToList();

    /// <summary>Закрывает инструмент (сначала лаунчер, чтобы ничего не перезапустилось) и проверяет, что процессов не осталось.</summary>
    async Task KillAll()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            foreach (var p in Process.GetProcessesByName("b1_benchmark")) Win32.KillTree(p);
            foreach (var p in GameProcesses()) Win32.KillTree(p);
            await Task.Delay(1500);
            if (GameProcesses().Count == 0 && Process.GetProcessesByName("b1_benchmark").Length == 0) return;
        }
        Log("[warn] Benchmark Tool processes are still alive after repeated kill attempts");
    }

    async Task<Process?> WaitForWindow(int timeoutSec)
    {
        var end = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < end)
        {
            var p = GameProcesses().FirstOrDefault(x => { x.Refresh(); return x.MainWindowHandle != 0; });
            if (p != null) return p;
            await Task.Delay(1000);
        }
        return null;
    }

    async Task<(Bitmap bmp, List<OcrWord> words)> Snap(Process game, bool resultScreen = false)
    {
        game.Refresh();
        if (game.MainWindowHandle == 0) throw new InvalidOperationException("game window is gone");
        Win32.Focus(game.MainWindowHandle);        // захват делается копированием экрана: игра должна быть верхним окном
        await Task.Delay(300);
        var bmp = Win32.CaptureClient(game.MainWindowHandle);
        return (bmp, await Ocr.ReadScreenAsync(bmp, resultScreen ? ResultParser.IsResultScreen(bmp.Width, bmp.Height) : null));
    }

    public async Task<RunResult> RunAsync(string name, Preset preset, int w, int h, bool nvidiaRtx, ConfigApplier cfg, bool dryRun)
    {
        var res = new RunResult { Name = name, Resolution = $"{w}x{h}" };
        var sw = Stopwatch.StartNew();
        try
        {
            if (GameProcesses().Count > 0) { Log("Benchmark Tool already running - closing it."); await KillAll(); }

            Log($"[{name}] applying settings: {preset.Description}");
            res.AppliedSettings = cfg.Apply(preset, w, h, nvidiaRtx);
            if (dryRun) { res.Error = "dry-run: game not started"; return res; }

            Log($"[{name}] launching steam://rungameid/{_s.AppId}");
            Process.Start(new ProcessStartInfo($"steam://rungameid/{_s.AppId}") { UseShellExecute = true });
            var game = await WaitForWindow(_s.LaunchTimeoutSec) ?? throw new TimeoutException("Benchmark Tool window did not appear");
            Log($"[{name}] window found ({game.ProcessName})");

            foreach (var step in _s.StartSteps) await RunStep(step, game, name);
            Log($"[{name}] benchmark started, waiting for the result screen");

            var started = DateTime.UtcNow;
            BenchMetrics? m = null; List<string> toolSettings = new(); Bitmap? shot = null;
            while (DateTime.UtcNow < started.AddSeconds(_s.BenchmarkTimeoutSec))
            {
                await Task.Delay(1000);
                if (GameProcesses().Count == 0) throw new InvalidOperationException("game exited before a result was shown");
                // OCR только когда прогон почти закончен, чтобы не влиять на измерение.
                if ((DateTime.UtcNow - started).TotalSeconds < _s.OcrStartAfterSec) continue;

                var (bmp, words) = await Snap(game, resultScreen: true);
                (m, toolSettings) = ResultParser.ParseResultScreen(words, bmp.Width, bmp.Height);
                if (m != null) { shot = bmp; break; }
                bmp.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(_s.OcrIntervalSec));
            }
            if (m == null) throw new TimeoutException("result screen not recognised within the timeout");

            // сразу после появления экрана цифры ещё могут анимироваться; читаем повторно, когда всё устоится.
            await Task.Delay(TimeSpan.FromSeconds(_s.ResultsSettleSec));
            var (bmp2, words2) = await Snap(game, resultScreen: true);
            var (m2, ts2) = ResultParser.ParseResultScreen(words2, bmp2.Width, bmp2.Height);
            if (m2 != null) { m = m2; toolSettings = ts2; shot?.Dispose(); shot = bmp2; } else bmp2.Dispose();

            Directory.CreateDirectory(_outDir);
            res.ScreenshotPath = Path.Combine(_outDir, $"{name}_result.png");
            shot!.Save(res.ScreenshotPath);
            shot.Dispose();
            res.Metrics = m; res.ToolSettingsText = toolSettings;
            Log($"[{name}] avg {m.AvgFps} / max {m.MaxFps} / min {m.MinFps} FPS");
        }
        catch (Exception ex)
        {
            res.Error = ex.Message;
            Log($"[{name}] FAILED: {ex.Message}");
        }
        finally
        {
            await KillAll();
            await Task.Delay(3000);
            cfg.Restore();      // исходные настройки пользователя возвращаются перед следующим проходом / при выходе
            res.DurationSec = sw.Elapsed.TotalSeconds;
        }
        return res;
    }

    async Task RunStep(Step step, Process game, string name)
    {
        game.Refresh();
        switch (step.Type.ToLowerInvariant())
        {
            case "wait": await Task.Delay(TimeSpan.FromSeconds(step.Seconds)); break;
            case "key": Win32.Focus(game.MainWindowHandle); await Task.Delay(300); Win32.PressKey(step.Key); break;
            case "click": Win32.Focus(game.MainWindowHandle); await Task.Delay(300); Win32.Click(game.MainWindowHandle, step.X, step.Y); break;
            case "waitformenu":
                var end = DateTime.UtcNow.AddSeconds(_s.MenuTimeoutSec);
                while (DateTime.UtcNow < end)
                {
                    await Task.Delay(3000);
                    var (shot, words) = await Snap(game);
                    bool menu = ResultParser.LooksLikeMainMenu(words, shot.Width, shot.Height);
                    shot.Dispose();
                    if (menu) { await Task.Delay(1500); Log($"[{name}] main menu is up"); return; }
                }
                throw new TimeoutException("main menu did not appear");
            case "confirmdialogs":
                // диалог «Хотите запустить тест?»: кнопка по умолчанию «Подтвердить», поэтому Enter принимает его.
                // Повторяем, пока два снимка подряд не покажут отсутствие диалога (идёт загрузка теста).
                for (int clear = 0, guard = 0; clear < 2 && guard < 12; guard++)
                {
                    await Task.Delay(2000);
                    var (shot, words) = await Snap(game);
                    bool dialog = ResultParser.HasDialog(words, shot.Width, shot.Height);
                    shot.Dispose();
                    if (dialog) { clear = 0; Log($"[{name}] confirmation dialog - pressing Enter"); Win32.PressKey("Enter"); }
                    else clear++;
                }
                break;
            default: throw new InvalidOperationException($"Unknown step type '{step.Type}'");
        }
    }
}
