using RavenM.Helpers;
using SimpleJSON;
using System.Collections.Generic;
using System.Linq;

namespace RavenM.Lobby
{
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
                        serializedConfigs.Add(node);
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
                    foreach (var serializedSetting in serializedConfg)
                    {
                        settingStrings.Add(serializedSetting.Value.ToString());
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

                if (!mutator.isEnabled)
                    continue;
                EnabledMutators.Add(i);

                List<string> configFieldStrings = new();
                foreach (var item in mutator.configuration.GetAllFields())
                {
                    configFieldStrings.Add(item.SerializeValue());
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

            foreach (var mutator in modManager.loadedMutators) 
            {
                mutator.isEnabled = false;
            }

            for (int i = 0; i < EnabledMutators.Count; i++)
            {
                int mutatorIndex = EnabledMutators[i];
                List<string> mutatorConfig = MutatorConfigs[i];

                var mutator = modManager.loadedMutators[mutatorIndex];
                mutator.isEnabled = true;

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
}
