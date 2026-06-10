using System.Collections.ObjectModel;

namespace SaturdayPulse.Helpers
{
    /// <summary>
    /// In-memory logger for on-device diagnostics.
    /// Accessible from the Debug Log section in Settings.
    /// 
    /// Usage: AppLogger.Log("message")
    ///        AppLogger.Log("message", LogLevel.Error)
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _lock = new();
        private const int MaxEntries = 500;

        public static ObservableCollection<LogEntry> Entries { get; } = new();

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level     = level,
                Message   = message
            };

            lock (_lock)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Entries.Insert(0, entry); // newest first
                    while (Entries.Count > MaxEntries)
                        Entries.RemoveAt(Entries.Count - 1);
                });
            }

            // Also write to debug output for when debugger is attached
            System.Diagnostics.Debug.WriteLine($"[{level}] {entry.Timestamp:HH:mm:ss.fff} {message}");
        }

        public static void Clear()
        {
            MainThread.BeginInvokeOnMainThread(() => Entries.Clear());
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level     { get; set; }
        public string   Message   { get; set; } = string.Empty;

        public string Display => $"{Timestamp:HH:mm:ss.fff} [{Level}] {Message}";

        public string LevelColor => Level switch
        {
            LogLevel.Error   => "#FF4444",
            LogLevel.Warning => "#FFB344",
            _                => "#AAAAAA"
        };
    }

    public enum LogLevel { Info, Warning, Error }
}
