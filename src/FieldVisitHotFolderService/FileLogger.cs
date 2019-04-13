using System;
using System.Text;
using log4net;

namespace FieldVisitHotFolderService
{
    public class FileLogger
    {
        public ILog Log { get; set; }
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
