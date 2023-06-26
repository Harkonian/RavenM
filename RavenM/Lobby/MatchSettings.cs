using RavenM.Helpers;

namespace RavenM.Lobby
{
    internal class MatchSettings : GenericEquatable<MatchSettings>
    {
        public MatchSettings() { }

        public int GameMode { get; set; }

        public bool NightToggle { get; set; }

        public bool PlayersHaveAllWeapons { get; set; }

        public bool ReverseMode { get; set; }

        public string BotNumberText { get; set; }

        public float BalanceSlider { get; set; }

        public string RespawnTime { get; set; }

        public int GameLength { get; set; }

        public int SelectedMapIndex { get; set; }
        
        public string SelectedMapName { get; set; } = string.Empty;

        public int TeamDropdownValue { get; set; }

        public bool MatchStarted { get; set; } = false;

        public TeamEquipmentData Eagle {get; set;} = new();

        public TeamEquipmentData Raven {get; set;} = new();

        public MutatorData Mutators { get; set;} = new();


        public static MatchSettings FromInstantActionMapData(InstantActionMaps mapsInstance, CachedGameData cache)
        {
            MatchSettings result = new MatchSettings();
            result.PopulateData(mapsInstance, cache);
            return result;
        }

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
                SelectedMapName = mapEntries[SelectedMapIndex].name;
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

        public void SetInstantActionMapData(InstantActionMaps mapsInstance, CachedGameData cache)
        {
            mapsInstance.gameModeDropdown.value = GameMode;
            mapsInstance.nightToggle.isOn = NightToggle;
            mapsInstance.playerHasAllWeaponsToggle.isOn = PlayersHaveAllWeapons;
            mapsInstance.reverseToggle.isOn = ReverseMode;
            mapsInstance.botNumberField.text = BotNumberText;
            mapsInstance.balanceSlider.value = BalanceSlider;
            mapsInstance.respawnTimeField.text = RespawnTime;
            mapsInstance.gameLengthDropdown.value = GameLength;

            // TODO: Spec ops forces everyone to the hosts team.
            if (GameMode == 1)
                mapsInstance.teamDropdown.value = TeamDropdownValue;

            Eagle.SetToGameSettings(GameInfoContainer.TEAM_EAGLE, cache);
            Raven.SetToGameSettings(GameInfoContainer.TEAM_RAVEN, cache);

            Mutators.SetToLoadedMutators(ModManager.instance);
        }
    }
}
