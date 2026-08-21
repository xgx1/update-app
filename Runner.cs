using System.Text.Json;
using System.Text.RegularExpressions;

namespace UpdateApp;

/// <summary>一个可执行的更新单元（源码项目 / scoop / 特殊配置的单个路径 / 后置命令）。</summary>
public sealed record SourceItem(string Category, string Name, string? Path, string Preview, Func<(bool Ok, string Output)> Execute);

public sealed class ItemResult
{
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    /// <summary>ok | fixed | skipped | needs_input | failed</summary>
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string? Output { get; set; }
    public string? FixApplied { get; set; }
    public string? Hint { get; set; }
}

/// <summary>更新编排：源码 → scoop → 特殊配置，逐项执行并应用已知问题自愈规则，产出 JSON 摘要。</summary>
public sealed class Runner
{
    private readonly AppConfig _cfg;
    private readonly string _configDir;
    private readonly Logger _log;
    private readonly bool _dryRun;
    private readonly string? _only;
    private readonly int _timeout;
    private readonly string _runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    private readonly List<ItemResult> _results = new();
    private int _ok, _fixed, _skipped, _needsInput, _failed;

    public Runner(AppConfig cfg, string configPath, Logger log, bool dryRun, string? only, int? timeoutOverride)
    {
        _cfg = cfg;
        _configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        _log = log;
        _dryRun = dryRun;
        _only = only;
        _timeout = timeoutOverride ?? cfg.TimeoutSeconds;
    }

    public IReadOnlyList<ItemResult> Results => _results;

    public int RunAll()
    {
        var items = new List<SourceItem>();
        foreach (var it in _cfg.Sources.Where(s => s.Enabled))
            items.Add(BuildSourceItem(it));
        items.Add(BuildScoopItem());
        foreach (var it in _cfg.Specials.Where(s => s.Enabled))
            items.AddRange(BuildSpecialItems(it));

        if (items.Count == 0)
            _log.Warn("配置中没有可更新的项目（source/special 为空且 scoop 无任务）");

        var matched = 0;
        foreach (var it in items)
        {
            if (_only is not null && !(it.Name.Contains(_only, StringComparison.OrdinalIgnoreCase)
                                       || it.Category.Contains(_only, StringComparison.OrdinalIgnoreCase)))
                continue;
            matched++;
            try { ProcessItem(it); }
            catch (Exception ex)
            {
                _log.Error($"[{it.Category}:{it.Name}] 执行异常: {ex.Message}");
                Record("failed", it, ex.Message, message: "执行异常");
            }
        }
        if (_only is not null)
            _log.Info(matched == 0 ? $"没有匹配 --only={_only} 的项目" : $"--only 过滤后共 {matched} 项");

        _log.Info($"更新完成：成功 {_ok}，已修复 {_fixed}，跳过 {_skipped}，需确认 {_needsInput}，失败 {_failed}");
        return (_failed > 0 || _needsInput > 0) ? 1 : 0;
    }

    // ---------- 项目构建 ----------

    private SourceItem BuildSourceItem(SourceProject p)
    {
        var defaultCmd = $"git -C \"{p.Path}\" pull --ff-only";
        return new SourceItem("source", p.Name, p.Path,
            p.Command is null ? defaultCmd : $"自定义命令: {p.Command}",
            () => RunProject(p.Command, p.Path, p.PostCommands));
    }

    private SourceItem BuildScoopItem()
    {
        string preview;
        if (!_cfg.Scoop.UpdateAll && _cfg.Scoop.Apps.Count == 0)
            preview = "(无可更新应用)";
        else if (_cfg.Scoop.UpdateAll)
            preview = "scoop update *";
        else
            preview = "scoop update " + string.Join(" ", _cfg.Scoop.Apps);
        return new SourceItem("scoop", "scoop", null, preview, RunScoop);
    }

    private List<SourceItem> BuildSpecialItems(SpecialItem item)
    {
        var list = new List<SourceItem>();
        foreach (var path in item.Paths)
            list.Add(new SourceItem("special", item.Name, path,
                item.Command is null ? $"git -C \"{path}\" pull --ff-only" : $"自定义命令: {item.Command}",
                () => RunGitPath(path, item.Command)));
        if (item.PostCommands.Count > 0)
            list.Add(new SourceItem("special-post", item.Name + "[后置]", null,
                "后置命令: " + string.Join(" && ", item.PostCommands),
                () => RunPostCommands(item.PostCommands, item.Name, item.Paths.FirstOrDefault())));
        return list;
    }

    // ---------- 执行器 ----------

    /// <summary>源码项目 / 特殊配置路径共用的更新执行：默认 git pull，先检查 remote。</summary>
    private (bool Ok, string Output) RunProject(string? command, string path, List<string> postCommands)
    {
        if (!Directory.Exists(path))
            return (false, $"路径不存在: {path}");
        var (ok, output) = RunGitPath(path, command);
        if (!ok || postCommands.Count == 0) return (ok, output);
        var (postOk, postOut) = RunPostCommands(postCommands, Path.GetFileName(path.TrimEnd('\\', '/')), path);
        return (postOk, output + (postOut.Length > 0 ? "\n[post]\n" + postOut : ""));
    }

    private (bool Ok, string Output) RunGitPath(string path, string? command)
    {
        if (!Directory.Exists(path))
            return (false, $"路径不存在: {path}");
        if (command is not null)
        {
            var r = ProcessRunner.Run(Expand(command, path), path, _timeout);
            return (r.ExitCode == 0, r.Output);
        }
        // 默认更新方式：git pull --ff-only
        var remotes = ProcessRunner.Run($"git -C \"{path}\" remote", path, 60);
        if (remotes.ExitCode != 0)
            return (false, remotes.Output);
        if (string.IsNullOrWhiteSpace(remotes.Output))
            return (true, "无 remote，跳过拉取");
        var pull = ProcessRunner.Run($"git -C \"{path}\" pull --ff-only", path, _timeout);
        return (pull.ExitCode == 0, pull.Output);
    }

    private (bool Ok, string Output) RunPostCommands(List<string> commands, string name, string? workDir)
    {
        var outs = new List<string>();
        foreach (var c in commands)
        {
            var r = ProcessRunner.Run(Expand(c, workDir ?? _configDir), workDir, _timeout);
            outs.Add($"> {c}\n{r.Output}");
            if (r.ExitCode != 0) return (false, string.Join("\n", outs));
        }
        return (true, string.Join("\n", outs));
    }

    private (bool Ok, string Output) RunScoop()
    {
        if (!_cfg.Scoop.UpdateAll && _cfg.Scoop.Apps.Count == 0)
            return (true, "update_all=false 且未列出应用，无更新任务");
        var check = ProcessRunner.Run("scoop --version", null, 120);
        if (check.ExitCode != 0)
            return (false, "scoop 不可用: " + check.Output);
        var apps = _cfg.Scoop.UpdateAll ? new[] { "*" } : _cfg.Scoop.Apps.ToArray();
        var r = ProcessRunner.Run("scoop update " + string.Join(" ", apps), null, _timeout);
        return (r.ExitCode == 0, r.Output);
    }

    private static string Expand(string template, string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return template
            .Replace("{path}", Quote(path))
            .Replace("{name}", Quote(name));
    }

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;

    // ---------- 自愈引擎 ----------

    private void ProcessItem(SourceItem it)
    {
        if (_dryRun)
        {
            _log.Info($"[dry-run] [{it.Category}:{it.Name}] 将执行: {it.Preview}");
            Record("ok", it, "[dry-run] 未实际执行: " + it.Preview);
            return;
        }

        _log.Info($"[{it.Category}:{it.Name}] 开始更新…");
        var r = it.Execute();
        if (r.Ok)
        {
            Record("ok", it, r.Output);
            return;
        }

        var rule = FindRule(r.Output + "\n" + it.Name);
        if (rule is null)
        {
            _log.Error($"[{it.Category}:{it.Name}] 失败且未命中已知修复规则，需要 AI 处理。输出: {Truncate(r.Output, 800)}");
            Record("failed", it, r.Output, message: "未命中已知修复规则");
            return;
        }

        if (rule.Skip)
        {
            _log.Warn($"[{it.Name}] 命中规则 [{rule.Match}]，跳过（{rule.Hint}）");
            Record("skipped", it, r.Output, rule, rule.Hint);
            return;
        }
        if (rule.AskUser)
        {
            _log.Warn($"[{it.Name}] 命中规则 [{rule.Match}]，需要用户确认（{rule.Hint}）");
            Record("needs_input", it, r.Output, rule, rule.Hint);
            return;
        }

        // 自动修复：执行修复命令（若有）→ 重试一次
        _log.Warn($"[{it.Name}] 命中规则 [{rule.Match}]，尝试自动修复"
                  + (rule.Command is not null ? $"：{rule.Command}" : "（直接重试一次）"));
        string? fixOut = null;
        if (!string.IsNullOrWhiteSpace(rule.Command))
        {
            var fixCmd = Expand(rule.Command, it.Path ?? _configDir);
            var fix = ProcessRunner.Run(fixCmd, it.Path is not null && Directory.Exists(it.Path) ? it.Path : _configDir, _timeout);
            fixOut = fix.Output;
            _log.Debug($"[{it.Name}] 修复命令输出: {Truncate(fix.Output, 500)}");
        }
        if (!rule.Retry)
        {
            Record("failed", it, r.Output + (fixOut is not null ? "\n[修复输出]\n" + fixOut : ""), rule, "修复后未重试（retry=false）");
            return;
        }

        var r2 = it.Execute();
        if (r2.Ok)
        {
            _log.Info($"[{it.Name}] 自动修复成功（规则: {rule.Match}）");
            Record("fixed", it, r2.Output, rule, hint: rule.Hint, fixOut);
        }
        else
        {
            _log.Error($"[{it.Name}] 修复后仍失败（规则: {rule.Match}）");
            Record("failed", it, r2.Output + (fixOut is not null ? "\n[修复输出]\n" + fixOut : ""), rule, "修复后仍失败");
        }
    }

    private FixRule? FindRule(string haystack)
    {
        foreach (var rule in _cfg.FixRules)
        {
            try
            {
                if (Regex.IsMatch(haystack, rule.Match, RegexOptions.IgnoreCase)) return rule;
            }
            catch (ArgumentException)
            {
                _log.Warn($"忽略无效的正则规则: \"{rule.Match}\"");
            }
        }
        return null;
    }

    private void Record(string status, SourceItem it, string? output, FixRule? rule = null, string? hint = null, string? fixOut = null, string? message = null)
    {
        var res = new ItemResult
        {
            Category = it.Category,
            Name = it.Name,
            Path = it.Path,
            Status = status,
            Message = message ?? hint,
            Output = Truncate(output, 4000),
            FixApplied = rule?.Match,
            Hint = hint,
        };
        _results.Add(res);
        switch (status)
        {
            case "ok": _ok++; _log.Info($"[{it.Name}] OK"); break;
            case "fixed": _fixed++; break;
            case "skipped": _skipped++; break;
            case "needs_input": _needsInput++; break;
            default: _failed++; break;
        }
        _ = fixOut;
    }

    // ---------- 摘要 ----------

    public string WriteSummary(string logDir)
    {
        var summary = new
        {
            runId = _runId,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            dryRun = _dryRun,
            only = _only,
            items = _results,
            counts = new Dictionary<string, int>
            {
                ["ok"] = _ok,
                ["fixed"] = _fixed,
                ["skipped"] = _skipped,
                ["needs_input"] = _needsInput,
                ["failed"] = _failed,
            },
            exitCode = (_failed > 0 || _needsInput > 0) ? 1 : 0,
            logFile = Path.Combine(logDir, "latest.log"),
        };
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "latest.json"), json);
        File.WriteAllText(Path.Combine(logDir, $"run-{_runId}.json"), json);
        return json;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + $"\n…（截断，共 {s.Length} 字符）";
    }
}