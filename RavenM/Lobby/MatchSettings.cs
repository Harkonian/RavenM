namespace RavenM.Lobby;

/// <summary>
/// A smaller subset of match settings that are used as part of the browse menu.
/// </summary>
public class MatchListingInfo
{
    public bool MatchStarted { get; set; } = false;

    public string BotNumberText { get; set; }

    /// <summary>
    /// If this is equal to the last index of the dropdown then <see cref="SelectedMapName"/>" should be used to figure out what custom map to load.
    /// </summary>
    public int SelectedMapIndex { get; set; }
    
    public string SelectedMapName { get; set; } = string.Empty;
}

/// <summary>
/// Represents the various settings that the host can change for a given match. 
/// </summary>
internal class MatchSettings : MatchListingInfo
{
    public MatchSettings() { }

    public int GameMode { get; set; }

    public bool NightToggle { get; set; }

    public bool PlayersHaveAllWeapons { get; set; }

    public bool ReverseMode { get; set; }

    public float BalanceSlider { get; set; }

    public string RespawnTime { get; set; }

    public int GameLength { get; set; }

    public int TeamDropdownValue { get; set; } // TODO: This is only used if playing spec ops and technically can be removed if we just set our team to the host's team which is in member data already.

    public TeamEquipmentData Eagle { get; set; } = new();

    public TeamEquipmentData Raven { get; set; } = new();

    public MutatorData Mutators { get; set; } = new();

    public void PopulateData(InstantActionMaps mapsInstance, CachedGameData cache)
    {
        GameMode = mapsInstance.gameModeDropdown.value;
        NightToggle = mapsInstance.nightToggle.isOn;
        PlayersHaveAllWeapons = mapsInstance.playerHasAllWeaponsToggle.isOn;
        ReverseMode = mapsInstance.reverseToggle.isOn;
        BotNumberText = mapsInstance.botNumberField.text;
        BalanceSlider = mapsInstance.balanceSlider.value;
        RespawnTime = mapsInstance.respawnTimeField.text;
        GameLength = mapsInstance.gameLengthDropdown.value;
        SelectedMapIndex = mapsInstance.mapDropdown.value;

        TeamDropdownValue = mapsInstance.teamDropdown.value;

        var mapEntries = cache.Maps;
        if (mapEntries.Count <= SelectedMapIndex)
        {
            LoggingHelper.LogMarker($"Cache.Maps.Count {{'{mapEntries.Count}'}} <= SelectedMapIndex {{'{SelectedMapIndex}'}}");
            cache.UpdateCacheFromIAM(mapsInstance);
            mapEntries = cache.Maps;
        }

        if (mapEntries.Count > SelectedMapIndex)
        {
            SelectedMapName = CachedGameData.GetMapKeyFromEntry(mapEntries[SelectedMapIndex]);
            LoggingHelper.LogMarker($"SelectedMapName {{'{SelectedMapName}'}}");
        }
        else
        {
            Plugin.logger.LogError("Attempted Map Refresh did not fix index out of range issue.");
            Plugin.logger.LogError($"Map count '{mapEntries.Count}' <= SelectedMapIndex '{SelectedMapIndex}'");
        }

        Eagle.GetFromGameSettings(GameInfoContainer.TEAM_EAGLE, cache);
        Raven.GetFromGameSettings(GameInfoContainer.TEAM_RAVEN, cache);

        Mutators.GetFromLoadedMutators(ModManager.instance);
    }

    public bool SetInstantActionMapData(InstantActionMaps mapsInstance, CachedGameData cache)
    {
        if (!SetMap(mapsInstance, cache))
            return false;

        mapsInstance.gameModeDropdown.value = GameMode;
        mapsInstance.nightToggle.isOn = NightToggle;
        mapsInstance.playerHasAllWeaponsToggle.isOn = PlayersHaveAllWeapons;
        mapsInstance.reverseToggle.isOn = ReverseMode;
        mapsInstance.botNumberField.text = BotNumberText;
        mapsInstance.balanceSlider.value = BalanceSlider;
        mapsInstance.respawnTimeField.text = RespawnTime;
        mapsInstance.gameLengthDropdown.value = GameLength;

        // Spec ops forces everyone to the host's team. // TODO: Can we get this magic number elsewhere or should we just store it as a constant for readability.
        if (GameMode == 1)
            mapsInstance.teamDropdown.value = TeamDropdownValue;

        Eagle.SetToGameSettings(GameInfoContainer.TEAM_EAGLE, cache);
        Raven.SetToGameSettings(GameInfoContainer.TEAM_RAVEN, cache);

        Mutators.SetToLoadedMutators(ModManager.instance);

        return true;
    }

    private bool SetMap(InstantActionMaps mapsInstance, CachedGameData cache)
    {
        bool returnVal = SetMapInt(mapsInstance, cache);

        if (returnVal == false)
        {
            //TODO: Test removing this.
            LoggingHelper.LogMarker();
            cache.PopulateCustomMaps();
            returnVal = SetMapInt(mapsInstance, cache);
        }

        return returnVal;
    }

    private bool SetMapInt(InstantActionMaps mapsInstance, CachedGameData cache)
    {
        if (SelectedMapIndex != cache.CustomMapIndex)
        {
            mapsInstance.mapDropdown.value = SelectedMapIndex;
            return true;
        }

        InstantActionMaps.MapEntry currentCustomMap = null;
        LoggingHelper.LogMarker($"mapsInstance.mapDropdown.value {{'{mapsInstance.mapDropdown.value}'}} > cache.CustomMapIndex {{'{cache.CustomMapIndex}'}}");
        if (cache.Maps.Count > cache.CustomMapIndex)
        {
            currentCustomMap = cache.Maps[cache.CustomMapIndex];
            LoggingHelper.LogMarker($"{currentCustomMap}");
        }

        string currentCustomMapName = CachedGameData.GetMapKeyFromEntry(currentCustomMap);
        LoggingHelper.LogMarker($"mapName {{'{currentCustomMapName}'}} != SelectedMapName {{'{SelectedMapName}'}}");

        if (mapsInstance.mapDropdown.value != cache.CustomMapIndex || currentCustomMapName != SelectedMapName)
        {
            if (!cache.CustomMapEntries.TryGetValue(SelectedMapName, out CustomMapEntry entry))
            {
                Plugin.logger.LogError($"Could not find map with selected map name {SelectedMapName}.");
                return false;
            }

            if (entry != null)
                entry.Select();
            else
            {
                string message = $"Returned null entry for {currentCustomMapName}. Checking all other entries in map.\n";
                foreach (var kvp in cache.CustomMapEntries)
                {
                    string mapEntrySceneName = "null";
                    if (kvp.Value != null)
                    {
                        mapEntrySceneName = kvp.Value.entry.sceneName;
                    }
                    message += $"'{kvp.Key}' - '{mapEntrySceneName}' \n";
                }

                LoggingHelper.LogMarker(message);
                return false;
            }
        }

        return true;
    }

}
