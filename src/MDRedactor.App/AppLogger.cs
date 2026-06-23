using System.IO;
using System.Text;

namespace MDRedactor.App;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static void LogError(Exception exception, string context, string? filePath = null)
    {
        Write("ERROR", context, filePath, exception.ToString());
    }

    public static void LogWarning(string context, string? filePath = null)
    {
        Write("WARN", context, filePath, null);
    }

    private static void Write(string level, string context, string? filePath, string? details)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = Path.Combine(localAppData, "MDRedactor", "logs");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, $"{DateTimeOffset.Now:yyyy-MM-dd}.log");
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [")
                .Append(level)
                .Append("] ")
                .Append(context);

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                builder.Append(" | File: ").Append(filePath);
            }

            if (!string.IsNullOrWhiteSpace(details))
            {
                builder.AppendLine().Append(details);
            }

            builder.AppendLine().AppendLine();

            lock (SyncRoot)
            {
                File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
