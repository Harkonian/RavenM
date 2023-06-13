using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace RavenM.Lobby
{
    public static class DebugLoggingCache
    {
        public static bool ShouldLog = true;
        const float DefaultStaleSeconds = 10.0f;

        public class SimpleMessageHolder : IEquatable<SimpleMessageHolder>
        {
            public string Message { get; set; }
            
            public SimpleMessageHolder() { }

            public SimpleMessageHolder(string message)
            {
                Message = message;
            }

            public bool Equals(SimpleMessageHolder other)
            {
                return Message == other.Message;
            }
        }

        //Should really only be used for logging objects every frame while not actually spamming the log.
        public static void ExportToLog<T>(string key, T classToExport, float staleSecondsAllowed = DefaultStaleSeconds) where T : IEquatable<T>, new()
        {
            if (!ShouldLog)
                return;

            StringBuilder stringBuilder = new StringBuilder();

            GenericDataCache<string, T>.ExportTo(
                key,
                classToExport,
                (dataKey, dataValue) => stringBuilder.AppendLine($"{dataKey} - {dataValue}"), staleSecondsAllowed);

            if (stringBuilder.Length > 0)
            {
                Plugin.logger.LogInfo($"{key} : {DateTime.Now.ToString("HH:mm:ss:ff")} - {stringBuilder.ToString().TrimEnd()}");
            }
        }

        public static void ExportToLog<T>(T classToExport,
            float staleSecondsAllowed = DefaultStaleSeconds,
            [CallerMemberName] string funcName = "",
            [CallerLineNumber] int lineNumber = -1,
            [CallerFilePath] string path = ""
            ) where T : IEquatable<T>, new()
        {
            if (!ShouldLog)
                return;

            string key = $"{Path.GetFileName(path)}::{lineNumber}::{funcName}";
            ExportToLog(key, classToExport, staleSecondsAllowed);

        }
    }
}
