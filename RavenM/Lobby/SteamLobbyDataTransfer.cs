using BepInEx.Logging;
using Steamworks;
using System;
using System.CodeDom;
using System.Collections.Generic;

namespace RavenM.Lobby
{
    public static class SteamLobbyDataTransfer
    {
        const float DefaultStaleSeconds = 0.0f;

        private class LobbyCacheKey : IEquatable<LobbyCacheKey>
        {
            public CSteamID LobbyID { get; set; }
            public string Prefix { get; set; }

            public LobbyCacheKey(CSteamID steamID, string prefix) 
            {
                LobbyID = steamID;
                Prefix = prefix;
            }

            public bool Equals(LobbyCacheKey other) 
            { 
                return LobbyID == other.LobbyID && Prefix == other.Prefix;
            }
        }
        private class LobbyMemberCacheKey : LobbyCacheKey, IEquatable<LobbyMemberCacheKey>
        {
            public CSteamID MemberID { get; set; }

            public LobbyMemberCacheKey(CSteamID steamID, string prefix, CSteamID memberID) : base(steamID, prefix)
            {
                MemberID = memberID;
            }

            public bool Equals(LobbyMemberCacheKey other)
            {
                return base.Equals(other) && MemberID == other.MemberID;
            }
        }

        private static HashSet<Type> cachedTypes = new HashSet<Type>();

        public static bool ImportFromLobbyData<T>(CSteamID lobbyID, out T ret, float staleSecondsAllowed = DefaultStaleSeconds, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            return GenericDataTransfer.ImportFrom(
                out ret,
                (datakey) => SteamMatchmaking.GetLobbyData(lobbyID, GenericDataTransfer.HandlePrefix(dataPrefix, datakey)));
        }

        public static void ExportToLobbyData<T>(CSteamID lobbyID, T classToExport, float staleSecondsAllowed = DefaultStaleSeconds, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            GenericDataTransfer.ExportTo(
                classToExport,
                (dataKey, dataValue) => SteamMatchmaking.SetLobbyData(lobbyID, GenericDataTransfer.HandlePrefix(dataPrefix, dataKey), dataValue));
        }

        public static bool ImportFromMemberData<T>(CSteamID lobbyID, CSteamID memberID, out T ret, float staleSecondsAllowed = DefaultStaleSeconds, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            return GenericDataTransfer.ImportFrom(
                out ret,
                (datakey) => SteamMatchmaking.GetLobbyMemberData(lobbyID, memberID, GenericDataTransfer.HandlePrefix(dataPrefix, datakey)));
        }

        public static void ExportToMemberData<T>(CSteamID lobbyID, T classToExport, float staleSecondsAllowed = DefaultStaleSeconds, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            GenericDataTransfer.ExportTo(
                classToExport,
                (dataKey, dataValue) =>
                {
                    SteamMatchmaking.SetLobbyMemberData(lobbyID, GenericDataTransfer.HandlePrefix(dataPrefix, dataKey), dataValue);
                });
        }
    }
}
