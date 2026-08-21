using Tomlyn;
using Tomlyn.Model;

namespace UpdateApp;

/// <summary>applist.toml 顶层配置。</summary>
public sealed class AppConfig
{
    /// <summary>日志目录（相对配置文件所在目录）。</summary>
    public string LogDir { get; set; } = "logs";

    /// <summary>发布产物目录（相对配置文件所在目录），供 self 自更新使用。</summary>
    public string BinDir { get; set; } = "bin";

    /// <summary>单项更新默认超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 900;

    public ScoopConfig Scoop { get; set; } = new();
    public List<SourceProject> Sources { get; set; } = new();
    public List<SpecialItem> Specials { get; set; } = new();
    public List<FixRule> FixRules { get; set; } = new();

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到配置文件: {path}");

        TomlTable root;
        try
        {
            root = Toml.ToModel(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            var tip = ex.Message.Contains("escape character", StringComparison.OrdinalIgnoreCase)
                ? "。提示：路径含反斜杠时请改用正斜杠（C:/path/to/dir）或 TOML 字面量字符串 'C:\\path\\to\\dir'"
                : "";
            throw new InvalidDataException($"解析 TOML 失败: {ex.Message}{tip}");
        }

        var cfg = new AppConfig();
        if (root.TryGetValue("general", out var g) && g is TomlTable gt)
        {
            cfg.LogDir = GetString(gt, "log_dir") ?? cfg.LogDir;
            cfg.BinDir = GetString(gt, "bin_dir") ?? cfg.BinDir;
            cfg.TimeoutSeconds = GetInt(gt, "timeout_seconds") ?? cfg.TimeoutSeconds;
        }

        if (root.TryGetValue("scoop", out var s) && s is TomlTable st)
        {
            cfg.Scoop.UpdateAll = GetBool(st, "update_all") ?? true;
            cfg.Scoop.Apps = GetStringList(st, "apps") ?? new();
        }

        foreach (var t in GetTableList(root, "source", "projects"))
        {
            cfg.Sources.Add(new SourceProject
            {
                Name = GetString(t, "name") ?? "",
                Path = GetString(t, "path") ?? "",
                Enabled = GetBool(t, "enabled") ?? true,
                Command = GetString(t, "command"),
                PostCommands = GetStringList(t, "post_commands") ?? new(),
            });
        }

        foreach (var t in GetTableList(root, "special", "items"))
        {
            cfg.Specials.Add(new SpecialItem
            {
                Name = GetString(t, "name") ?? "",
                Paths = GetStringList(t, "paths") ?? new(),
                Enabled = GetBool(t, "enabled") ?? true,
                Command = GetString(t, "command"),
                PostCommands = GetStringList(t, "post_commands") ?? new(),
            });
        }

        foreach (var t in GetTableList(root, "fixes", "rules"))
        {
            var match = GetString(t, "match") ?? "";
            if (string.IsNullOrWhiteSpace(match)) continue;
            cfg.FixRules.Add(new FixRule
            {
                Match = match,
                Command = GetString(t, "command"),
                Retry = GetBool(t, "retry") ?? true,
                Skip = GetBool(t, "skip") ?? false,
                AskUser = GetBool(t, "ask_user") ?? false,
                Hint = GetString(t, "hint"),
            });
        }

        cfg.Sources.RemoveAll(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Path));
        cfg.Specials.RemoveAll(x => string.IsNullOrWhiteSpace(x.Name) || x.Paths.Count == 0);
        return cfg;
    }

    private static string? GetString(TomlTable t, string key)
        => t.TryGetValue(key, out var v) ? v as string : null;

    private static bool? GetBool(TomlTable t, string key)
        => t.TryGetValue(key, out var v) && v is bool b ? b : null;

    private static int? GetInt(TomlTable t, string key)
        => t.TryGetValue(key, out var v) && v is long l ? (int)l : null;

    private static List<string>? GetStringList(TomlTable t, string key)
    {
        if (!t.TryGetValue(key, out var v) || v is not TomlArray arr) return null;
        var list = new List<string>();
        foreach (var e in arr)
            if (e is string s) list.Add(s);
        return list;
    }

    /// <summary>读取「数组中的表」：兼容 TomlArray 与 TomlTableArray（Tomlyn 把 [[key]] 建模为后者）。</summary>
    private static List<TomlTable> GetTableList(TomlTable t, string tableKey, string arrayKey)
    {
        var result = new List<TomlTable>();
        if (!t.TryGetValue(tableKey, out var top) || top is not TomlTable table) return result;
        if (!table.TryGetValue(arrayKey, out var v)) return result;
        if (v is TomlTableArray tarr)
            foreach (var e1 in tarr)
                if (e1 is TomlTable tt1) result.Add(tt1);
        else if (v is TomlArray arr)
            foreach (var e2 in arr)
                if (e2 is TomlTable tt2) result.Add(tt2);
        return result;
    }
}

public sealed class ScoopConfig
{
    /// <summary>true = scoop update *；false = 只更新 Apps 列出的应用。</summary>
    public bool UpdateAll { get; set; } = true;
    public List<string> Apps { get; set; } = new();
}

/// <summary>源码型项目：基于本地源码目录更新（默认 git pull --ff-only）。</summary>
public sealed class SourceProject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>可选自定义更新命令模板，支持 {name} {path} 占位符。</summary>
    public string? Command { get; set; }
    /// <summary>更新成功后执行的后置命令模板。</summary>
    public List<string> PostCommands { get; set; } = new();
}

/// <summary>特殊配置更新：一组本地路径（单个 repo 或包含多个 repo 的目录）。</summary>
public sealed class SpecialItem
{
    public string Name { get; set; } = "";
    public List<string> Paths { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public string? Command { get; set; }
    public List<string> PostCommands { get; set; } = new();
}

/// <summary>已知问题自愈规则：失败输出命中 match（正则）后按语义处理。</summary>
public sealed class FixRule
{
    public string Match { get; set; } = "";
    /// <summary>修复命令模板（支持 {name} {path}）。为空则只做一次原样重试。</summary>
    public string? Command { get; set; }
    /// <summary>执行修复后是否重试该项（默认 true）。</summary>
    public bool Retry { get; set; } = true;
    /// <summary>命中后直接标记跳过（已知问题，不再重试）。</summary>
    public bool Skip { get; set; }
    /// <summary>命中后标记 needs_input，停下等用户回复（由调用方/技能处理）。</summary>
    public bool AskUser { get; set; }
    public string? Hint { get; set; }
}