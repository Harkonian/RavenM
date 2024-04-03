using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RavenM.Lobby;
// Once all mods have been loaded in this data is cached once rather than repeatedly using reflection to grab it every frame.
public class CachedGameData
{
    List<InstantActionMaps.MapEntry> maps = null;

    // This is almost a constant, it represents the value that traslates to "Load a custom map" rather than "The index of a built-in map"
    public int CustomMapIndex { get; private set; }

    public List<InstantActionMaps.MapEntry> Maps
    {
        get
        {
            if (maps == null)
            {
                UpdateCacheFromIAM(InstantActionMaps.instance);
            }
            return maps;
        }
    }

    public Dictionary<string, CustomMapEntry> CustomMapEntries { get; private set; } = [];

    // These will contain all prefabs, both from default content and mods and will be our definitive list to use the index on.
    public List<GameObject> TurretPrefabs { get; private set; } = [];

    public List<GameObject> VehiclePrefabs { get; private set; } = [];

    public List<WeaponManager.WeaponEntry> Weapons { get; private set; } = [];

    public Dictionary<GameObject, int> WeaponPrefabToIndex { get; private set; } = [];

    // Separator to be used between a modded map's workshop ID and its file name. We want a non-path separator for potential cross plat issues.
    public const string CustomMapKeyDesignator = " -- ";

    public CachedGameData(InstantActionMaps instantActionMaps)
    {
        PopulateCustomMaps();
        UpdateCacheFromIAM(instantActionMaps);
        
        VehiclePrefabs.AddRange(ActorManager.instance.defaultVehiclePrefabs);
        VehiclePrefabs.AddRange(ModManager.AllVehiclePrefabs());
        VehiclePrefabs.Sort((x, y) => x.name.CompareTo(y.name));

        
        TurretPrefabs.AddRange(ActorManager.instance.defaultTurretPrefabs);
        TurretPrefabs.AddRange(ModManager.AllTurretPrefabs());
        TurretPrefabs.Sort((x, y) => x.name.CompareTo(y.name));

        PopulateWeaponCache();
    }

    public void UpdateCacheFromIAM(InstantActionMaps mapsInstance)
    {
        Plugin.logger.LogInfo($"Updating MapCache from IAM instance (mapsInstance != null = {mapsInstance != null}).");
        CustomMapIndex = (int)typeof(InstantActionMaps).GetField("customMapOptionIndex", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mapsInstance);
        maps = (List<InstantActionMaps.MapEntry>)typeof(InstantActionMaps).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mapsInstance);
    }

    private void PopulateCustomMaps()
    {
        CustomMapEntries.Clear();
        Plugin.logger.LogInfo("Populating MapCache Custom Maps Entries.");

        foreach (Transform item in InstantActionMaps.instance.customMapsBrowser.contentPanel)
        {
            var entryComponent = item.gameObject.GetComponent<CustomMapEntry>();

            if (entryComponent == null)
            {
                Plugin.logger.LogWarning("A Transform in the custom maps browser does not have a CustomMapEntry component! This should be reported to the RavenM developers to investigate how this happened.");
                continue;
            }

            string key = GetMapKeyFromEntry(entryComponent.entry);

            Plugin.logger.LogInfo(key);
            Plugin.logger.LogInfo($"sceneName = {entryComponent.entry.sceneName}");
            Plugin.logger.LogInfo($"sourceMod = {entryComponent.entry.sourceMod}");
            Plugin.logger.LogInfo($"metaData.displayName = {entryComponent.entry.metaData.displayName}");
            Plugin.logger.LogInfo($"metaData.suggestedBots = {entryComponent.entry.metaData.suggestedBots}");

            CustomMapEntries[key] = entryComponent;
        }
    }

    public static string GetMapKeyFromEntry(InstantActionMaps.MapEntry entry)
    {
        if (entry == null)
        { 
            return "Unknown"; 
        }

        if (entry.sceneName.IndexOf(Path.PathSeparator) != -1)
        {
            return entry.sceneName; // default maps don't have any path info in their scene name.
        }

        string workshopDirName = Path.GetFileName(Path.GetDirectoryName(entry.sceneName));

        // This is a modded map, The directory name should be the steam workshop ID of the mod and the filename of the map must be unique in that folder.
        // We're combining the two without using the normal path separator just in case there is a difference in operating systems between host/client.
        return string.Concat(workshopDirName, CustomMapKeyDesignator, Path.GetFileName(entry.sceneName));
    }

    public static bool IsCustomMapKey(string mapKey)
    {
        return mapKey.Contains(CustomMapKeyDesignator);
    }

    private void PopulateWeaponCache()
    {
        Dictionary<GameObject, WeaponManager.WeaponEntry> prefabToEntries = [];

        foreach (WeaponManager.WeaponEntry weaponEntry in WeaponManager.instance.allWeapons)
        {
            // Removing duplicate entries as they all map back to the same weapon prefab and turning one one will turn them all on.
            if (!prefabToEntries.ContainsKey(weaponEntry.prefab))
            {
                prefabToEntries.Add(weaponEntry.prefab, weaponEntry);
                Weapons.Add(weaponEntry);
            }
        }

        Weapons.Sort((x, y) => x.nameHash.CompareTo(y.nameHash));


        string weaponString = "";
        int duplicates = 0;
        int nonMatchingDupes = 0;

        for (int i = 0; i < Weapons.Count; i++)
        {
            var weapon = Weapons[i];

            if (WeaponPrefabToIndex.ContainsKey(weapon.prefab))
            {
                int oldIndex = WeaponPrefabToIndex[weapon.prefab];
                var oldWeapon = Weapons[oldIndex];
                if (oldWeapon.prefab != weapon.prefab)
                {
                    LoggingHelper.LogMarker($"Indices {oldIndex} & {i} match hashes ({weapon.nameHash}) but are not the same {weapon.name} & {oldWeapon.name}", false);
                    LoggingHelper.LogMarker($"{oldWeapon.slot} - {weapon.slot}", false);
                    LoggingHelper.LogMarker($"{oldWeapon.prefab.name} - {weapon.prefab.name}", false);
                    LoggingHelper.LogMarker($"{oldWeapon.prefab.GetHashCode()} - {weapon.prefab.GetHashCode()}", false);
                    nonMatchingDupes++;
                }
                duplicates++;
            }
            WeaponPrefabToIndex[weapon.prefab] = i;
            weaponString += $"{i} - {weapon.name} - {weapon.nameHash}\n";
        }

        LoggingHelper.LogMarker(weaponString);
        if (duplicates > 0)
        {
            LoggingHelper.LogMarker($"Duplicate Count = {duplicates} {nonMatchingDupes} of which don't match.");

        }
    }
}