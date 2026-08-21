using System.Text;

namespace UpdateApp;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return Cli.Run(args);
        }
        catch (Exception ex)
        {
            // 用 ex.ToString()（消息+堆栈）而不是 ex.Message：空消息异常也能定位根因
            Console.Error.WriteLine("错误: " + ex);
            return 2;
        }
    }
}

public static class Cli
{
    public static int Run(string[] args)
    {
        string? config = null, only = null, repo = null, project = null;
        var dryRun = false;
        var verbose = false;
        int? timeout = null;
        var command = "";

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-c":
                case "--config":
                    config = RequireValue(args, ref i, a);
                    break;
                case "--only":
                    only = RequireValue(args, ref i, a);
                    break;
                case "--repo":
                    repo = RequireValue(args, ref i, a);
                    break;
                case "--project":
                    project = RequireValue(args, ref i, a);
                    break;
                case "--timeout":
                    timeout = int.Parse(RequireValue(args, ref i, a));
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "-v":
                case "--verbose":
                    verbose = true;
                    break;
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    if (a.StartsWith('-')) { Console.Error.WriteLine($"未知选项: {a}"); return 2; }
                    if (command.Length == 0) command = a;
                    else { Console.Error.WriteLine($"多余的参数: {a}"); return 2; }
                    break;
            }
        }

        if (command.Length == 0) command = "help";
        var configPath = config ?? Path.Combine(Directory.GetCurrentDirectory(), "applist.toml");

        return command switch
        {
            "run" => CmdRun(configPath, only, dryRun, verbose, timeout),
            "list" => CmdList(configPath),
            "validate" => CmdValidate(configPath),
            "self" => CmdSelf(configPath, repo, project),
            "help" => PrintHelpAndReturn(),
            _ => UnknownCommand(command),
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"选项 {flag} 缺少参数值");
        return args[++i];
    }

    // ---------- run ----------

    private static int CmdRun(string configPath, string? only, bool dryRun, bool verbose, int? timeout)
    {
        var cfg = AppConfig.Load(configPath);
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        var logDir = Path.Combine(configDir, cfg.LogDir);
        Directory.CreateDirectory(logDir);
        var latestLog = Path.Combine(logDir, "latest.log");
        File.Delete(latestLog);
        var runLog = Path.Combine(logDir, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.AppendAllText(latestLog, $"# update-app run {DateTime.Now:yyyy-MM-dd HH:mm:ss} config={configPath} dryRun={dryRun}\n");

        var runner = new Runner(cfg, configPath, new Logger(latestLog, verbose), dryRun, only, timeout);
        var code = runner.RunAll();
        var json = runner.WriteSummary(logDir);
        File.Copy(latestLog, runLog, overwrite: true);
        Console.WriteLine();
        Console.WriteLine(json);
        return code;
    }

    // ---------- list ----------

    private static int CmdList(string configPath)
    {
        var cfg = AppConfig.Load(configPath);
        Console.WriteLine($"配置: {Path.GetFullPath(configPath)}\n");

        Console.WriteLine("[source] 源码型项目");
        foreach (var s in cfg.Sources)
            Console.WriteLine($"  {(s.Enabled ? "" : "[禁用] ")}{s.Name}  ({s.Path})" + (s.Command is null ? "" : $"\n    自定义命令: {s.Command}"));

        Console.WriteLine("\n[scoop] Scoop 应用");
        Console.WriteLine(cfg.Scoop.UpdateAll ? "  update_all = true  →  scoop update *"
            : cfg.Scoop.Apps.Count == 0 ? "  update_all = false 且未列出应用（无任务）"
            : "  update_all = false  →  scoop update " + string.Join(" ", cfg.Scoop.Apps));

        Console.WriteLine("\n[special] 特殊配置更新");
        foreach (var sp in cfg.Specials)
        {
            Console.WriteLine($"  {(sp.Enabled ? "" : "[禁用] ")}{sp.Name}");
            foreach (var p in sp.Paths) Console.WriteLine($"    - {p}");
            if (sp.PostCommands.Count > 0) Console.WriteLine($"    后置命令: {string.Join(" && ", sp.PostCommands)}");
        }

        Console.WriteLine($"\n[fixes] 已知问题自愈规则（{cfg.FixRules.Count} 条）");
        foreach (var f in cfg.FixRules)
            Console.WriteLine($"  /{f.Match}/  →  " + (f.Skip ? "跳过" : f.AskUser ? "询问用户" : f.Command ?? "重试") + (f.Hint is null ? "" : $"  （{f.Hint}）"));
        return 0;
    }

    // ---------- validate ----------

    private static int CmdValidate(string configPath)
    {
        var cfg = AppConfig.Load(configPath);
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        var errors = 0;
        var warnings = 0;

        foreach (var s in cfg.Sources.Where(x => x.Enabled))
        {
            if (Directory.Exists(s.Path)) Console.WriteLine($"  [ok] source {s.Name}: {s.Path}");
            else { Console.WriteLine($"  [缺] source {s.Name}: 路径不存在 {s.Path}"); errors++; }
        }
        foreach (var sp in cfg.Specials.Where(x => x.Enabled))
            foreach (var p in sp.Paths)
            {
                if (Directory.Exists(p)) Console.WriteLine($"  [ok] special {sp.Name}: {p}");
                else { Console.WriteLine($"  [缺] special {sp.Name}: 路径不存在 {p}"); errors++; }
            }
        if (!cfg.Scoop.UpdateAll && cfg.Scoop.Apps.Count == 0)
        {
            Console.WriteLine("  [警告] scoop: update_all=false 且未列出应用（无任务）");
            warnings++;
        }

        var git = ProcessRunner.Run("git --version", null, 60);
        Console.WriteLine(git.ExitCode == 0 ? $"  [ok] git: {git.Output}" : "  [缺] git 不可用");
        if (git.ExitCode != 0) errors++;

        var scoop = ProcessRunner.Run("scoop --version", null, 120);
        Console.WriteLine(scoop.ExitCode == 0 ? $"  [ok] scoop: {FirstLine(scoop.Output)}" : "  [缺] scoop 不可用（可注释/移除 scoop 段）");
        if (scoop.ExitCode != 0) warnings++;

        var dotnet = DotnetResolver.Resolve();
        Console.WriteLine(dotnet is null ? "  [缺] 找不到带 SDK 的 dotnet（self 自更新不可用）" : $"  [ok] dotnet: {dotnet}");
        if (dotnet is null) warnings++;

        var logDir = Path.Combine(configDir, cfg.LogDir);
        try
        {
            Directory.CreateDirectory(logDir);
            Console.WriteLine($"  [ok] 日志目录: {logDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [缺] 日志目录不可写: {ex.Message}");
            errors++;
        }

        Console.WriteLine($"\n校验完成：{errors} 个错误，{warnings} 个警告");
        return errors > 0 ? 1 : 0;
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return i < 0 ? s : s[..i];
    }

    // ---------- self（自更新） ----------

    private static int CmdSelf(string configPath, string? repoOverride, string? projectOverride)
    {
        var cfg = AppConfig.Load(configPath);
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";

        var repo = repoOverride ?? cfg.Sources.FirstOrDefault(s => s.Name.Equals("update-app", StringComparison.OrdinalIgnoreCase))?.Path;
        if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo))
        {
            Console.Error.WriteLine("无法确定 update-app 源码目录：请用 --repo 指定，或在 applist.toml 的 [source] 中配置名为 update-app 的项目");
            return 2;
        }

        Console.WriteLine($"[self] 源码目录: {repo}");
        var remotes = ProcessRunner.Run($"git -C \"{repo}\" remote", repo, 60);
        if (remotes.ExitCode != 0)
        {
            Console.WriteLine($"[self] 不是 git 仓库（{remotes.Output}），跳过拉取");
        }
        else if (string.IsNullOrWhiteSpace(remotes.Output))
        {
            Console.WriteLine("[self] 无 remote，跳过 git pull");
        }
        else
        {
            Console.WriteLine("[self] git pull --ff-only …");
            var pull = ProcessRunner.Run($"git -C \"{repo}\" pull --ff-only", repo, _TimeoutOf(cfg));
            if (pull.ExitCode != 0)
            {
                Console.Error.WriteLine("[self] 拉取失败:\n" + pull.Output);
                return 1;
            }
            Console.WriteLine("[self] 拉取完成");
        }

        var dotnet = DotnetResolver.Resolve();
        if (dotnet is null)
        {
            Console.Error.WriteLine("找不到带 SDK 的 dotnet（可安装 scoop 的 dotnet-sdk 包）");
            return 1;
        }

        var csproj = projectOverride ?? Directory.GetFiles(repo, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"));
        if (csproj is null)
        {
            Console.Error.WriteLine($"在 {repo} 下找不到 .csproj");
            return 1;
        }

        var binDir = Path.Combine(configDir, cfg.BinDir);
        var outDir = Path.Combine(binDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[self] dotnet publish → {outDir}");
        var pub = ProcessRunner.RunExe(dotnet, new[] { "publish", csproj, "-c", "Release", "-o", outDir, "-nologo" }, repo, _TimeoutOf(cfg) * 2);
        if (pub.ExitCode != 0)
        {
            Console.Error.WriteLine("[self] publish 失败:\n" + pub.Output);
            return 1;
        }

        var exe = Path.Combine(outDir, "update-app.exe");
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"[self] 发布产物缺失: {exe}\n输出: {pub.Output}");
            return 1;
        }
        File.WriteAllText(Path.Combine(binDir, "current.json"),
            System.Text.Json.JsonSerializer.Serialize(new { exe, publishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[self] 完成。当前版本: {exe}");
        Console.WriteLine($"[self] 之后请运行: \"{exe}\" run --config \"{Path.GetFullPath(configPath)}\"");
        return 0;
    }

    private static int _TimeoutOf(AppConfig cfg) => cfg.TimeoutSeconds;

    // ---------- help ----------

    private static void PrintHelp()
    {
        Console.WriteLine("""
            update-app —— 读取 applist.toml 批量更新软件（源码项目 / Scoop 应用 / 特殊配置）

            用法: update-app <命令> [选项]

            命令:
              run       执行全部更新（源码 git pull → scoop → 特殊配置），写 logs/latest.json + latest.log
              list      列出配置中的更新项
              validate  校验配置与依赖（路径 / git / scoop / dotnet）
              self      自更新：git pull 自身源码 + dotnet publish 到 <config>/bin（写入 current.json）

            选项:
              -c, --config <path>   配置文件路径（默认 ./applist.toml）
              --only <关键字>       只运行名称/类别包含关键字的项目
              --dry-run             只预览将执行的命令，不实际执行
              --timeout <秒>        覆盖单项超时（默认 900）
              --repo <路径>         self 用：指定自身源码目录
              --project <路径>      self 用：指定 .csproj（默认源码目录下第一个）
              -v, --verbose         详细日志
              -h, --help            显示帮助

            退出码: 0 = 全部成功/已修复；1 = 存在失败或需确认项；2 = 参数/配置错误
            """);
    }

    private static int PrintHelpAndReturn()
    {
        PrintHelp();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"未知命令: {command}");
        PrintHelp();
        return 2;
    }
}

/// <summary>解析出「带 SDK」且 SDK 版本最高的 dotnet：优先 PATH，其次 scoop 的 dotnet-sdk / dotnet9-sdk，最后 Program Files。</summary>
public static class DotnetResolver
{
    public static string? Resolve()
    {
        var trace = Environment.GetEnvironmentVariable("UA_TRACE_DOTNET") == "1";
        void T(string m) { if (trace) Console.WriteLine("[dotnet-trace] " + m); }

        var candidates = new List<(string Path, Version Ver)>();
        void Add(string p)
        {
            if (candidates.Any(c => c.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) return;
            if (!File.Exists(p)) { T($"candidate {p} exists=False"); return; }
            var ver = GetSdkVersion(p);
            T($"candidate {p} sdk={ver?.ToString() ?? "none"}");
            if (ver is not null) candidates.Add((p, ver));
        }

        var whereOut = ProcessRunner.Run("where dotnet", null, 60).Output;
        T($"where dotnet => {whereOut.Replace("\n", " | ")}");
        foreach (var d in whereOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Add(d);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var c in new[]
                 {
                     Path.Combine(home, "scoop", "apps", "dotnet-sdk", "current", "dotnet.exe"),
                     Path.Combine(home, "scoop", "apps", "dotnet9-sdk", "current", "dotnet.exe"),
                     @"C:\Program Files\dotnet\dotnet.exe",
                 })
            Add(c);

        var best = candidates.OrderByDescending(c => c.Ver).FirstOrDefault();
        return best.Path is null ? null : best.Path;
    }

    /// <summary>返回该 dotnet 安装的最高 SDK 版本（无 SDK 时返回 null）。</summary>
    private static Version? GetSdkVersion(string dotnet)
    {
        var r = ProcessRunner.RunExe(dotnet, new[] { "--list-sdks" }, null, 60);
        if (r.ExitCode != 0) return null;
        Version? best = null;
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = line.Split(' ')[0].Trim();
            if (Version.TryParse(token, out var v) && (best is null || v > best)) best = v;
        }
        return best;
    }
}