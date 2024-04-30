using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using Steamworks;
using UnityEngine;

namespace RavenM.Lobby;

public class LobbySystem : MonoBehaviour
{
    public static LobbySystem instance;
    public bool InLobby { get; private set; } = false;

    public bool LobbyDataReady { get; private set; } = false;

    public bool IsLobbyOwner = false;

    public LobbyGUI GUI = null;

    public CSteamID LobbyID = CSteamID.Nil;

    public CSteamID OwnerID = CSteamID.Nil;

    public bool ReadyToPlay = false;

    public List<PublishedFileId_t> ServerMods = [];

    public List<PublishedFileId_t> ModsToDownload = [];

    // TODO: These next two bools are really hazily defined in my mind. What does each of these actually mean?
    public bool LoadedServerMods = false;

    public bool RequestModReload = false;


    public bool TEMP__NeedsReloadOnLeave = false;

    public Dictionary<CSteamID, FixedServerSettings> OpenLobbies = [];

    public List<CSteamID> CurrentKickedMembers = [];

    public bool nameTagsEnabled = true;

    public bool nameTagsForTeamOnly = false;

    public bool IntentionToStart = false;

    public bool HasCommittedToStart = false;

    public CachedGameData Cache { get; private set; } = null;

    public FixedServerSettings FixedSettings = new();

    internal MatchSettings MatchSettings = new();

    internal LobbyMemberData localMemberData = new();

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        Callback<DownloadItemResult_t>.Create(OnItemDownload);
        Callback<LobbyMatchList_t>.Create(OnLobbyList);
        Callback<LobbyDataUpdate_t>.Create(OnLobbyData);
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

    private void OnLobbyData(LobbyDataUpdate_t lobbyDataUpdateEvent)
    {
        if (!InLobby)
        {
            BrowsingLobbyDataUpdated(lobbyDataUpdateEvent);
        }
        else
        {
            CurrentLobbyDataUpdated(lobbyDataUpdateEvent);
        }
    }

    // We're not in a lobby and we've gotten lobby data. This should be something we requested while browsing.
    private void BrowsingLobbyDataUpdated(LobbyDataUpdate_t lobbyDataUpdateEvent)
    {
        CSteamID lobby = new CSteamID(lobbyDataUpdateEvent.m_ulSteamIDLobby);

        if (lobbyDataUpdateEvent.m_bSuccess == 0 || SteamMatchmaking.GetLobbyDataCount(lobby) == 0)
            OpenLobbies.Remove(lobby);

        LogAllSteamLobbyData(lobby);

        SteamLobbyDataTransfer.ImportFromLobbyData(lobby, out FixedServerSettings settings);

        if (settings != null && settings.IncludeInBrowseList)
            OpenLobbies[lobby] = settings;
        else
            OpenLobbies.Remove(lobby);
    }

    private void CurrentLobbyDataUpdated(LobbyDataUpdate_t lobbyDataUpdateEvent)
    {
        CSteamID lobby = new CSteamID(lobbyDataUpdateEvent.m_ulSteamIDLobby);
        if (LobbyID != lobby || lobbyDataUpdateEvent.m_ulSteamIDLobby != lobbyDataUpdateEvent.m_ulSteamIDMember)
        {
            // Either this wasn't for our lobby (lingering browse request perhaps?)
            // or it was a player updating their member data which we're not going to care about as we pull those each frame regardless (TODO: Stop pulling those each frame.)
            return;
        }

        if (lobbyDataUpdateEvent.m_bSuccess == 0)
        {
            LoggingHelper.LogMarker("Lobby Updated but event marked as failed.", false);
            return; // TODO: This feels like a big error but for now lets just log it and swallow it.
        }

        if (SteamMatchmaking.GetLobbyDataCount(lobby) == 0)
        {
            LoggingHelper.LogMarker("Lobby Updated but has no data.", false);
            return; // TODO: This is probably expected for hosts to get at least one of these when first setting up the lobby. Figure out if true.
        }

        if (IsLobbyOwner)
        {
            return; // We're the lobby owner, we don't need to read our own updates.
        }

        if (!SteamLobbyDataTransfer.ImportFromLobbyData(LobbyID, out MatchSettings))
        {
            LoggingHelper.LogMarker("Failed to import data from lobby!!", false);
            return;
        }

        string result = "Imported From Steam Update Notification:\n";
        DataTransfer.GenericDataTransfer.ExportTo(MatchSettings, (key, value) => result += $"{key} = {value}\n");
        LoggingHelper.LogMarker(result);
    }

    // Host only function.
    IEnumerator SendLobbyDataPeriodic()
    {
        float currentDelay = 1.0f;

        while (InLobby && IsLobbyOwner)
        {
            if (SteamLobbyDataTransfer.HasQueuedData())
            {
                if (!SteamLobbyDataTransfer.SendQueuedDataToSteam(LobbyID)) 
                {
                    // We wrote too much and steam is refusing our writes now. Chill out for a while before trying again.
                    yield return new WaitForSeconds(currentDelay);
                }
            }
            else
            {
                yield return null;
            }

            yield return new WaitForSeconds(currentDelay);
        }
    }

    // Hosts & clients both should have this running.
    IEnumerator SendLobbyMemberDataPeriodic()
    {
        float currentDelay = 1.0f;

        while (InLobby)
        {
            SteamLobbyDataTransfer.ExportToMemberData(LobbyID, localMemberData);

            yield return new WaitForSeconds(currentDelay);
        }
    }

    private void OnLobbyEnter(LobbyEnter_t pCallback)
    {
        LobbyID = new CSteamID(pCallback.m_ulSteamIDLobby);
        Plugin.logger.LogInfo($"Joined lobby: {LobbyID}");
        // TODO: This is all about setting our data to its initial states, Should this belong in Reset instead?
        CurrentKickedMembers.Clear();
        RequestModReload = false;
        LoadedServerMods = false;

        if (pCallback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            BeginLeavingLobby(Notifications.JoiningError);
            return;
        }

        LobbyDataReady = true;
        ChatManager.instance.PushLobbyChatMessage($"Welcome to the lobby! Press {ChatManager.instance.GlobalChatKeybind} to chat.");
        StartCoroutine(SendLobbyMemberDataPeriodic());

        if (IsLobbyOwner)
        {
            OwnerID = SteamUser.GetSteamID();
            Plugin.logger.LogInfo("Attempting to start as host.");

            Plugin.logger.LogInfo($"Lobby Owner ID: {OwnerID}");
            FixedSettings.OwnerID = OwnerID.m_SteamID;

            SetupServerMods();
            StartCoroutine(SendLobbyDataPeriodic());

            SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, FixedSettings);
            SteamLobbyDataTransfer.SendQueuedDataToSteam(LobbyID);

            CompletedLoading();
        }
        else
        {
            Plugin.logger.LogInfo("Attempting to start as client.");

            LogAllSteamLobbyData(LobbyID);

            if (!SteamLobbyDataTransfer.ImportFromLobbyData(LobbyID, out FixedSettings) || Plugin.BuildGUID != FixedSettings.BuildID || !SteamLobbyDataTransfer.ImportFromLobbyData(LobbyID, out MatchSettings))
            {
                BeginLeavingLobby(Notifications.PluginMismatch);
                return;
            }

            Plugin.logger.LogInfo($"Host ID: {FixedSettings.OwnerID}");
            OwnerID = new CSteamID(FixedSettings.OwnerID);

            MainMenu.instance.OpenPageIndex(MainMenu.PAGE_INSTANT_ACTION);
            ReadyToPlay = false;

            ServerMods.Clear();
            ModsToDownload.Clear();
            // TODO-MEMBER-MODS: Comment on why this is being stored in the host's member settings and also probably come up with a more structured way to access this.
            string fullModsString = SteamMatchmaking.GetLobbyMemberData(LobbyID, OwnerID, "mods");
            LoggingHelper.LogMarker($"fullModsString = {fullModsString}");
            string[] mods = fullModsString.Split(',');
            foreach (string mod_str in mods)
            {
                if (mod_str == string.Empty)
                    continue;
                PublishedFileId_t mod_id = new PublishedFileId_t(ulong.Parse(mod_str));
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

            LoggingHelper.LogMarker($"Server mod count:{ServerMods.Count}, Number to download:{ModsToDownload.Count}");

            int modsDownloaded = (ServerMods.Count - ModsToDownload.Count);
            localMemberData.ServerModsDownloaded = modsDownloaded;

            TriggerModRefresh();

            nameTagsEnabled = FixedSettings.NameTagsEnabled;
            nameTagsForTeamOnly = FixedSettings.TeamOnlyNameTags;

            if (MatchSettings.MatchStarted && FixedSettings.MidgameJoin == false)
            {
                BeginLeavingLobby(Notifications.HotJoinDisabled);
            }
        }
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

            if (InLobby && LobbyDataReady)
            {
                int modsDownloaded = (ServerMods.Count - ModsToDownload.Count);
                localMemberData.ServerModsDownloaded = modsDownloaded;
            }

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
                List<bool> oldState = new List<bool>();
                bool changeMade = false; // Did we already match the server?

                foreach (var mod in ModManager.instance.mods)
                {
                    bool wasEnabled = mod.enabled;

                    oldState.Add(wasEnabled);
                    mod.enabled = ServerMods.Contains(mod.workshopItemId);;

                    if (wasEnabled != mod.enabled)
                    {
                        changeMade = true;
                    }
                }

                LoadedServerMods = true;

                // If we didn't actually change which mods are loaded then we don't need to do all this.
                if (changeMade)
                {
                    // Clones the list of enabled mods.
                    ModManager.instance.ReloadModContent();

                    for (int i = 0; i < ModManager.instance.mods.Count; i++)
                        ModManager.instance.mods[i].enabled = oldState[i];
                }
                else
                {
                    // This is normally called by a harmony patch class after a mod reload but since we don't need to reload any mods trigger it here.
                    CompletedLoading();
                }

                TEMP__NeedsReloadOnLeave = changeMade;

            }
        }
        else
        {
            var mod_id = ModsToDownload[0];
            bool isDownloading = SteamUGC.DownloadItem(mod_id, true);
            Plugin.logger.LogInfo($"Downloading mod with id: {mod_id} -- {isDownloading}");
        }
    }

    private void StartAsClient()
    {
        ReadyToPlay = true;
        //No initial bots! Many errors otherwise!
        InstantActionMaps.instance.botNumberField.text = "0";
        InstantActionMaps.instance.StartGame();
    }

    public void RefreshOpenLobbies()
    {
        OpenLobbies.Clear();
        SteamMatchmaking.RequestLobbyList();
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

    private void Update()
    {
        if (GameManager.instance == null || GameManager.IsIngame())
            return;

        // TODO: Horrible hack
        if (GameManager.IsInLoadingScreen())
        {
            LoggingHelper.LogMarker();
            if (IsLobbyOwner)
            {
                SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, MatchSettings);
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.M) && !InLobby)
        {
            if (GUI == null)
                GUI = new LobbyGUI();
            else
                GUI = null;
        }

        // If somehow we're still on one of the few pages before the instant action menu AND in a lobby (potentially a user attempting to leave the menu without leaving the lobby)
        // then force ourselves to the instant action menu.
        if (MainMenu.instance != null
            && (int)typeof(MainMenu).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MainMenu.instance) < MainMenu.PAGE_INSTANT_ACTION
            && InLobby)
            MainMenu.instance.OpenPageIndex(MainMenu.PAGE_INSTANT_ACTION);

        // We've left a lobby that we loaded mods for.
        if (LoadedServerMods && RequestModReload)
        {
            LoadedServerMods = false;
            RequestModReload = false;
            ModManager.instance.ReloadModContent();
        }

        if (!LobbyDataReady)
            return;

        // Don't allow spectator.
        if (InstantActionMaps.instance.teamDropdown.value == 2)
        {
            InstantActionMaps.instance.teamDropdown.value = 0;
        }

        localMemberData.Team = InstantActionMaps.instance.teamDropdown.value;
        
        if (IsLobbyOwner)
        {
            HostUpdate();
        }
        else if (localMemberData.Loaded)
        {
            ClientUpdate();
        }
    }

    public void ResetState()
    {
        LobbyDataReady = false;
        ReadyToPlay = true;
        IsLobbyOwner = false;
        InLobby = false;

        localMemberData = new();

        if (LoadedServerMods)
        {
            RequestModReload = TEMP__NeedsReloadOnLeave;
        }

        TEMP__NeedsReloadOnLeave = false;

        ModsToDownload.Clear();
        ServerMods.Clear();
        CurrentKickedMembers.Clear();
        StopAllCoroutines();
        SteamLobbyDataTransfer.ClearCache();
    }

    public void HostLobby(FixedServerSettings settings)
    {
        FixedSettings = settings;
        FixedSettings.BuildID = Plugin.BuildGUID; // Because we're hosting we need to set this.
        SteamMatchmaking.CreateLobby(settings.FriendsOnlyLobby ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic, (int)settings.LobbyMemberCap);
        InLobby = true;
        IsLobbyOwner = true;
        LobbyDataReady = false;
    }

    public void HostUpdate()
    {
        MatchSettings.PopulateData(InstantActionMaps.instance, Cache);
        SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, MatchSettings);
    }

    public void ClientUpdate()
    {
        InstantActionMaps.instance.configFlagsToggle.isOn = false; // Clients never get this option so always turn it off to make sure a client doesn't check this themselves.

        if (!instance.LoadedServerMods)
        {
            return; // If we've not finished loading the server's mods then don't bother trying to set the map or other data that could mismatch.
        }

        MatchSettings.SetInstantActionMapData(InstantActionMaps.instance, Cache);

        if (MatchSettings.MatchStarted)
        {
            StartAsClient();
        }
    }

    public void BeginLeavingLobby(string notification = null)
    {
        if (notification != null)
        {
            SetNotification(notification);
            Plugin.logger.LogInfo(notification);
        }

        Plugin.logger.LogInfo("BeginLeavingLobby");
        SteamMatchmaking.LeaveLobby(LobbyID);
        InLobby = false;
        ResetState();
        ChatManager.instance.ResetChat();
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

    private void OnGUI()
    {
        GUI?.DrawLobbyGui(this);
    }

    public void SetNotification(string notification)
    {
        if (GUI != null)
            GUI.NotificationText = notification;
    }
    
    public void CompletedLoading()
    {
        Plugin.logger.LogInfo("Loading completed after joining this lobby!");

        Cache = new CachedGameData(InstantActionMaps.instance);
        localMemberData.Loaded = true;

        if (IsLobbyOwner)
        {
            this.MatchSettings.PopulateData(InstantActionMaps.instance, Cache);
        }

        // Sort mutators
        ModManager.instance.loadedMutators.Sort((x, y) => x.name.CompareTo(y.name));
    }

    private void SetupServerMods()
    {
        if (!IsLobbyOwner)
        {
            return; // TODO: Added this just as a precaution but we should probably log and more noticeably fail so we can figure out how this even happened.
        }

        bool needsToReload = false;
        List<PublishedFileId_t> mods = new();

        foreach (var mod in ModManager.instance.GetActiveMods())
        {
            if (mod.workshopItemId.ToString() == "0")
            {
                mod.enabled = false;
                needsToReload = true;
            }
            else
                mods.Add(mod.workshopItemId);
        }

        if (needsToReload)
            ModManager.instance.ReloadModContent();

        string finalModsString = string.Join(",", mods.ToArray());
        LoggingHelper.LogMarker(finalModsString);

        // TODO-MEMBER-MODS: Comment on why this is being stored in the host's member settings and also probably come up with a more structured way to access this.
        SteamMatchmaking.SetLobbyMemberData(LobbyID, "mods", finalModsString);

        FixedSettings.ModCount = mods.Count;
    }

    public void HostStartedMatch()
    {
        MatchSettings.MatchStarted = true;
        SteamLobbyDataTransfer.ExportToLobbyData(LobbyID, MatchSettings);
        LoggingHelper.LogMarker("HostStartedMatch", false);
    }

    public void HostEndedMatch()
    {
        MatchSettings.MatchStarted = false;
        LoggingHelper.LogMarker("HostEndedMatch", false);
    }

    public void MatchSteamModSubscriptions()
    {
        if (IsLobbyOwner)
        {
            return;
        }

        foreach (var mod in ModManager.instance.mods)
        {
            bool shouldBeSubscribed = ServerMods.Contains(mod.workshopItemId);
            EItemState itemStateFlags = (EItemState)SteamUGC.GetItemState(mod.workshopItemId);

            if (itemStateFlags.HasFlag(EItemState.k_EItemStateSubscribed) == shouldBeSubscribed)
            {
                continue; // We match already, either we are subscribed and we should be or we aren't and shouldn't be.
            }

            if (shouldBeSubscribed)
            {
                Plugin.logger.LogInfo($"Subscribing to mod with ID:{mod.workshopItemId} (https://steamcommunity.com/sharedfiles/filedetails/?id={mod.workshopItemId})");
                SteamUGC.SubscribeItem(mod.workshopItemId);
            }
            else
            {
                Plugin.logger.LogInfo($"Unsubscribing from '{mod.title}' ID:{mod.workshopItemId}");
                SteamUGC.UnsubscribeItem(mod.workshopItemId);
            }
        }
    }

    public void LogAllSteamLobbyData(CSteamID lobby, [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0)
    {
        int lobbyDataCount = SteamMatchmaking.GetLobbyDataCount(lobby);

        string log = $"Loading data from Lobby {lobby}\n";
        int dataLength = 0;

        for (int i = 0; i < lobbyDataCount; i++)
        {
            SteamMatchmaking.GetLobbyDataByIndex(lobby, i, out string key, 8192, out string value, 8192);
            log += $"Got data {key} - {value}\n";
            dataLength += value.Length;
        }

        log += $"Total Length: {dataLength}";

        LoggingHelper.LogMarker(log, false, memberName, lineNumber);
    }
}