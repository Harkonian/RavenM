using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;

using Steamworks;
using SimpleJSON;
using UnityEngine;

namespace RavenM.Lobby;
public class LobbySystem : MonoBehaviour
{
    public static LobbySystem instance;

    FixedServerSettings FixedSettings;

    public bool InLobby { get; private set; } = false;

    public bool LobbyDataReady { get; private set; } = false;

    public bool IsLobbyOwner = false;

    public LobbyGUI GUI = null;

    public CSteamID LobbyID = CSteamID.Nil;

    public CSteamID OwnerID = CSteamID.Nil;

    public bool ReadyToPlay = false;

    public List<PublishedFileId_t> ServerMods = [];

    public List<PublishedFileId_t> ModsToDownload = [];

    public bool LoadedServerMods = false;

    public bool RequestModReload = false;

    public Dictionary<CSteamID, FixedServerSettings> OpenLobbies = [];

    public List<CSteamID> CurrentKickedMembers = [];

    public CSteamID KickPrompt = CSteamID.Nil;

    public string NotificationText = string.Empty;

    public bool nameTagsEnabled = true;

    public bool nameTagsForTeamOnly = false;

    public List<GameObject> sortedModdedVehicles = [];

    public Dictionary<string, Tuple<String, float>> LobbySetCache = [];

    public Dictionary<string, Tuple<String, float>> LobbySetMemberCache = [];

    public static readonly float SET_DEADLINE = 5f; // Seconds

    public bool IntentionToStart = false;

    public bool HasCommittedToStart = false;

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

    public void SetLobbyDataDedup(string key, string value) {
        if (!InLobby || !LobbyDataReady || !IsLobbyOwner)
            return;

        // De-dup any lobby values since apparently Steam doesn't do that for you.
        if (LobbySetCache.TryGetValue(key, out Tuple<String, float> oldValue) && oldValue.Item1 == value && Time.time < oldValue.Item2)
            return;

        SteamMatchmaking.SetLobbyData(LobbyID, key, value);
        LobbySetCache[key] = new Tuple<String, float>(value, Time.time + SET_DEADLINE);
    }

    public void SetLobbyMemberDataDedup(string key, string value) {
        if (!InLobby || !LobbyDataReady)
            return;

        // De-dup any lobby values since apparently Steam doesn't do that for you.
        if (LobbySetMemberCache.TryGetValue(key, out Tuple<String, float> oldValue) && oldValue.Item1 == value && Time.time < oldValue.Item2)
            return;

        SteamMatchmaking.SetLobbyMemberData(LobbyID, key, value);
        LobbySetMemberCache[key] = new Tuple<String, float>(value, Time.time + SET_DEADLINE);
    }

    private void OnLobbyData(LobbyDataUpdate_t pCallback)
    {
        var lobby = new CSteamID(pCallback.m_ulSteamIDLobby);

        if (pCallback.m_bSuccess == 0 || SteamMatchmaking.GetLobbyDataCount(lobby) == 0)
            OpenLobbies.Remove(lobby);

        FixedServerSettings settings = ImportFixedServerSettings(lobby);
        if (settings != null && settings.IncludeInBrowseList)
            OpenLobbies[lobby] = settings;
        else
            OpenLobbies.Remove(lobby);
    }

    private void OnLobbyEnter(LobbyEnter_t pCallback)
    {
        Plugin.logger.LogInfo("Joined lobby!");
        CurrentKickedMembers.Clear();
        LobbySetCache.Clear();
        RequestModReload = false;
        LoadedServerMods = false;

        if (pCallback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            NotificationText = "Unknown error joining lobby. (Does it still exist?)";
            InLobby = false;
            return;
        }

        LobbyDataReady = true;
        LobbyID = new CSteamID(pCallback.m_ulSteamIDLobby);

        ChatManager.instance.PushLobbyChatMessage($"Welcome to the lobby! Press {ChatManager.instance.GlobalChatKeybind} to chat.");

        if (IsLobbyOwner)
        {
            Plugin.logger.LogInfo("Attempting to start as host.");
            OwnerID = SteamUser.GetSteamID();
            SetLobbyDataDedup("owner", OwnerID.ToString());
            SetLobbyDataDedup("build_id", Plugin.BuildGUID);
            if (FixedSettings.FriendsOnlyLobby)
                SetLobbyDataDedup("hidden", "true");
            if (FixedSettings.MidgameJoin)
                SetLobbyDataDedup("hotjoin", "true");
            if (FixedSettings.NameTagsEnabled)
                SetLobbyDataDedup("nameTags","true");
            if (FixedSettings.TeamOnlyNameTags)
                SetLobbyDataDedup("nameTagsForTeamOnly", "true");

            bool needsToReload = false;
            List<PublishedFileId_t> mods = new ();

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
            SetLobbyDataDedup("owner", OwnerID.ToString());
            SetLobbyDataDedup("mods", string.Join(",", mods.ToArray()));
            SteamMatchmaking.SetLobbyMemberData(LobbyID, "loaded", "yes");
            SetLobbyDataDedup("started", "false");
        }
        else
        {
            Plugin.logger.LogInfo("Attempting to start as client.");
            OwnerID = new CSteamID(ulong.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "owner")));
            Plugin.logger.LogInfo($"Host ID: {OwnerID}");

            MainMenu.instance.OpenPageIndex(MainMenu.PAGE_INSTANT_ACTION);
            ReadyToPlay = false;

            if (Plugin.BuildGUID != SteamMatchmaking.GetLobbyData(LobbyID, "build_id"))
            {
                BeginLeavingLobby(Notifications.PluginMismatch);
                return;
            }

            ServerMods.Clear();
            ModsToDownload.Clear();
            string[] mods = SteamMatchmaking.GetLobbyData(LobbyID, "mods").Split(',');
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
            SteamMatchmaking.SetLobbyMemberData(LobbyID, "modsDownloaded", (ServerMods.Count - ModsToDownload.Count).ToString());
            TriggerModRefresh();
            bool nameTagsConverted = bool.TryParse(SteamMatchmaking.GetLobbyData(LobbyID, "nameTags"),out bool nameTagsOn);
            if (nameTagsConverted)
            {
                nameTagsEnabled = nameTagsOn;
            }
            else
            {
                nameTagsEnabled = false;
            }
            bool nameTagsTeamOnlyConverted = bool.TryParse(SteamMatchmaking.GetLobbyData(LobbyID, "nameTagsForTeamOnly"), out bool nameTagsForTeamOnlyOn);
            if (nameTagsTeamOnlyConverted)
            {
                nameTagsForTeamOnly = nameTagsForTeamOnlyOn;
            }
            else
            {
                nameTagsForTeamOnly = false;
            }
            if (SteamMatchmaking.GetLobbyData(LobbyID, "started") == "yes" && SteamMatchmaking.GetLobbyData(LobbyID, "hotjoin") != "true")
            {
                Plugin.logger.LogInfo("The game has already started :( Leaving lobby.");
                BeginLeavingLobby(Notifications.HotJoinDisabled);
                return;
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
                SteamMatchmaking.SetLobbyMemberData(LobbyID, "modsDownloaded", (ServerMods.Count - ModsToDownload.Count).ToString());

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

                foreach (var mod in ModManager.instance.mods)
                {
                    oldState.Add(mod.enabled);

                    mod.enabled = ServerMods.Contains(mod.workshopItemId);
                }

                // Clones the list of enabled mods.
                ModManager.instance.ReloadModContent();
                LoadedServerMods = true;

                for (int i = 0; i < ModManager.instance.mods.Count; i++)
                    ModManager.instance.mods[i].enabled = oldState[i];
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
        int customMapOptionIndex = (int)typeof(InstantActionMaps).GetField("customMapOptionIndex", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(InstantActionMaps.instance);
        var entries = (List<InstantActionMaps.MapEntry>)typeof(InstantActionMaps).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(InstantActionMaps.instance);
        // Don't allow spectator.
        if (InstantActionMaps.instance.teamDropdown.value == 2)
        {
            InstantActionMaps.instance.teamDropdown.value = 0;
        }
        SetLobbyMemberDataDedup("team", InstantActionMaps.instance.teamDropdown.value == 0 ? "<color=blue>E</color>" : "<color=red>R</color>");
        
        if (IsLobbyOwner)
        {

            // TODO: All of these should get encapsulated into a class we can pass around.
            SetLobbyDataDedup("gameMode", InstantActionMaps.instance.gameModeDropdown.value.ToString());
            SetLobbyDataDedup("nightMode", InstantActionMaps.instance.nightToggle.isOn.ToString());
            SetLobbyDataDedup("playerHasAllWeapons", InstantActionMaps.instance.playerHasAllWeaponsToggle.isOn.ToString());
            SetLobbyDataDedup("reverseMode", InstantActionMaps.instance.reverseToggle.isOn.ToString());
            SetLobbyDataDedup("botNumberField", InstantActionMaps.instance.botNumberField.text);
            SetLobbyDataDedup("balance", InstantActionMaps.instance.balanceSlider.value.ToString(CultureInfo.InvariantCulture));
            SetLobbyDataDedup("respawnTime", InstantActionMaps.instance.respawnTimeField.text);
            SetLobbyDataDedup("gameLength", InstantActionMaps.instance.gameLengthDropdown.value.ToString());
            SetLobbyDataDedup("loadedLevelEntry", InstantActionMaps.instance.mapDropdown.value.ToString());

            // For SpecOps.
            if (InstantActionMaps.instance.gameModeDropdown.value == 1)
            {
                SetLobbyDataDedup("team", InstantActionMaps.instance.teamDropdown.value.ToString());
            }

            if (InstantActionMaps.instance.mapDropdown.value == customMapOptionIndex)
            {
                SetLobbyDataDedup("customMap", entries[customMapOptionIndex].metaData.displayName);
            }

            for (int i = 0; i < 2; i++)
            {
                var teamInfo = GameManager.instance.gameInfo.team[i];

                var weapons = new List<int>();
                foreach (var weapon in teamInfo.availableWeapons)
                {
                    weapons.Add(weapon.nameHash);
                }
                string weaponString = string.Join(",", weapons.ToArray());
                SetLobbyDataDedup(i + "weapons", weaponString);

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

                    SetLobbyDataDedup(i + "vehicle_" + type, prefab == null ? "NULL" : isDefault + "," + idx);
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

                    SetLobbyDataDedup(i + "turret_" + type, prefab == null ? "NULL" : isDefault + "," + idx);
                }

                SetLobbyDataDedup(i + "skin", InstantActionMaps.instance.skinDropdowns[i].value.ToString());
            }

            var enabledMutators = new List<int>();
            ModManager.instance.loadedMutators.Sort((x, y) => x.name.CompareTo(y.name));

            for (int i = 0; i < ModManager.instance.loadedMutators.Count; i++)
            {
                var mutator = ModManager.instance.loadedMutators.ElementAt(i);

                if (!GameManager.instance.gameInfo.activeMutators.Contains(mutator))
                    continue;

                int id = i;

                enabledMutators.Add(id);

                var serializedMutators = new JSONArray();
                foreach (var item in mutator.configuration.GetAllFields())
                {
                    JSONNode node = new JSONString(item.SerializeValue());
                    serializedMutators.Add(node);
                }
                
                SetLobbyDataDedup(id + "config", serializedMutators.ToString());
            }
            SetLobbyDataDedup("mutators", string.Join(",", enabledMutators.ToArray()));
        }
        else if (SteamMatchmaking.GetLobbyMemberData(LobbyID, SteamUser.GetSteamID(), "loaded") == "yes")
        {
            InstantActionMaps.instance.gameModeDropdown.value = int.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "gameMode"));
            InstantActionMaps.instance.nightToggle.isOn = bool.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "nightMode"));
            InstantActionMaps.instance.playerHasAllWeaponsToggle.isOn = bool.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "playerHasAllWeapons"));
            InstantActionMaps.instance.reverseToggle.isOn = bool.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "reverseMode"));
            InstantActionMaps.instance.configFlagsToggle.isOn = false;
            InstantActionMaps.instance.botNumberField.text = SteamMatchmaking.GetLobbyData(LobbyID, "botNumberField");
            InstantActionMaps.instance.balanceSlider.value = float.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "balance"), CultureInfo.InvariantCulture);
            InstantActionMaps.instance.respawnTimeField.text = SteamMatchmaking.GetLobbyData(LobbyID, "respawnTime");
            InstantActionMaps.instance.gameLengthDropdown.value = int.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "gameLength"));
            // For SpecOps.
            if (InstantActionMaps.instance.gameModeDropdown.value == 1)
            {
                InstantActionMaps.instance.teamDropdown.value = int.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "team"));
            }

            if (instance.LoadedServerMods)
            {
                int givenEntry = int.Parse(SteamMatchmaking.GetLobbyData(LobbyID, "loadedLevelEntry"));

                if (givenEntry == customMapOptionIndex)
                {
                    string mapName = SteamMatchmaking.GetLobbyData(LobbyID, "customMap");

                    if (InstantActionMaps.instance.mapDropdown.value != customMapOptionIndex || entries[customMapOptionIndex].metaData.displayName != mapName)
                    {
                        foreach (Transform item in InstantActionMaps.instance.customMapsBrowser.contentPanel) 
                        {
                            var entry = item.gameObject.GetComponent<CustomMapEntry>();
                            if (entry.entry.metaData.displayName == mapName)
                            {
                                entry.Select();
                            }
                        }
                    }
                }
                else
                {
                    InstantActionMaps.instance.mapDropdown.value = givenEntry;
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

            string[] enabledMutators = SteamMatchmaking.GetLobbyData(LobbySystem.instance.LobbyID, "mutators").Split(',');
            GameManager.instance.gameInfo.activeMutators.Clear();
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
                        GameManager.instance.gameInfo.activeMutators.Add(mutator);

                        string configStr = SteamMatchmaking.GetLobbyData(LobbySystem.instance.LobbyID, mutatorIndex + "config");

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

            if (SteamMatchmaking.GetLobbyData(LobbyID, "started") == "yes")
            {
                StartAsClient();
            }
        }
    }

    public void Reset()
    {
        LobbyDataReady = false;
        ReadyToPlay = true;
        IsLobbyOwner = false;
        InLobby = false;

        if (LoadedServerMods)
            RequestModReload = true;

        ModsToDownload.Clear();
        ServerMods.Clear();
        CurrentKickedMembers.Clear();
    }

    public void HostLobby(FixedServerSettings settings)
    {
        FixedSettings = settings;
        SteamMatchmaking.CreateLobby(settings.FriendsOnlyLobby ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic, (int)settings.LobbyMemberCap);
        InLobby = true;
        IsLobbyOwner = true;
        LobbyDataReady = false;
    }
    

    public void BeginLeavingLobby(string notification = null, bool leaveSteam = true)
    {
        if (notification != null)
        {
            SetNotification(notification);
            Plugin.logger.LogInfo(notification);
        }

        Plugin.logger.LogInfo("BeginLeavingLobby");

        if (leaveSteam)
            SteamMatchmaking.LeaveLobby(LobbyID);
        else
            Reset();

        InLobby = false;
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

    public FixedServerSettings ImportFixedServerSettings(CSteamID lobbyID)
    {
        FixedServerSettings settings = new();
        settings.BuildID = SteamMatchmaking.GetLobbyData(lobbyID, "build_id");
        // This is technically only used when putting up the lobby through steam and is never needed to be referenced by us. Leaving here to note its absence.
        // settings.FriendsOnlyLobby = 
        settings.IncludeInBrowseList = SteamMatchmaking.GetLobbyData(lobbyID, "hidden") != "true";
        settings.LobbyMemberCap = (uint)SteamMatchmaking.GetLobbyMemberLimit(lobbyID);
        settings.MidgameJoin = SteamMatchmaking.GetLobbyData(LobbyID, "hotjoin") == "true";
        settings.NameTagsEnabled = SteamMatchmaking.GetLobbyData(LobbyID, "nameTags") == "true";
        settings.TeamOnlyNameTags = SteamMatchmaking.GetLobbyData(LobbyID, "nameTags") == "true";

        settings.OwnerID = ulong.Parse(SteamMatchmaking.GetLobbyData(lobbyID, "owner"));

        return settings;
    }
}