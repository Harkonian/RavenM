using System.Collections.Generic;

namespace RavenM.Lobby
{
    public class MatchSettings : GenericEquatable<MatchSettings>
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


        public static MatchSettings FromInstantActionMapData(InstantActionMaps mapsInstance, IList<InstantActionMaps.MapEntry> mapEntries)
        {
            MatchSettings result = new MatchSettings();
            result.PopulateData(mapsInstance, mapEntries);
            return result;
        }

        public void PopulateData(InstantActionMaps mapsInstance, IList<InstantActionMaps.MapEntry> mapEntries)
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
            if (mapEntries != null && mapEntries.Count > SelectedMapIndex)
                SelectedMapName = mapEntries[SelectedMapIndex].name;
            else
            {
                int count = -1;
                if (mapEntries != null)
                    count = mapEntries.Count;
                Plugin.logger.LogError("mapEntries != null && mapEntries.Count > SelectedMapIndex");
                Plugin.logger.LogError($"'{mapEntries != null}' && '{count}' > '{SelectedMapIndex}'");
            }
        }

        public void SetInstantActionMapData(InstantActionMaps mapsInstance)
        {
            mapsInstance.gameModeDropdown.value = GameMode;
            mapsInstance.nightToggle.isOn = NightToggle;
            mapsInstance.playerHasAllWeaponsToggle.isOn = PlayersHaveAllWeapons;
            mapsInstance.reverseToggle.isOn = ReverseMode;
            mapsInstance.botNumberField.text = BotNumberText;
            mapsInstance.balanceSlider.value = BalanceSlider;
            mapsInstance.respawnTimeField.text = RespawnTime;
            mapsInstance.gameLengthDropdown.value = GameLength;
            // TODO: This currently is only set if it's not a custom map so we're going to emulate that for now. Experiment with just setting it regardless.
            //mapsInstance.mapDropdown.value = SelectedMapIndex;

            // TODO: Spec ops forces everyone to the hosts team.
            if (GameMode == 1)
                mapsInstance.teamDropdown.value = TeamDropdownValue;
        }
    }
}
