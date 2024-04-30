using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using RavenM.Lobby.DataTransfer;
using Steamworks;

namespace RavenM.Lobby;

public static class SteamLobbyDataTransfer
{
    const string LongFieldMarker = "~~Long Field ~~";

    private class FailedSendData
    {
        public KeyValuePair<string, string> KeyValuePair = new();
        public List<string> UnsentChunks = null;
        public int lastSentIndex = -1;
    }
    /// <summary>
    /// Used for keeping track of SendLobbyData's results so we can know what data actually got sent and what has failed so far.
    /// SendLobbyMemberData does not have the same feedback so we'll not do the same for it.
    /// </summary>
    private class LobbySendData
    {
        public Dictionary<string, string> Cache = [];

        public ConcurrentQueue<string> DataToWrite = [];
        public FailedSendData FailedSend = null;
    }

    private static LobbySendData lobbyData = new();
    private static Dictionary<string, string> memberCache = [];

    public static bool ImportFromLobbyData<T>(CSteamID lobbyID, out T ret, string dataPrefix = null) where T : new()
    {
        string importDelegate (string dataKey)
        {
            string fullDataKey = GenericDataTransfer.HandlePrefix(dataPrefix, dataKey);
            string data = SteamMatchmaking.GetLobbyData(lobbyID, fullDataKey);

            // LoggingHelper.LogMarker($"Importing {fullDataKey} - {data}", false);
            return data;
        }

        return GenericDataTransfer.ImportFrom(out ret, importDelegate);
    }

    public static bool ImportFromMemberData<T>(CSteamID lobbyID, CSteamID memberID, out T ret, string dataPrefix = null) where T : new()
    {
        return GenericDataTransfer.ImportFrom(
            out ret,
            (dataKey) => SteamMatchmaking.GetLobbyMemberData(lobbyID, memberID, GenericDataTransfer.HandlePrefix(dataPrefix, dataKey)));
    }

    public static void ExportToLobbyData<T>(CSteamID lobbyID, T classToExport, string dataPrefix = null) where T : new()
    {
        const string logPrepend = "Exporting Lobby Data";
        // Because a lobby data send may fail we need to keep better track of what has been sent and what hasn't been so we don't just immediately write to steam here.
        ExportDataDelegate exportDelegate = (exportKey, exportValue) =>
        {
            lobbyData.Cache[exportKey] = exportValue;
            lobbyData.DataToWrite.Enqueue(exportKey);
            //SendWrapper(lobbyID, exportKey, exportValue);
        };
        GenericDataTransfer.ExportTo(
            classToExport,
            (dataKey, dataValue) => CachedExport( // Really nasty looking but let's us completely reuse any system between lobby data and member data.
                lobbyData.Cache,
                logPrepend,
                GenericDataTransfer.HandlePrefix(dataPrefix, dataKey),
                dataValue,
                exportDelegate));
    }

    public static void ExportToMemberData<T>(CSteamID lobbyID, T classToExport, string dataPrefix = null) where T : new()
    {
        const string logPrepend = "Exporting Lobby Member Data";
        // Unlike lobby data we can just send this right away on off to steam.
        void exportDelegate(string exportKey, string exportValue) => SteamMatchmaking.SetLobbyMemberData(lobbyID, exportKey, exportValue);
        GenericDataTransfer.ExportTo(
            classToExport,
            (dataKey, dataValue) => CachedExport(
                memberCache,
                logPrepend,
                GenericDataTransfer.HandlePrefix(dataPrefix, dataKey),
                dataValue,
                exportDelegate));
    }

    private static void CachedExport(Dictionary<string, string> cache, string logPrepend, string dataKey, string dataValue, ExportDataDelegate exportDelegate)
    {
        // Check the cache, if we don't have it at all or it doesn't match, then we actually mark it for export.
        if (!cache.TryGetValue(dataKey, out string cachedValue) || cachedValue != dataValue)
        {
            cache[dataKey] = dataValue;
            string logKey = $"{logPrepend} {dataKey}";
            LoggingHelper.ThrottledLogInfo(logKey, $"{logKey} - {dataValue}");
            exportDelegate(dataKey, dataValue);
        }
    }

    public static bool HasQueuedData()
    {
        if (lobbyData.FailedSend != null || lobbyData.DataToWrite.Count != 0)
        {
            return true;
        }

        return false;
    }

    public static bool SendQueuedDataToSteam(CSteamID lobbyID)
    {
        // We failed whilst sending something previously, let's clean that up first.
        if (lobbyData.FailedSend != null)
        {
            // TODO: Add a system so if it fails too much we just stop trying and tell the user.

            LoggingHelper.LogMarker($"Sending Failed data {lobbyData.FailedSend.KeyValuePair}");

            if (SendLobbyData(lobbyID, lobbyData.FailedSend) == false)
            {
                return false; // We failed to finish sending the data again, keep trying.
            }

            lobbyData.FailedSend = null;
        }

        while (lobbyData.DataToWrite.Count > 0)
        {
            if (!lobbyData.DataToWrite.TryDequeue(out string key))
            {
                LoggingHelper.LogMarker("Failed");
                return true; // We failed to pull right now but no reason not to keep trying next update.
            }

            string value = lobbyData.Cache[key];
            LoggingHelper.LogMarker($"Attempting to send data: key = {key}, value = {value}");
            lobbyData.FailedSend = new FailedSendData();
            lobbyData.FailedSend.KeyValuePair = new (key, lobbyData.Cache[key]);

            if (SendLobbyData(lobbyID, lobbyData.FailedSend) == false)
            {
                LoggingHelper.LogMarker("Failed");
                return false; // We failed to finish sending this
            }

            lobbyData.FailedSend = null;
        }

        return true;
    }

    private static bool SendLobbyData(CSteamID lobbyID, FailedSendData data)
    {
        const int MaxSendableLength = Constants.k_cubChatMetadataMax;
        string key = data.KeyValuePair.Key;
        string value = data.KeyValuePair.Value;

        if (value.Length < MaxSendableLength)
        {
            if (!SendWrapper(lobbyID, key, value))
            {
                return false; // We failed to send via steam so we need to take a break and not send for a bit.
            }

            return true;
        }

        // TODO: This code is completely irrelevant now. We need to completely rework this now that we understand what the MaxSendableLength actually represents.
        LoggingHelper.LogMarker("This should never happen!");

        // Too much data to fit in one field, split it up.
        if (data.UnsentChunks == null)
        {
            data.UnsentChunks = new (Enumerable.Range(0, value.Length / MaxSendableLength).Select(i => value.Substring(i * MaxSendableLength, MaxSendableLength)));
            LoggingHelper.LogMarker($"Chunk Count = {data.UnsentChunks.Count}");
        }

        LoggingHelper.LogMarker($"(data.lastSentIndex {data.lastSentIndex} < data.UnsentChunks.Count {data.UnsentChunks.Count})");
        while (data.lastSentIndex < data.UnsentChunks.Count)
        {
            string subKey;
            string subValue;
            if (data.lastSentIndex == -1)
            {
                subKey = key;
                subValue = $"{LongFieldMarker} {data.UnsentChunks.Count}";
            }
            else
            {
                subKey = $"{key}.{data.lastSentIndex}";
                subValue = data.UnsentChunks[data.lastSentIndex];
            }
            LoggingHelper.LogMarker($"key = {subKey}, value = {subValue}");

            if (!SendWrapper(lobbyID, subKey, subValue))
            {
                return false; // We failed to send via steam so we need to take a break and not send for a bit.
            }

            data.lastSentIndex++;
        }

        return true;
    }

    private static bool SendWrapper(CSteamID lobbyID, string key, string value)
    {
        bool sentSuccesfully =
             SteamMatchmaking.SetLobbyData(lobbyID, key, value);
        LoggingHelper.LogMarker($"key = {key}, value = {value}, Sent successfully = {sentSuccesfully}", false);
        if (!sentSuccesfully)
        {
            return false; // We failed to send via steam so we need to take a break and not send for a bit.
        }

        return true;
    }

    /// <summary>
    /// Debug log function. 
    /// Currently not hooked up but leaving it in as it's incredibly useful if the cache is suspected to be causing issues.
    /// </summary>
    public static void LogCache()
    {
        int length = 0;
        string cacheString = "";
        foreach (var kvp in lobbyData.Cache)
        {
            cacheString += $"{kvp.Key} - {kvp.Value}\n";
            length += kvp.Value.Length;
        }

        cacheString += $"Total Length - {length}";

        LoggingHelper.LogMarker(cacheString, false);
    }

    public static void ClearCache()
    {
        memberCache.Clear();
        lobbyData = new(); // TODO: This may not be thread safe to do but I don't actually think any of our code is currently so this is probably fine.
    }
}