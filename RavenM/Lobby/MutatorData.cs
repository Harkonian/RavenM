using System.Collections.Generic;

using RavenM.Lobby.DataTransfer;
using SimpleJSON;

namespace RavenM.Lobby;

internal class MutatorData : GenericNested<MutatorData>
{
    [DataTransferIgnored]
    public List<int> EnabledMutators { get; private set; } = new();

    [DataTransferIgnored]
    public List<List<string>> MutatorConfigs { get; private set; } = new();

    [DataTransferIncluded]
    private string EnabledMutatorsTransferString
    {
        get => DataPacking.EncodeToString(EnabledMutators);
        set => EnabledMutators = DataPacking.DecodeFromString<int>(value);
    }

    [DataTransferIncluded]
    private string MutatorConfigTransferString
    {
        get
        {
            var serializedConfigs = new JSONArray();
            foreach (var config in MutatorConfigs)
            {
                var serializedConfig = new JSONArray();
                foreach (var setting in config)
                {
                    JSONNode node = new JSONString(setting);
                    serializedConfig.Add(node);
                }

                serializedConfigs.Add(serializedConfig);
            }

            return serializedConfigs.ToString();
        }
        set
        {
            MutatorConfigs.Clear();
            JSONArray serializedConfigs = JSON.Parse(value).AsArray;

            List<JSONArray> allConfigs = new();

            foreach (var jsonConfigs in serializedConfigs)
            {
                List<string> settingStrings = new();
                JSONArray serializedConfg = jsonConfigs.Value.AsArray;
                if (serializedConfg != null)
                {
                    foreach (var serializedSetting in serializedConfg)
                    {
                        string finalSettingString = serializedSetting.Value.Value;
                        settingStrings.Add(finalSettingString);
                    }
                }
                MutatorConfigs.Add(settingStrings);
            }
        }
    }

    public MutatorData()
    {
        base.SetSelf(this);
    }

    public void GetFromLoadedMutators(ModManager modManager)
    {
        EnabledMutators.Clear();
        MutatorConfigs.Clear();
        for (int i = 0; i < modManager.loadedMutators.Count; i++)
        {
            var mutator = modManager.loadedMutators[i];

            if (!GameManager.instance.gameInfo.activeMutators.Contains(mutator))
                continue;

            EnabledMutators.Add(i);

            List<string> configFieldStrings = [];
            string combinedConfig = $"Mutator {i} - {mutator.name}\n";
            foreach (var item in mutator.configuration.GetAllFields())
            {
                string serializedValue = item.SerializeValue();
                configFieldStrings.Add(serializedValue);
                combinedConfig += $"{serializedValue}\n";
            }

            MutatorConfigs.Add(configFieldStrings);
        }
    }

    public void SetToLoadedMutators(ModManager modManager)
    {
        if (EnabledMutators.Count != MutatorConfigs.Count)
        {
            Plugin.logger.LogError($"Attempted to set mutator data but the list sizes do not match. {nameof(EnabledMutators)}({EnabledMutators.Count}) != {nameof(MutatorConfigs)}({MutatorConfigs.Count})");
            return;
        }

        GameManager.instance.gameInfo.activeMutators.Clear();

        for (int i = 0; i < EnabledMutators.Count; i++)
        {
            int mutatorIndex = EnabledMutators[i];
            List<string> mutatorConfig = MutatorConfigs[i];

            var mutator = modManager.loadedMutators[mutatorIndex];
            GameManager.instance.gameInfo.activeMutators.Add(mutator);

            var mutatorConfigFields = mutator.configuration.GetAllFields();

            int configFieldIndex = 0;
            foreach (var item in mutatorConfigFields)
            {
                if (configFieldIndex < mutatorConfig.Count)
                    item.DeserializeValue(mutatorConfig[configFieldIndex]);
                else
                    Plugin.logger.LogError($"Attempting to set configurations on mutator {mutator.name} which had {mutatorConfig.Count} values deserialized but the mutator is asking for element {configFieldIndex}.");
                configFieldIndex++;
            }
        }
    }
}