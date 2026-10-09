namespace WukongBench;

/// <summary>Минимальный INI-редактор с сохранением строк (в стиле Unreal: повторяющиеся ключи и комментарии не трогаются).</summary>
public class IniFile
{
    readonly List<string> _lines;
    public IniFile(IEnumerable<string> lines) => _lines = lines.ToList();
    public static IniFile Load(string path) => new(File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>());
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, _lines);
    }
    public override string ToString() => string.Join(Environment.NewLine, _lines);

    static bool IsSection(string l, out string name)
    {
        l = l.Trim();
        name = l.StartsWith('[') && l.EndsWith(']') ? l[1..^1] : "";
        return name.Length > 0;
    }

    public string? Get(string section, string key)
    {
        var (_, idx) = Find(section, key);
        return idx < 0 ? null : _lines[idx][(_lines[idx].IndexOf('=') + 1)..].Trim();
    }

    (int secStart, int keyIdx) Find(string section, string key)
    {
        int sec = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (IsSection(_lines[i], out var n))
            {
                if (sec >= 0) return (sec, -1);                       // вышли из нужной секции, не найдя ключ
                if (n.Equals(section, StringComparison.OrdinalIgnoreCase)) sec = i;
            }
            else if (sec >= 0)
            {
                int eq = _lines[i].IndexOf('=');
                if (eq > 0 && _lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return (sec, i);
            }
        }
        return (sec, -1);
    }

    public void Set(string section, string key, string value)
    {
        var (sec, idx) = Find(section, key);
        if (idx >= 0) { _lines[idx] = $"{key}={value}"; return; }
        if (sec < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }
        int end = sec + 1;                                             // добавляем в конец секции
        while (end < _lines.Count && !IsSection(_lines[end], out _)) end++;
        while (end > sec + 1 && _lines[end - 1].Trim().Length == 0) end--;
        _lines.Insert(end, $"{key}={value}");
    }
}
