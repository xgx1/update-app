using System.Diagnostics;
using System.Text;

namespace UpdateApp;

public sealed record ProcResult(int ExitCode, string Output);

/// <summary>通过 cmd.exe 执行命令并捕获输出（Windows 下兼容 .cmd/.ps1 shim，如 scoop）。</summary>
public static class ProcessRunner
{
    public static ProcResult Run(string command, string? workDir = null, int timeoutSeconds = 900)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new(-1, "命令为空");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // 命令不以引号开头时原样传给 cmd /d /c（cmd 能正确处理命令内部的引号）；
            // 仅当整条命令以引号开头（如带引号的 exe 路径）时用 /s + 双重引号包装，
            // 避免外层包装引号与命令内部引号错位。
            Arguments = command.TrimStart().StartsWith('"')
                ? $"/d /s /c \"\"{command}\"\""
                : "/d /c " + command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // 清理会干扰子进程的环境变量：
        //  - MSBuildSDKsPath：本机被用户级设置为 dotnet9-sdk 的 Sdks，会强制 MSBuild 用错误的 SDK targets
        //  - version=N/A：会被 MSBuild 当作 Version 属性展开，导致 NuGet 报 "N/A" 不是有效的版本字符串
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("Version");
        psi.Environment.Remove("version");
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
            psi.WorkingDirectory = workDir;

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new(-1, "无法启动进程");
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                return new(124, $"执行超时（>{timeoutSeconds}s）: {command}");
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