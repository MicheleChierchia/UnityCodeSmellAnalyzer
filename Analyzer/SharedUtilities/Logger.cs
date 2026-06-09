using System;
using System.IO;
using System.Text;

namespace SharedUtilities
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    public class Logger
    {
        private static readonly object _syncLock = new object();
        private static LogLevel _logLevel = LogLevel.Information;
        private static bool _verbose = false;
        private static bool _useColors = true;
        private static string? _logFilePath;
        private static StreamWriter? _logFileWriter;

        public static LogLevel CurrentLogLevel
        {
            get => _logLevel;
            set
            {
                _logLevel = value;
                if (_logLevel == LogLevel.None)
                {
                    _verbose = false;
                }
            }
        }

        public static bool Verbose
        {
            get => _verbose;
            set => _verbose = value;
        }

        public static bool UseColors
        {
            get => _useColors;
            set => _useColors = value;
        }

        public static void Initialize(string? logFilePath = null, LogLevel logLevel = LogLevel.Information, bool verbose = false, bool useColors = true)
        {
            _logLevel = logLevel;
            _verbose = verbose;
            _useColors = useColors;
            _logFilePath = logFilePath;

            if (!string.IsNullOrEmpty(logFilePath))
            {
                try
                {
                    string logDirectory = Path.GetDirectoryName(logFilePath);
                    if (string.IsNullOrEmpty(logDirectory))
                    {
                        logDirectory = ".";
                    }
                    Directory.CreateDirectory(logDirectory);
                    _logFileWriter = new StreamWriter(logFilePath, true, Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    Log(LogLevel.Information, "Logger initialized", "Logger");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error initializing logger: {ex.Message}");
                }
            }
        }

        public static void Log(LogLevel level, string message, string source = "General")
        {
            if (level < _logLevel) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logMessage = $"[{timestamp}] [{level}] [{source}] {message}";

            lock (_syncLock)
            {
                if (_verbose || level >= LogLevel.Warning)
                {
                    WriteToConsole(level, logMessage);
                }

                _logFileWriter?.WriteLine(logMessage);
            }
        }

        public static void LogException(Exception ex, string context = "Application")
        {
            string exceptionMessage = $"{ex.GetType().Name} in {context}: {ex.Message}";
            Log(LogLevel.Error, exceptionMessage);
            Log(LogLevel.Debug, ex.StackTrace ?? "No stack trace available");
        }

        public static void LogStructured(object data, string eventName = "Event")
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(data);
                Log(LogLevel.Information, $"{eventName}: {json}");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Failed to serialize structured log for {eventName}: {ex.Message}");
            }
        }

        private static void WriteToConsole(LogLevel level, string message)
        {
            if (!_useColors)
            {
                Console.WriteLine(message);
                return;
            }

            var originalColor = Console.ForegroundColor;
            try
            {
                switch (level)
                {
                    case LogLevel.Trace:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;
                    case LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        break;
                    case LogLevel.Information:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case LogLevel.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    case LogLevel.Critical:
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        break;
                }
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        public static void Close()
        {
            _logFileWriter?.Dispose();
            _logFileWriter = null;
        }
    }
}
