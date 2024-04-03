using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RavenM.Lobby
{
    internal static class LoggingHelper
    {
        private static Dictionary<string, DateTime> LastSentTimes = [];
        private static readonly TimeSpan TimeBeforeResend = TimeSpan.FromSeconds(1);

        const bool Enabled = true;

        public static void ThrottledLogInfo(string message, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0)
        {
            string key = $"{memberName}::{lineNumber}";
            ThrottledLogInfo(key, message);
        }

        public static void ThrottledLogInfo(string key, string message)
        {
            if (Enabled == false)
            {
                return;
            }

            if (LastSentTimes.TryGetValue(key, out DateTime lastSent))
            {
                if (DateTime.Now - lastSent < TimeBeforeResend)
                {
                    return;
                }
            }

            LogInfo($"{key} - {message}");
            LastSentTimes[key] = DateTime.Now;
        }

        public static void LogMarker(string data = null, bool throttle = true, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0)
        {
            string key = $"{memberName}::{lineNumber}";
            string output = key;

            if (data != null)
            {
                output += $" {data}";
            }
            
            if (throttle)
            {
                ThrottledLogInfo(key, output);
            }
            else
            {
                LogInfo(output);
            }
        }

        private static void LogInfo(string output)
        {
            Plugin.logger.LogInfo($"{DateTime.Now.ToString("hh:mm:ss.ff", CultureInfo.InvariantCulture)}:{output}");
        }
    }
}
