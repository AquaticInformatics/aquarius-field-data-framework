using System;
using FieldDataPluginFramework;

namespace Common
{
    public static class LogExtensions
    {
        // A few extensions to make the framework logger look a bit more like a log4net logger
        public static bool DebugEnabled { get; set; }

        public static void Debug(this ILog log, string message)
        {
            if (!DebugEnabled) return;

            log.Info($"DEBUG: {message}");
        }

        public static void Warn(this ILog log, string message)
        {
            log.Info($"WARN: {message}");
        }

        public static void Warn(this ILog log, string message, Exception exception)
        {
            log.Warn($"{message}: {SummarizeException(exception)}");
        }

        public static void Warn(this ILog log, Exception exception)
        {
            log.Warn(SummarizeException(exception));
        }

        public static void Error(this ILog log, string message, Exception exception)
        {
            log.Error($"{message}: {SummarizeException(exception)}");
        }

        public static void Error(this ILog log, Exception exception)
        {
            log.Error(SummarizeException(exception));
        }

        private static string SummarizeException(Exception exception)
        {
            return $"{exception.Message}\n{exception.StackTrace}";
        }
    }
}
