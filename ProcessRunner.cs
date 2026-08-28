using System.Diagnostics;
using System.Text;

namespace UpdateApp;

public sealed record ProcResult(int ExitCode, string Output);

/// <summary>
/// 命令执行：
///  - Run：通过 cmd.exe /d /c 执行配置类命令（支持 cmd 语法：管道/重定向/&& 等）。
///  - RunExe：直接启动可执行文件（ArgumentList 正规引用，路径含空格也安全），用于工具探测/发布。
/// </summary>
public static class ProcessRunner
{
    public static ProcResult Run(string command, string? workDir = null, int timeoutSeconds = 900)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new(-1, "命令为空");

        if (OperatingSystem.IsWindows())
        {
            var psi = NewPsi("cmd.exe", timeoutSeconds, workDir);
            // 原样交给 cmd 解析（不加外层包装引号）：cmd 能正确处理命令内部的引号。
            // 注意：自定义命令不要以引号开头（cmd 对首字符为引号的行有特殊剥离规则）。
            psi.Arguments = "/d /c " + command;
            return Exec(psi, timeoutSeconds, command);
        }

        // Linux: sh -lc <command>，整个命令必须作为单个参数传入（sh 自行解析管道/重定向/&&/引号）
        var sh = NewPsi("/bin/sh", timeoutSeconds, workDir);
        sh.ArgumentList.Add("-lc");
        sh.ArgumentList.Add(command);
        return Exec(sh, timeoutSeconds, command);
    }

    public static ProcResult RunExe(string exe, IEnumerable<string> args, string? workDir = null, int timeoutSeconds = 900)
    {
        if (!File.Exists(exe))
            return new(-1, $"可执行文件不存在: {exe}");
        var psi = NewPsi(exe, timeoutSeconds, workDir);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Exec(psi, timeoutSeconds, exe + " " + string.Join(" ", args));
    }

    private static ProcessStartInfo NewPsi(string fileName, int timeoutSeconds, string? workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
            psi.WorkingDirectory = workDir;
        // 清理会干扰子进程的环境变量：
        //  - MSBuildSDKsPath：本机被用户级设置为 dotnet9-sdk 的 Sdks，会强制 MSBuild 用错误的 SDK targets
        //  - version=N/A：会被 MSBuild 当作 Version 属性展开，导致 NuGet 报 "N/A" 不是有效的版本字符串
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("Version");
        psi.Environment.Remove("version");
        return psi;
    }

    private static ProcResult Exec(ProcessStartInfo psi, int timeoutSeconds, string label)
    {
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new(-1, "无法启动进程");
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                return new(124, $"执行超时（>{timeoutSeconds}s）: {label}");
            }
            var outp = so.GetAwaiter().GetResult().Trim();
            var err = se.GetAwaiter().GetResult().Trim();
            var merged = outp;
            if (err.Length > 0)
                merged = outp.Length > 0 ? outp + "\n[stderr]\n" + err : "[stderr]\n" + err;
            return new(p.ExitCode, merged);
        }
        catch (Exception ex)
        {
            return new(-1, $"启动失败: {ex.Message}");
        }
    }
}