using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace RavenM.Lobby
{
    internal class PeriodicDataTransfer
    {
        internal class TimedCoroutine
        {
            public delegate void UpdateDelegate();

            private UpdateDelegate updateDelegate;
            private float periodInSeconds;

            public IEnumerator Coroutine()
            {
                while (true)
                {
                    updateDelegate();
                    yield return new WaitForSeconds(periodInSeconds);
                }
            }

            public TimedCoroutine(UpdateDelegate updateDelegate, float periodInSeconds = 1.0f)
            {
                this.periodInSeconds = periodInSeconds;
                this.updateDelegate = updateDelegate;
            }

        }

        List<Coroutine> coroutines = new List<Coroutine>();
        private MonoBehaviour gameObject;

        public delegate T DataExportDelegate<T>();
        public delegate void DataImportDelegate<T>(T data);

        public PeriodicDataTransfer(MonoBehaviour gameObject)
        {
            this.gameObject = gameObject;
        }

        public Coroutine StartPeriodicLobbySend<T>(float periodInSeconds, CSteamID lobbyID, DataExportDelegate<T> exportDelegate, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            Coroutine ret = null;
            TimedCoroutine coroutine = new TimedCoroutine(
                () =>
                {
                    T dataObject = exportDelegate();
                    if (dataObject != null)
                        SteamLobbyDataTransfer.ExportToLobbyData(lobbyID, dataObject);
                    else
                        Plugin.logger.LogError($"Periodic send for type '{typeof(T)}' was null");
                },
                periodInSeconds);


            ret = gameObject.StartCoroutine(coroutine.Coroutine());
            coroutines.Add(ret);

            return ret;
        }

        public Coroutine StartPeriodicLobbyRead<T>(float periodInSeconds, CSteamID lobbyID, DataImportDelegate<T> updateDelegate, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            Coroutine ret = null;
            TimedCoroutine coroutine = new TimedCoroutine(
                () =>
                {
                    T lobbyData;
                    // TODO : Think about actually checking the return value here, genius
                    SteamLobbyDataTransfer.ImportFromLobbyData(lobbyID, out lobbyData, -1.0f, dataPrefix);
                    updateDelegate(lobbyData);
                },
                periodInSeconds);

            ret = gameObject.StartCoroutine(coroutine.Coroutine());
            coroutines.Add(ret);

            return ret;
        }

        public Coroutine StartPeriodicLobbyMemberSend<T>(float periodInSeconds, CSteamID lobbyID, DataExportDelegate<T> exportDelegate, string dataPrefix = null) where T : IEquatable<T>, new()
        {
            Coroutine ret = null;
            TimedCoroutine coroutine = new TimedCoroutine(
                () =>
                {
                    T dataObject = exportDelegate();
                    if (dataObject != null)
                        SteamLobbyDataTransfer.ExportToMemberData(lobbyID, dataObject);
                    else
                        Plugin.logger.LogError($"Periodic send for type '{typeof(T)}' was null");
                },
                periodInSeconds);


            ret = gameObject.StartCoroutine(coroutine.Coroutine());
            coroutines.Add(ret);

            return ret;
        }

        public void Clear()
        {
            foreach (Coroutine coroutine in coroutines)
            {
                gameObject.StopCoroutine(coroutine);
            }

            coroutines.Clear();
        }
    }
}
