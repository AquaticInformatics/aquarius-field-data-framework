using System;
using System.Text;
using log4net;

using Log4NetILog = log4net.ILog;
using PluginILog = FieldDataPluginFramework.ILog;

namespace FieldVisitHotFolderService
{
    public class FileLogger : PluginILog
    {
        public FileLogger(Log4NetILog log)
        {
            Log = log;
        }

        private ILog Log { get; }
        private StringBuilder Builder { get; } = new StringBuilder();

        public void Info(string message)
        {
            Log.Info(message);

            Append("INFO", message);
        }

        public void Warn(string message)
        {
            Log.Warn(message);

            Append("WARN", message);
        }

        public void Warn(string message, Exception exception)
        {
            Log.Warn(message, exception);

            Append("WARN", message, exception);
        }

        public void Error(string message)
        {
            Log.Error(message);

            Append("ERROR", message);
        }

        private void Append(string label, string message, Exception exception = null)
        {
            Builder.Append($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fffzzz} {label,-5} - {message}");

            if (exception != null)
            {
                Builder.Append($": {exception.Message}");
            }

            Builder.AppendLine();
        }

        public string AllText()
        {
            return Builder.ToString();
        }
    }
}
