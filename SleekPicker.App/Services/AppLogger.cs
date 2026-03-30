using System.IO;

namespace SleekPicker.App;

internal sealed class AppLogger
{
    private readonly object _sync = new();
    private readonly string _path;

    public AppLogger(string path)
    {
        _path = path;
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Write("ERROR", message);
            return;
        }

        Write("ERROR", $"{message} {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

        lock (_sync)
        {
            File.AppendAllText(_path, line);
        }
    }
}
