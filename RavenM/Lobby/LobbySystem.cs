using HarmonyLib;
using RavenM.RSPatch.Wrapper;
using SimpleJSON;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RavenM.Lobby
{
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.OnGameManagerStart))]
    public class CleanupListPatch
    {
        static void Prefix(ModManager __instance)
        {
            if (__instance.noContentMods)
                __instance.noWorkshopMods = true;
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartLevel))]
    public class OnStartPatch
    {
        static bool Prefix()
        {
            LobbySystem instance = LobbySystem.instance;

            if (instance.InLobby && !instance.IsLobbyOwner && !instance.ReadyToPlay)
                return false;

            OptionsPatch.SetConfigValues(false);

            // Only start if all members are ready.
            if (instance.LobbyDataReady && instance.IsLobbyOwner)
            {
                foreach (var memberId in instance.GetLobbyMembers())
                {
                    if (!SteamLobbyDataTransfer.ImportFromMemberData(instance.LobbyID, memberId, out LobbyMemberData memberData) || memberData.Loaded == false)
                    {
                        if (!LobbySystem.instance.HasCommittedToStart)
                        {
                            LobbySystem.instance.IntentionToStart = true;
                            return false;
                        }
                    }
                }
                LobbySystem.instance.HasCommittedToStart = false;
            }

            instance.ResetOnTeamChanged();

            if (instance.IsLobbyOwner)
            {
                IngameNetManager.instance.OpenRelay();

                Plugin.logger.LogInfo($"{DateTime.Now:HH:mm:ss:ff} - Set LobbyStarted True");
                instance.SetLobbyData("started", "yes");
                instance.MatchSettings.MatchStarted = true;
                
            }

            instance.ReadyToPlay = false;
            return true;
        }
    }

    [HarmonyPatch(typeof(LoadoutUi), nameof(LoadoutUi.OnDeployClick))]
    public class FirstDeployPatch
    {
        static bool Prefix()
        {
            if (LobbySystem.instance.InLobby)
            {
                // TODO: Does this do what that comment says? I don't think so.
                // Ignore players who joined mid-game.
                if ((bool)typeof(LoadoutUi).GetField("hasAcceptedLoadoutOnce", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(LoadoutUi.instance))
                    return true;

                // Wait for everyone to load in first.
                foreach (var memberId in LobbySystem.instance.GetLobbyMembers())
                {
                    if (SteamLobbyDataTransfer.ImportFromMemberData(LobbySystem.instance.LobbyID, memberId, out LobbyMemberData memberData))
                    {
                        if (!memberData.Ready && memberData.Loaded)
                            return false;
                    }
                    else
                    {
                        // There's something wrong with one of the user's data, for now let's wait. GOAL - Perhaps set a timer before kicking them?
                        return false;
                    }
                }
            }
            if (IngameNetManager.instance.IsHost || LobbySystem.instance.IsLobbyOwner)
            {
                Plugin.logger.LogInfo("SendNetworkGameObjectsHashesPacket()");
                WLobby.SendNetworkGameObjectsHashesPacket();
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameManager), "StartGame")]
    public class FinalizeStartPatch
    {
        // Maps sometimes have their own vehicles. We need to tag them.
        static void Prefix()
        {
            if (!LobbySystem.instance.InLobby)
                return;

            // The game will destroy any vehicles that have already spawned. Ignore them.
            var ignore = UnityEngine.Object.FindObjectsOfType<Vehicle>();
            Plugin.logger.LogInfo($"Ignore list: {ignore.Length}");

            var map = GameManager.instance.lastMapEntry;

            foreach (var vehicle in Resources.FindObjectsOfTypeAll<Vehicle>())
            {
                if (!vehicle.TryGetComponent(out PrefabTag _) && !Array.Exists(ignore, x => x == vehicle))
                {
                    Plugin.logger.LogInfo($"Detected map vehicle with name: {vehicle.name}, and from map: {map.name}.");

                    var tag = vehicle.gameObject.AddComponent<PrefabTag>();
                    tag.NameHash = vehicle.name.GetHashCode();
                    tag.Mod = (ulong)map.name.GetHashCode();
                    IngameNetManager.PrefabCache[new Tuple<int, ulong>(tag.NameHash, tag.Mod)] = vehicle.gameObject;
                }
            }

            foreach (var projectile in Resources.FindObjectsOfTypeAll<Projectile>())
            {
                if (!projectile.TryGetComponent(out PrefabTag _))
                {
                    Plugin.logger.LogInfo($"Detected map projectile with name: {projectile.name}, and from map: {map.name}.");

                    var tag = projectile.gameObject.AddComponent<PrefabTag>();
                    tag.NameHash = projectile.name.GetHashCode();
                    tag.Mod = (ulong)map.name.GetHashCode();
                    IngameNetManager.PrefabCache[new Tuple<int, ulong>(tag.NameHash, tag.Mod)] = projectile.gameObject;
                }
            }

            foreach (var destructible in Resources.FindObjectsOfTypeAll<Destructible>())
            {
                var prefab = DestructiblePacket.Root(destructible);

                if (!prefab.TryGetComponent(out PrefabTag _))
                {
                    Plugin.logger.LogInfo($"Detected map destructible with name: {prefab.name}, and from map: {map.name}.");

                    IngameNetManager.TagPrefab(prefab, (ulong)map.name.GetHashCode());
                }
            }

            foreach (var destructible in UnityEngine.Object.FindObjectsOfType<Destructible>())
            {
                // One shot created destructibles -- not cool!
                var root = DestructiblePacket.Root(destructible);

                if (IngameNetManager.instance.ClientDestructibles.ContainsValue(root))
                    continue;

                // FIXME: Shitty hack. The assumption is map destructibles are consistent
                // and thus will always spawn in the same positions regardless of the
                // client run. I have no idea how correct this assumption actually is.
                int id = root.transform.position.GetHashCode() ^ root.name.GetHashCode();

                if (root.TryGetComponent(out GuidComponent guid))
                    id = guid.guid;
                else
                    root.AddComponent<GuidComponent>().guid = id;

                IngameNetManager.instance.ClientDestructibles[id] = root;

                Plugin.logger.LogInfo($"Registered new destructible root with name: {root.name} and id: {id}");
            }
        }

        static void Postfix()
        {
            Plugin.logger.LogInfo("Entered FinalizeStartPatch PostFix");

            LobbySystem instance = LobbySystem.instance;

            if (!instance.LobbyDataReady)
                return;

            if (instance.IsLobbyOwner)
                IngameNetManager.instance.StartAsServer();
            else
                IngameNetManager.instance.StartAsClient(instance.OwnerID);

            Plugin.logger.LogInfo("Set Ready");
            instance.LocalMemberData.Ready = true;
        }
    }

    [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.GoBack))]
    public class GoBackPatch
    {
        static bool Prefix()
        {
            if (LobbySystem.instance.InLobby && (int)typeof(MainMenu).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MainMenu.instance) == MainMenu.PAGE_INSTANT_ACTION)
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(InstantActionMaps), "SetupSkinList")]
    public class SkinListPatch
    {
        static void Prefix() => ModManager.instance.actorSkins.Sort((x, y) => x.name.CompareTo(y.name));
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.FinalizeLoadedModContent))]
    public class AfterModsLoadedPatch
    {
        static void Postfix()
        {
            if (InstantActionMaps.instance != null)
            {
                // We need to update the skin dropdown with the new mods.
                typeof(InstantActionMaps).GetMethod("SetupSkinList", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(InstantActionMaps.instance, null);
            }

            ModManager.instance.ContentChanged();

            LobbySystem lobbySystem = LobbySystem.instance;

            if (!lobbySystem.InLobby || !lobbySystem.LobbyDataReady || lobbySystem.IsLobbyOwner || lobbySystem.ModsToDownload.Count > 0)
                return;

            LobbySystem.instance.CompletedLoading();
        }
    }

    [HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.LeaveLobby))]
    public class OnLobbyLeavePatch
    {
        static void Postfix()
        {
            Plugin.logger.LogInfo("OnLobbyLeavePatch");
            LobbySystem.instance.Reset();
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReturnToMenu))]
    public class LeaveOnEndGame
    {
        static void Prefix()
        {
            if (!LobbySystem.instance.InLobby || LobbySystem.instance.IsLobbyOwner)
                return;

            // Exit the lobby if we actually want to leave.
            if (new StackFrame(2).GetMethod().Name == "Menu")
            {
                LobbySystem.instance.BeginLeavingLobby();
            }
        }

        static void Postfix()
        {
            LobbySystem instance = LobbySystem.instance;

            if (!instance.InLobby)
                return;

            if (instance.IsLobbyOwner)
            {
                instance.SetLobbyData("started", "false");
                instance.MatchSettings.MatchStarted = false;
            }

            instance.LocalMemberData.Ready = false;
        }
    }

    [HarmonyPatch(typeof(InstantActionMaps), "Awake")]
    public class InstantActionMenuAwake
    {
        static void Postfix(InstantActionMaps __instance)
        {
            Plugin.logger.LogInfo("InstantActionMenuAwake");
            LobbySystem lobbyInstance = LobbySystem.instance;
            lobbyInstance.RegisterOnTeamChangeListener();
            lobbyInstance.MapCache?.UpdateCacheFromIAM(__instance);
        }
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.GetActiveMods))]
    public class ActiveModsPatch
    {
        static bool Prefix(ModManager __instance, ref List<ModInformation> __result)
        {
            if (LobbySystem.instance.LoadedServerMods && LobbySystem.instance.ServerMods.Count > 0)
            {
                __result = new List<ModInformation>();
                foreach (var mod in __instance.mods)
                {
                    if (LobbySystem.instance.ServerMods.Contains(mod.workshopItemId))
                    {
                        __result.Add(mod);
                    }
                }
                return false;
            }

            return true;
        }
    }

    public class LobbySystem : MonoBehaviour
    {
        private bool needsTeamChangeRegister = true;

        public static LobbySystem instance;

        public bool InLobby { get; private set; } = false;

        public bool LobbyDataReady { get; private set; } = false;

        public CSteamID LobbyID = CSteamID.Nil;

        public CSteamID OwnerID = CSteamID.Nil;

        public bool IsLobbyOwner { get; private set; } = false;

        public bool ReadyToPlay = false;

        public List<PublishedFileId_t> ServerMods = new();

        public List<PublishedFileId_t> ModsToDownload = new();

        public bool LoadedServerMods { get; private set; } = false;

        public bool RequestModReload { get; private set; } = false;

        public Dictionary<CSteamID, ServerSettings> OpenLobbies = new();

        public List<CSteamID> CurrentKickedMembers = new();

        public List<GameObject> sortedModdedVehicles = new();

        public Dictionary<string, string> LobbySetCache = new();

        public ServerSettings ServerSettings = null;

        public LobbyMemberData LocalMemberData = null;

        public MatchSettings MatchSettings = null;

        private PeriodicDataTransfer periodicTransfer;

        public CachedMapData MapCache { get; private set; } = null;

        public LobbyGUI GUI = null;

        private readonly List<Coroutine> coroutines = new();

        public bool MatchSubscriptionsToServer = false;

        public bool IntentionToStart = false;

        public bool HasCommittedToStart = false;

        public void SendMatchSettings()
        {
            if (MatchSettings == null)
            {
                return;
            }

            DebugLoggingCache.ExportToLog(MatchSettings);
            SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, MatchSettings);
        }

        public void SendServerSettings()
        {
            if (ServerSettings == null)
            {
                return;
            }

            DebugLoggingCache.ExportToLog(ServerSettings);
            SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, ServerSettings);
        }

        public void ReadServerSettings()
        {
            if (SteamLobbyDataTransfer.ImportFromLobbyData(LobbyID, out ServerSettings))
                DebugLoggingCache.ExportToLog(ServerSettings);
            else
            {
                Plugin.logger.LogError("Failed to read server settings after joining a server. How the hell did this happen?");
            }
        }

        public void ReadMatchSettings()
        {
            if (SteamLobbyDataTransfer.ImportFromLobbyData(LobbyID, out MatchSettings))
                DebugLoggingCache.ExportToLog(ServerSettings);
            else
            {
                Plugin.logger.LogError("Failed to read match settings after joining a lobby. How the hell did this happen?");
            }
        }

        public void CompletedLoading()
        {
            if (DebugLoggingCache.ShouldLog)
                Plugin.logger.LogInfo("Loading completed after joining this lobby!");

            MapCache = new CachedMapData(InstantActionMaps.instance);
            LocalMemberData.Loaded = true;

            if (IsLobbyOwner)
            {
                MatchSettings.PopulateData(InstantActionMaps.instance, MapCache.Maps);

                //SendMatchSettings();
                //SendServerSettings();
                coroutines.Add(StartCoroutine(new PeriodicDataTransfer.TimedCoroutine(SendMatchSettings).Coroutine()));
                coroutines.Add(StartCoroutine(new PeriodicDataTransfer.TimedCoroutine(SendServerSettings).Coroutine()));
            }
            else
            {
                coroutines.Add(StartCoroutine(new PeriodicDataTransfer.TimedCoroutine(ReadMatchSettings).Coroutine()));
                coroutines.Add(StartCoroutine(new PeriodicDataTransfer.TimedCoroutine(ReadServerSettings).Coroutine()));
            }

            // Sort vehicles
            var moddedVehicles = ModManager.AllVehiclePrefabs().ToList();
            moddedVehicles.Sort((x, y) => x.name.CompareTo(y.name));
            LobbySystem.instance.sortedModdedVehicles = moddedVehicles;

            // Sort mutators
            ModManager.instance.loadedMutators.Sort((x, y) => x.name.CompareTo(y.name));
        }

        private void Awake()
        {
            instance = this;
            periodicTransfer = new PeriodicDataTransfer(this);
        }

        private void Start()
        {
            Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            Callback<DownloadItemResult_t>.Create(OnItemDownload);
            Callback<LobbyMatchList_t>.Create(OnLobbyList);
            Callback<LobbyDataUpdate_t>.Create(OnLobbyData);
        }

        public void BeginLeavingLobby(string notification = null, bool leaveSteam = true)
        {
            if (notification != null)
            {
                SetNotification(notification);
                if (DebugLoggingCache.ShouldLog)
                    Plugin.logger.LogInfo($"{notification}");
            }

            Plugin.logger.LogInfo("BeginLeavingLobby");

            if (leaveSteam)
                SteamMatchmaking.LeaveLobby(LobbyID);
            else
                Reset();

            InLobby = false;
        }

        protected void StartedLoading()
        {
            Plugin.logger.LogInfo("StartedLoading");
            Reset();
            RequestModReload = false; // Reset normally sets this to true but we don't actually want a mod reload on first joining a lobby.
            LoadedServerMods = false;

            MapCache = null;
            LocalMemberData = new LobbyMemberData();

            periodicTransfer.StartPeriodicLobbyMemberSend(1.0f, LobbyID, () =>
            {
                LocalMemberData.ServerModsNeeded = ModsToDownload.Count();
                return LocalMemberData;
            });
        }

        public void Reset()
        {
            LocalMemberData = null;
            MatchSettings = null;

            LobbyDataReady = false;
            ReadyToPlay = true;

            if (LoadedServerMods)
                RequestModReload = true;

            ModsToDownload.Clear();
            ServerMods.Clear();
            CurrentKickedMembers.Clear();
            LobbySetCache.Clear();

            foreach(Coroutine coroutine in coroutines)
            {
                StopCoroutine(coroutine);
            }
            coroutines.Clear(); 
            periodicTransfer.Clear();
        }

        public void HostLobby(ServerSettings settings)
        {
            ServerSettings = settings;
            SteamMatchmaking.CreateLobby(settings.FriendsOnlyLobby ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic, (int)settings.LobbyMemberCap);
            InLobby = true;
            IsLobbyOwner = true;
        }

        public void AttemptToJoinLobby(uint lobbyId)
        {
            const EChatSteamIDInstanceFlags lobbyFlags = EChatSteamIDInstanceFlags.k_EChatInstanceFlagLobby | EChatSteamIDInstanceFlags.k_EChatInstanceFlagMMSLobby;
            CSteamID steamLobbyId = new(new AccountID_t(lobbyId), (uint)lobbyFlags, EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeChat);
            AttemptToJoinLobby(steamLobbyId);
        }

        public void AttemptToJoinLobby(CSteamID lobbyId)
        {
            IsLobbyOwner = false;
            SteamMatchmaking.JoinLobby(lobbyId);
            InLobby = true;
        }

        public void KickUser(CSteamID userId)
        {
            ChatManager.instance.SendLobbyChat($"/kick {userId}");
            CurrentKickedMembers.Add(userId);
            foreach (var connection in IngameNetManager.instance.ServerConnections)
            {
                if (SteamNetworkingSockets.GetConnectionInfo(connection, out SteamNetConnectionInfo_t pInfo) && pInfo.m_identityRemote.GetSteamID() == userId)
                {
                    SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                }
            }
        }

        public void RefreshOpenLobbies()
        {
            OpenLobbies.Clear();
            SteamMatchmaking.RequestLobbyList();
        }

        public List<CSteamID> GetLobbyMembers()
        {
            int len = SteamMatchmaking.GetNumLobbyMembers(LobbyID);
            var ret = new List<CSteamID>(len);

            for (int i = 0; i < len; i++)
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex(LobbyID, i);
                if (!CurrentKickedMembers.Contains(member))
                    ret.Add(member);
            }

            return ret;
        }

        public void SetLobbyData(string key, string value)
        {
            SteamMatchmaking.SetLobbyData(LobbyID, key, value);
            return;


            if (!InLobby || !LobbyDataReady || !IsLobbyOwner)
                return;

            // De-dup any lobby values since apparently Steam doesn't do that for you.
            if (LobbySetCache.TryGetValue(key, out string oldValue) && oldValue == value)
                return;

            SteamMatchmaking.SetLobbyData(LobbyID, key, value);
            LobbySetCache[key] = value;
        }

        public bool MatchHasStarted()
        {
            return (MatchSettings != null && MatchSettings.MatchStarted) || SteamMatchmaking.GetLobbyData(LobbyID, "started") == "yes";
        }

        private void OnLobbyData(LobbyDataUpdate_t pCallback)
        {
            var lobby = new CSteamID(pCallback.m_ulSteamIDLobby);

            if (pCallback.m_bSuccess == 0 || SteamMatchmaking.GetLobbyDataCount(lobby) == 0)
                OpenLobbies.Remove(lobby); // Something is wrong with this lobby, either an error or it has no data at all.

            if (SteamLobbyDataTransfer.ImportFromLobbyData(lobby, out ServerSettings serverSettings) && serverSettings.IncludeInBrowseList)
                OpenLobbies[lobby] = serverSettings;
            else
                OpenLobbies.Remove(lobby);
        }

        private void SetupServerMods()
        {
            bool needsToReload = false;
            List<PublishedFileId_t> mods = new();

            foreach (var mod in ModManager.instance.GetActiveMods())
            {
                if (mod.workshopItemId == PublishedFileId_t.Invalid)
                {
                    mod.enabled = false;
                    needsToReload = true;

                    if (DebugLoggingCache.ShouldLog)
                    {
                        Plugin.logger.LogInfo($"{mod.title} - Failed to find matching workshop id. Disabling mod.");
                    }
                }
                else
                    mods.Add(mod.workshopItemId);
            }


            if (DebugLoggingCache.ShouldLog)
            {
                Plugin.logger.LogInfo($"Mod Reload Required : {needsToReload}");
            }

            if (needsToReload)
                ModManager.instance.ReloadModContent();

            SetLobbyData("mods", string.Join(",", mods.ToArray()));
        }

        private void MakeModsMatchServer()
        {
            string[] mods = SteamMatchmaking.GetLobbyData(LobbyID, "mods").Split(',');
            foreach (string mod_str in mods)
            {
                if (mod_str == string.Empty)
                    continue;
                PublishedFileId_t mod_id = new(ulong.Parse(mod_str));
                if (mod_id.ToString() == "0")
                    continue;

                ServerMods.Add(mod_id);

                bool alreadyHasMod = false;
                foreach (var mod in ModManager.instance.mods)
                {
                    if (mod.workshopItemId == mod_id)
                    {
                        alreadyHasMod = true;
                        break;
                    }
                }

                if (!alreadyHasMod)
                {
                    ModsToDownload.Add(mod_id);
                }
            }

            TriggerModRefresh();
        }

        private void OnLobbyEnter(LobbyEnter_t pCallback)
        {
            Plugin.logger.LogInfo("Joined lobby!");

            if (pCallback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                BeginLeavingLobby(Notifications.JoiningError, false);
                return;
            }

            LobbyID = new CSteamID(pCallback.m_ulSteamIDLobby);
            StartedLoading();
            LobbyDataReady = true;
            MatchSettings = new MatchSettings();

            if (IsLobbyOwner)
            {
                Plugin.logger.LogInfo("Attempting to start as host");
                OwnerID = SteamUser.GetSteamID();
                ServerSettings.OwnerID = OwnerID.m_SteamID;
                SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, ServerSettings);
                SetupServerMods();
                CompletedLoading();
            }
            else
            {
                Plugin.logger.LogInfo("Attempting to start as client");
                ServerSettings = OpenLobbies.ContainsKey(LobbyID) ? OpenLobbies[LobbyID] : null;

                if (ServerSettings == null && !SteamLobbyDataTransfer.ImportFromLobbyData(LobbyID, out ServerSettings))
                {
                    // Could not load server settings for current lobby id. This is either because the server suddenly disappeared
                    // or we couldn't recognize the info stored in steam's lobby system. Either way, just get out of here.
                    BeginLeavingLobby(Notifications.JoiningError);
                    return;
                }

                if (Plugin.BuildGUID != ServerSettings.BuildID)
                {
                    BeginLeavingLobby(Notifications.PluginMismatch);
                    return;
                }

                // TODO: If we allow starting without users (currently a feature on mainline) being ready then we'll need to kick any users who haven't finished loading mods yet as well.
                if (MatchHasStarted() && !ServerSettings.MidgameJoin)
                {
                    BeginLeavingLobby(Notifications.HotJoinDisabled);
                    return;
                }

                OwnerID = new CSteamID(ServerSettings.OwnerID);
                Plugin.logger.LogInfo($" Host ID: {OwnerID}");
                Plugin.logger.LogInfo($"Lobby ID: {LobbyID}");

                MainMenu.instance.OpenPageIndex(MainMenu.PAGE_INSTANT_ACTION);
                ReadyToPlay = false;

                MakeModsMatchServer();
            }

            ResetOnTeamChanged();
        }

        private void OnItemDownload(DownloadItemResult_t pCallback)
        {
            Plugin.logger.LogInfo($"Downloaded mod! {pCallback.m_nPublishedFileId}");
            if (ModsToDownload.Contains(pCallback.m_nPublishedFileId))
            {
                var mod = (ModInformation)typeof(ModManager).GetMethod("AddWorkshopItemAsMod", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ModManager.instance, new object[] { pCallback.m_nPublishedFileId });
                mod.hideInModList = true;
                mod.enabled = false;

                ModsToDownload.Remove(pCallback.m_nPublishedFileId);

                TriggerModRefresh();
            }
        }

        public void TriggerModRefresh()
        {
            if (ModsToDownload.Count == 0)
            {
                Plugin.logger.LogInfo($"All server mods downloaded.");

                if (InLobby && LobbyDataReady && !IsLobbyOwner)
                {
                    bool actualReloadRequired = false; // If the clients and server already perfectly match in terms of enabled mods we don't need to do a content refresh.
                    List<bool> oldState = new();

                    foreach (var mod in ModManager.instance.mods)
                    {
                        bool shouldBeEnabled = ServerMods.Contains(mod.workshopItemId);
                        oldState.Add(mod.enabled);

                        if (mod.enabled != shouldBeEnabled)
                        {
                            Plugin.logger.LogInfo($"mod '{mod.title}' (https://steamcommunity.com/sharedfiles/filedetails/?id={mod.workshopItemId}) enabled status of {mod.enabled}, did not match the server's status for that mod which was {shouldBeEnabled}.");
                            actualReloadRequired = true;

                            if (MatchSubscriptionsToServer && mod.enabled)
                            {
                                // this is a mod we are likely currently subscribed to that we should attempt to unsubscribe from.

                                EItemState itemStateFlags = (EItemState)SteamUGC.GetItemState(mod.workshopItemId);
                                if (itemStateFlags.HasFlag(EItemState.k_EItemStateSubscribed))
                                {
                                    // yep we were subscribed and we shouldn't be.
                                    Plugin.logger.LogInfo($"Unsubscribing from '{mod.title}' ID:{mod.workshopItemId}");
                                    SteamUGC.UnsubscribeItem(mod.workshopItemId);
                                }
                            }
                        }

                        mod.enabled = shouldBeEnabled;
                    }

                    if (actualReloadRequired)
                    {
                        RequestModReload = true;
                        LoadedServerMods = true;

                        for (int i = 0; i < ModManager.instance.mods.Count; i++)
                            ModManager.instance.mods[i].enabled = oldState[i];
                    }
                    else
                    {
                        CompletedLoading();
                    }
                }
            }
            else
            {
                var mod_id = ModsToDownload[0];
                LocalMemberData.ServerModsNeeded = ModsToDownload.Count();
                if (MatchSubscriptionsToServer)
                {
                    // TODO: This should probably be done in both cases as we can check for if the mod is already cached as well rather than blindly attempt to download it again.
                    EItemState itemStateFlags = (EItemState)SteamUGC.GetItemState(mod_id);
                    if (!itemStateFlags.HasFlag(EItemState.k_EItemStateSubscribed))
                    {
                        Plugin.logger.LogInfo($"Subscribing to mod with ID:{mod_id} (https://steamcommunity.com/sharedfiles/filedetails/?id={mod_id})");
                        SteamUGC.SubscribeItem(mod_id);
                    }
                }
                else
                {
                    bool isDownloading = SteamUGC.DownloadItem(mod_id, true);
                    Plugin.logger.LogInfo($"Downloading mod with id: {mod_id} -- {isDownloading}");

                }
            }
        }

        public void SetNotification(string notification)
        {
            if (GUI != null)
                GUI.NotificationText = notification;
        }

        private void StartAsClient()
        {
            ReadyToPlay = true;
            //No initial bots! Many errors otherwise!
            InstantActionMaps.instance.botNumberField.text = "0";
            InstantActionMaps.instance.StartGame();
        }

        private void OnLobbyList(LobbyMatchList_t pCallback)
        {
            Plugin.logger.LogInfo("Got lobby list.");

            OpenLobbies.Clear();
            for (int i = 0; i < pCallback.m_nLobbiesMatching; i++)
            {
                var lobby = SteamMatchmaking.GetLobbyByIndex(i);
                Plugin.logger.LogInfo($"Requesting lobby data for {lobby} -- {SteamMatchmaking.RequestLobbyData(lobby)}");
                OpenLobbies.Add(lobby, null);
            }
        }

        public void ResetOnTeamChanged()
        {
            needsTeamChangeRegister = true;
        }

        public void RegisterOnTeamChangeListener()
        {
            needsTeamChangeRegister = false;

            InstantActionMaps.instance.teamDropdown.onValueChanged.AddListener(delegate
            {
                OnTeamChange(InstantActionMaps.instance.teamDropdown);
            });

            // Initialize currently selected team
            OnTeamChange(InstantActionMaps.instance.teamDropdown);
        }

        private void OnTeamChange(UnityEngine.UI.Dropdown dropdown)
        {
            if (LocalMemberData != null)
            {
                LocalMemberData.Team = dropdown.value;
            }
        }

        private void SetCustomMap(string mapName)
        {
            if (DebugLoggingCache.ShouldLog)
            {
                Plugin.logger.LogInfo($"Attempting to set custom map with name {mapName}");
            }

            if (!MapCache.CustomMapEntries.ContainsKey(mapName))
            {
                Plugin.logger.LogError($"Could not find custom map with name {mapName}");
                DebugLoggingCache.ExportToLog(MatchSettings);
                return;
            }

            InstantActionMaps.MapEntry entry = MapCache.CustomMapEntries[mapName];
            InstantActionMaps.SelectedCustomMapEntry(entry);
        }

        private void Update()
        {
            if (GameManager.instance == null || GameManager.IsIngame() || GameManager.IsInLoadingScreen())
                return;

            if (Input.GetKeyDown(KeyCode.M) && !InLobby)
            {
                if (GUI == null)
                    GUI = new LobbyGUI();
                else
                    GUI = null;
            }

            if (MainMenu.instance != null
                && (int)typeof(MainMenu).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MainMenu.instance) < MainMenu.PAGE_INSTANT_ACTION
                && InLobby)
                MainMenu.instance.OpenPageIndex(MainMenu.PAGE_INSTANT_ACTION);

            if (LoadedServerMods && RequestModReload)
            {
                LoadedServerMods = false;
                RequestModReload = false;
                ModManager.instance.ReloadModContent();
            }

            if (!LobbyDataReady)
                return;

            // TODO: Ok. This is really bad. We should either:
            // A) Update the menu items only when they are changed, or,
            // B) Sidestep the menu entirely, and send the game information
            //     when the host starts.
            // The latter option is the cleanest and most efficient way, but
            // the former at least has visual input for the non-host clients,
            // which is also important.
            // InstantActionMaps.instance.gameModeDropdown.value = 0;
            // Don't allow spectator.
            if (InstantActionMaps.instance.teamDropdown.value == 2)
            {
                InstantActionMaps.instance.teamDropdown.value = 0;
            }

            if (needsTeamChangeRegister)
            {
                RegisterOnTeamChangeListener();
            }

            if (IsLobbyOwner)
            {
                MatchSettings.PopulateData(InstantActionMaps.instance, MapCache.Maps);

                for (int i = 0; i < 2; i++)
                {
                    var teamInfo = GameManager.instance.gameInfo.team[i];

                    var weapons = new List<int>();
                    foreach (var weapon in teamInfo.availableWeapons)
                    {
                        weapons.Add(weapon.nameHash);
                    }
                    string weaponString = string.Join(",", weapons.ToArray());
                    SetLobbyData(i + "weapons", weaponString);

                    foreach (var vehiclePrefab in teamInfo.vehiclePrefab)
                    {
                        var type = vehiclePrefab.Key;
                        var prefab = vehiclePrefab.Value;

                        bool isDefault = true; // Default vehicle.
                        int idx = Array.IndexOf(ActorManager.instance.defaultVehiclePrefabs, prefab);

                        if (idx == -1)
                        {
                            isDefault = false;
                            idx = sortedModdedVehicles.IndexOf(prefab);
                        }

                        SetLobbyData(i + "vehicle_" + type, prefab == null ? "NULL" : isDefault + "," + idx);
                    }

                    foreach (var turretPrefab in teamInfo.turretPrefab)
                    {
                        var type = turretPrefab.Key;
                        var prefab = turretPrefab.Value;

                        bool isDefault = true; // Default turret.
                        int idx = Array.IndexOf(ActorManager.instance.defaultTurretPrefabs, prefab);

                        if (idx == -1)
                        {
                            isDefault = false;
                            var moddedTurrets = ModManager.AllTurretPrefabs().ToList();
                            moddedTurrets.Sort((x, y) => x.name.CompareTo(y.name));
                            idx = moddedTurrets.IndexOf(prefab);
                        }

                        SetLobbyData(i + "turret_" + type, prefab == null ? "NULL" : isDefault + "," + idx);
                    }

                    SetLobbyData(i + "skin", InstantActionMaps.instance.skinDropdowns[i].value.ToString());
                }

                var enabledMutators = new List<int>();
                ModManager.instance.loadedMutators.Sort((x, y) => x.name.CompareTo(y.name));

                for (int i = 0; i < ModManager.instance.loadedMutators.Count; i++)
                {
                    var mutator = ModManager.instance.loadedMutators.ElementAt(i);

                    if (!mutator.isEnabled)
                        continue;

                    int id = i;

                    enabledMutators.Add(id);

                    var serializedMutators = new JSONArray();
                    foreach (var item in mutator.configuration.GetAllFields())
                    {
                        JSONNode node = new JSONString(item.SerializeValue());
                        serializedMutators.Add(node);
                    }

                    SetLobbyData(id + "config", serializedMutators.ToString());
                }
                SetLobbyData("mutators", string.Join(",", enabledMutators.ToArray()));
            }
            else if (LocalMemberData.Loaded)
            {
                MatchSettings.SetInstantActionMapData(InstantActionMaps.instance);

                if (LoadedServerMods && MapCache.Maps != null)
                {
                    bool validMapIndex = MatchSettings.SelectedMapIndex < MapCache.Maps.Count;

                    if (validMapIndex)
                    {
                        InstantActionMaps.instance.mapDropdown.value = MatchSettings.SelectedMapIndex;
                    }

                    if (!validMapIndex || MapCache.Maps[MatchSettings.SelectedMapIndex].name != MatchSettings.SelectedMapName)
                    {
                        Plugin.logger.LogInfo($"!validMapIndex = ({!validMapIndex}) {MapCache.Maps.Count()}");
                        if (validMapIndex)
                            Plugin.logger.LogInfo($" || MapCache.Maps[MatchSettings.SelectedMapIndex({MatchSettings.SelectedMapIndex})].name({MapCache.Maps[MatchSettings.SelectedMapIndex].name}) != MatchSettings.SelectedMapName({MatchSettings.SelectedMapName})");
                        SetCustomMap(MatchSettings.SelectedMapName);
                        MapCache.UpdateCacheFromIAM(InstantActionMaps.instance);
                    }
                }


                for (int i = 0; i < 2; i++)
                {
                    var teamInfo = GameManager.instance.gameInfo.team[i];

                    teamInfo.availableWeapons.Clear();
                    string[] weapons = SteamMatchmaking.GetLobbyData(LobbyID, i + "weapons").Split(',');
                    foreach (string weapon_str in weapons)
                    {
                        if (weapon_str == string.Empty)
                            continue;
                        int hash = int.Parse(weapon_str);
                        var weapon = NetActorController.GetWeaponEntryByHash(hash);
                        teamInfo.availableWeapons.Add(weapon);
                    }

                    bool changedVehicles = false;
                    foreach (var vehicleType in (VehicleSpawner.VehicleSpawnType[])Enum.GetValues(typeof(VehicleSpawner.VehicleSpawnType)))
                    {
                        var type = vehicleType;
                        var prefab = teamInfo.vehiclePrefab[type];

                        var targetPrefab = SteamMatchmaking.GetLobbyData(LobbyID, i + "vehicle_" + type);

                        GameObject newPrefab = null;
                        if (targetPrefab != "NULL")
                        {
                            string[] args = targetPrefab.Split(',');
                            bool isDefault = bool.Parse(args[0]);
                            int idx = int.Parse(args[1]);

                            if (isDefault)
                            {
                                newPrefab = ActorManager.instance.defaultVehiclePrefabs[idx];
                            }
                            else
                            {
                                newPrefab = sortedModdedVehicles[idx];
                            }
                        }

                        if (prefab != newPrefab)
                            changedVehicles = true;

                        teamInfo.vehiclePrefab[type] = newPrefab;
                    }

                    bool changedTurrets = false;
                    foreach (var turretType in (TurretSpawner.TurretSpawnType[])Enum.GetValues(typeof(TurretSpawner.TurretSpawnType)))
                    {
                        var type = turretType;
                        var prefab = teamInfo.turretPrefab[type];

                        var targetPrefab = SteamMatchmaking.GetLobbyData(LobbyID, i + "turret_" + type);

                        GameObject newPrefab = null;
                        if (targetPrefab != "NULL")
                        {
                            string[] args = targetPrefab.Split(',');
                            bool isDefault = bool.Parse(args[0]);
                            int idx = int.Parse(args[1]);

                            if (isDefault)
                            {
                                newPrefab = ActorManager.instance.defaultTurretPrefabs[idx];
                            }
                            else
                            {
                                var moddedTurrets = ModManager.AllTurretPrefabs().ToList();
                                newPrefab = moddedTurrets[idx];
                            }
                        }

                        if (prefab != newPrefab)
                            changedTurrets = true;

                        teamInfo.turretPrefab[type] = newPrefab;
                    }

                    if (changedVehicles || changedTurrets)
                        GamePreview.UpdatePreview();

                    InstantActionMaps.instance.skinDropdowns[i].value = int.Parse(SteamMatchmaking.GetLobbyData(LobbyID, i + "skin"));
                }

                string[] enabledMutators = SteamMatchmaking.GetLobbyData(LobbyID, "mutators").Split(',');
                foreach (var mutator in ModManager.instance.loadedMutators)
                    mutator.isEnabled = false;
                foreach (var mutatorStr in enabledMutators)
                {
                    if (mutatorStr == string.Empty)
                        continue;

                    int id = int.Parse(mutatorStr);

                    for (int mutatorIndex = 0; mutatorIndex < ModManager.instance.loadedMutators.Count; mutatorIndex++)
                    {
                        var mutator = ModManager.instance.loadedMutators.ElementAt(mutatorIndex);

                        if (id == mutatorIndex)
                        {
                            mutator.isEnabled = true;

                            string configStr = SteamMatchmaking.GetLobbyData(instance.LobbyID, mutatorIndex + "config");

                            JSONArray jsonConfig = JSON.Parse(configStr).AsArray;
                            List<string> configList = new List<string>();

                            foreach (var configItem in jsonConfig)
                            {
                                configList.Add((string)configItem.Value);
                            }

                            string[] config = configList.ToArray();

                            for (int i = 0; i < mutator.configuration.GetAllFields().Count(); i++)
                            {
                                var item = mutator.configuration.GetAllFields().ElementAt(i);
                                if (item.SerializeValue() != "")
                                {
                                    item?.DeserializeValue(config[i]);
                                }
                            }
                        }
                    }
                }


                if (MatchHasStarted() || MatchSettings.MatchStarted)
                {
                    Plugin.logger.LogInfo($"SteamMatchmaking.GetLobbyData(LobbyID, \"started\") = {SteamMatchmaking.GetLobbyData(LobbyID, "started")} || MatchSettings.MatchStarted {MatchSettings.MatchStarted}");
                    StartAsClient();
                }
            }
        }

        private void OnGUI()
        {
            GUI?.DrawLobbyGui(this);
        }

        private void LogMatchSettings(MatchSettings matchSettings)
        {
            if (!DebugLoggingCache.ShouldLog)
                return;

            DebugLoggingCache.ExportToLog(matchSettings);
        }
    }
}