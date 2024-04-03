using UnityEngine;

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

    public int TeamDropdownValue { get; set; }

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
        if (mapEntries != null && mapEntries.Count > SelectedMapIndex)
        {
            SelectedMapName = CachedGameData.GetMapKeyFromEntry(mapEntries[SelectedMapIndex]);
        }
        else
        {
            // If this is hit then we've either not got maps at all in the dropdown or we have selected a map outside of that dropdown's data
            // Neither situation should be possible so let's log out what info we have to double check later.
            int count = -1;
            if (mapEntries != null)
                count = mapEntries.Count;
            Plugin.logger.LogError("mapEntries != null && mapEntries.Count > SelectedMapIndex");
            Plugin.logger.LogError($"'{mapEntries != null}' && '{count}' > '{SelectedMapIndex}'");
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
        if (SelectedMapIndex == cache.CustomMapIndex)
        {
            if (mapsInstance.mapDropdown.value != cache.CustomMapIndex || CachedGameData.GetMapKeyFromEntry(cache.Maps[cache.CustomMapIndex]) != SelectedMapName)
            {
                if (!cache.CustomMapEntries.TryGetValue(SelectedMapName, out CustomMapEntry entry))
                {
                    Plugin.logger.LogError($"Could not find map with selected map name {SelectedMapName}.");
                    return false;
                }

                entry.Select();
            }
        }
        else
        {
            mapsInstance.mapDropdown.value = SelectedMapIndex;
        }

        return true;
    }

}
