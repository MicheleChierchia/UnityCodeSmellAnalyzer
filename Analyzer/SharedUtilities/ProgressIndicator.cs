using System;
using System.Threading;

namespace SharedUtilities
{
    public class ProgressIndicator : IDisposable
    {
        private readonly string[] _spinner = { "|", "/", "-", "\\" };
        private readonly Timer _timer;
        private readonly string _message;
        private bool _disposed = false;
        private int _counter = 0;

        public ProgressIndicator(string message, int updateIntervalMs = 100)
        {
            _message = message;
            _timer = new Timer(UpdateProgress, null, 0, updateIntervalMs);
        }

        private void UpdateProgress(object? state)
        {
            if (_disposed) return;

            _counter++;
            int spinnerIndex = _counter % _spinner.Length;
            Console.Write($"\r{_message} {_spinner[spinnerIndex]}");
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Dispose();
            Console.Write("\r");
            for (int i = 0; i < Console.WindowWidth; i++)
            {
                Console.Write(" ");
            }
            Console.Write("\r");
        }
    }

    public class ProgressBar : IDisposable
    {
        private readonly int _total;
        private int _current = 0;
        private readonly string _message;
        private readonly int _barWidth;
        private readonly ConsoleColor _color;

        public ProgressBar(string message, int total, int barWidth = 50, ConsoleColor color = ConsoleColor.Green)
        {
            _message = message;
            _total = total;
            _barWidth = barWidth;
            _color = color;
            Update(0);
        }

        public void Update(int progress)
        {
            _current = progress;
            int percent = (int)((double)_current / _total * 100);
            int completedLength = (int)((double)_current / _total * _barWidth);
            int remainingLength = _barWidth - completedLength;

            var originalColor = Console.ForegroundColor;
            try
            {
                Console.Write("\r");
                Console.Write($"{_message}: [");
                Console.ForegroundColor = _color;
                Console.Write(new string('=', completedLength));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(new string(' ', remainingLength));
                Console.ForegroundColor = originalColor;
                Console.Write($"] {percent}% ({_current}/{_total})");
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        public void Dispose()
        {
            Console.WriteLine();
        }
    }
}