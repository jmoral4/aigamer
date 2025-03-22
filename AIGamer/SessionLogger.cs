
namespace AIGamer
{
    using System;
    using System.IO;
    using System.Text;

    public class SessionLogger
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();
        private bool _isEnabled;

        public SessionLogger(string sessionName = null)
        {
            // Create logs directory if it doesn't exist
            string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDirectory);

            // Generate log filename with timestamp
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logFileName = string.IsNullOrEmpty(sessionName)
                ? $"warsim_session_{timestamp}.log"
                : $"warsim_session_{sessionName}_{timestamp}.log";

            _logFilePath = Path.Combine(logsDirectory, logFileName);
            _isEnabled = true;

            // Write log header
            LogMessage($"=== WARSIM AI PLAYER SESSION LOG ===");
            LogMessage($"Session started: {DateTime.Now}");
            LogMessage($"====================================");
            LogMessage(string.Empty);
        }

        public void LogGameState(string gameState)
        {
            if (!_isEnabled) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("------ GAME STATE ------");
            sb.AppendLine($"[{DateTime.Now.ToString("HH:mm:ss")}]");
            sb.AppendLine(gameState);
            sb.AppendLine("------------------------");

            LogMessage(sb.ToString());
        }

        public void LogAIAction(string action, string rawResponse = null)
        {
            if (!_isEnabled) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("------ AI ACTION -------");
            sb.AppendLine($"[{DateTime.Now.ToString("HH:mm:ss")}]");
            sb.AppendLine($"Action: {action}");

            if (!string.IsNullOrEmpty(rawResponse))
            {
                sb.AppendLine();
                sb.AppendLine("Raw Response:");
                sb.AppendLine(rawResponse);
            }

            sb.AppendLine("------------------------");

            LogMessage(sb.ToString());
        }

        public void LogError(string errorMessage)
        {
            if (!_isEnabled) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("------ ERROR -------");
            sb.AppendLine($"[{DateTime.Now.ToString("HH:mm:ss")}]");
            sb.AppendLine(errorMessage);
            sb.AppendLine("-------------------");

            LogMessage(sb.ToString());
        }

        public void LogMessage(string message)
        {
            if (!_isEnabled) return;

            lock (_lockObject)
            {
                try
                {
                    File.AppendAllText(_logFilePath, message + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to log file: {ex.Message}");
                    // Disable logging if we encounter an error to prevent further exceptions
                    _isEnabled = false;
                }
            }
        }

        public void LogSystemEvent(string eventMessage)
        {
            if (!_isEnabled) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[SYSTEM] [{DateTime.Now.ToString("HH:mm:ss")}] {eventMessage}");

            LogMessage(sb.ToString());
        }

        public string GetLogFilePath()
        {
            return _logFilePath;
        }
    }
}
