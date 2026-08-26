using System.Diagnostics;

namespace PlaywrightSmartRecorder.Core.Services
{
    public class LogService
    {
        private readonly string _logFolder;

        public LogService()
        {
            _logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TestConverter", "Logs");
            Directory.CreateDirectory(_logFolder);
        }

        public void LogError(string message, Exception? ex = null)
        {
            try
            {
                string logFile = Path.Combine(_logFolder, $"log_{DateTime.Now:yyyyMMdd}.txt");
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}" + 
                                   (ex != null ? $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}" : "") + 
                                   "\n--------------------------------------------------\n";
                File.AppendAllText(logFile, logContent);
            }
            catch { }
        }

        public void OpenLogFolder()
        {
            try
            {
                if (Directory.Exists(_logFolder))
                {
                    Process.Start(new ProcessStartInfo(_logFolder) { UseShellExecute = true });
                }
            }
            catch { }
        }
    }
}