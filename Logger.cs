namespace UpdateApp;

/// <summary>同时输出到控制台与日志文件（latest.log）。</summary>
public sealed class Logger
{
    private readonly string? _file;
    private readonly object _lock = new();

    public bool Verbose { get; set; }

    public Logger(string? file, bool verbose)
    {
        _file = file;
        Verbose = verbose;
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);
    public void Debug(string msg)
    {
        if (Verbose) Write("DEBUG", msg);
    }

    private void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";
        lock (_lock)
        {
            Console.WriteLine(line);
            if (_file is not null)
            {
                try { File.AppendAllText(_file, line + Environment.NewLine); }
                catch { /* 日志写入失败不阻断主流程 */ }
            }
        }
    }
}