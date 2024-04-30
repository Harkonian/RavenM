using System;
using System.Collections.Generic;
using System.Linq;

using RavenM.Lobby.DataTransfer;
using UnityEngine;

using static TurretSpawner;
using static VehicleSpawner;

namespace RavenM.Lobby;

internal class TeamEquipmentData : GenericNested<TeamEquipmentData>
{
    [DataTransferIgnored]
    public List<int> WeaponIndices { get; set; } = [];

    [DataTransferIgnored]
    public List<int> VehicleIndices { get; set; } = [];

    [DataTransferIgnored]
    public List<int> TurretIndices { get; set; } = [];

    public int SelectedSkin { get; set; }

    // Using these private transfer variables to do the data transfer whilst still giving us useful typing when dealing with the data in code elsewhere.
    // TODO: These are pretty inefficient as they regenerate every access. Ideally we'd cache this and only regenerate the string if 
    //       we know we've changed the underlying data. Subscribing to events or using harmony patches to figure out when the data is changing would be wise.
    [DataTransferIncluded]
    private string WeaponTransferString
    {
        get => DataPacking.EncodeToString(WeaponIndices);
        set => WeaponIndices = DataPacking.DecodeFromString<int>(value);
    }

    [DataTransferIncluded]
    private string VehicleTransferString
    {
        get => DataPacking.EncodeToString(VehicleIndices);
        set => VehicleIndices = DataPacking.DecodeFromString<int>(value);
    }

    [DataTransferIncluded]
    private string TurretTransferString
    {
        get => DataPacking.EncodeToString(TurretIndices);
        set => TurretIndices = DataPacking.DecodeFromString<int>(value);
    }

    public TeamEquipmentData()
    {
        base.SetSelf(this);
    }

    public void GetFromGameSettings(int teamIndex, CachedGameData cache)
    {
        var teamInfo = GameManager.instance.gameInfo.team[teamIndex];
        HashSet<int> weapons = [];
        foreach (var weapon in teamInfo.availableWeapons)
        {
            int index = cache.WeaponPrefabToIndex[weapon.prefab];
            weapons.Add(index);
        }

        WeaponIndices = weapons.ToList();

        GetIndicesFromPrefabs(VehicleIndices, teamInfo.vehiclePrefab, cache.VehiclePrefabs);
        GetIndicesFromPrefabs(TurretIndices, teamInfo.turretPrefab, cache.TurretPrefabs);

        this.SelectedSkin = InstantActionMaps.instance.skinDropdowns[teamIndex].value;
    }

    public void SetToGameSettings(int teamIndex, CachedGameData cache)
    {
        var teamInfo = GameManager.instance.gameInfo.team[teamIndex];
        teamInfo.availableWeapons.Clear();
        foreach (int weaponIndex in WeaponIndices)
        {
            if (weaponIndex < cache.Weapons.Count)
            {
                var weapon = cache.Weapons[weaponIndex];
                teamInfo.availableWeapons.Add(weapon);
            }
            else
            {
                LoggingHelper.LogMarker($"Attempting to add weapon {weaponIndex} but only have {cache.Weapons.Count} in the cache");
            }
        }

        bool changedVehicles = SetPrefabsFromIndices(VehicleIndices, teamInfo.vehiclePrefab, cache.VehiclePrefabs, (VehicleSpawnType[])Enum.GetValues(typeof(VehicleSpawnType)));
        bool changedTurrets = SetPrefabsFromIndices(TurretIndices, teamInfo.turretPrefab, cache.TurretPrefabs, (TurretSpawnType[])Enum.GetValues(typeof(TurretSpawnType)));


        if (changedVehicles || changedTurrets)
        {
            GamePreview.UpdatePreview();
            LoggingHelper.LogMarker();
        }

        InstantActionMaps.instance.skinDropdowns[teamIndex].value = this.SelectedSkin;
    }

    private void GetIndicesFromPrefabs<T>(List<int> indexArray, Dictionary<T, GameObject> enumToPrefabDict, List<GameObject> cachedPrefabs) where T : struct, IComparable // can't restrict to enums only but at least eliminate classes.
    {
        // WARNING: Technically we're relying on the dictionary to have an entry per enum value.
        // Based on how the game works it should, but if things fall out of sync this is probably why.
        List<KeyValuePair<T, GameObject>> sortedDict = enumToPrefabDict.ToList();
        sortedDict.Sort((lhs, rhs) => lhs.Key.CompareTo(rhs.Key));
        List<GameObject> sortedPrefabs = new(sortedDict.Count);
        foreach (var kvp in sortedDict)
        {
            sortedPrefabs.Add(kvp.Value);
        }

        GetIndicesFromPrefabs(indexArray, sortedPrefabs, cachedPrefabs);
    }

    private void GetIndicesFromPrefabs(List<int> indexArray, IEnumerable<GameObject> selectedPrefabs, List<GameObject> cachedPrefabs)
    {
        int currentPrefab = 0;
        foreach (var prefab in selectedPrefabs)
        {
            int index = cachedPrefabs.IndexOf(prefab);
            if (indexArray.Count <= currentPrefab)
                indexArray.Add(index);
            else
                indexArray[currentPrefab] = index;

            currentPrefab += 1;
        }
    }

    private bool SetPrefabsFromIndices<T>(List<int> indexArray, Dictionary<T, GameObject> enumToPrefabDict, List<GameObject> cachedPrefabs, T[] enumValues) where T : struct, IComparable
    {
        bool ret = false;
        if (enumValues.Length != indexArray.Count)
        {
            Plugin.logger.LogError($"Something fell out of sync between the values of {nameof(T)} which has {enumValues.Length} whilst the parsed index array has {indexArray.Count}");
            return ret;
        }

        for (int i = 0; i < indexArray.Count; i++)
        {
            int prefabIndex = indexArray[i];
            T enumValue = enumValues[i];
            GameObject prefab = prefabIndex != -1 ? cachedPrefabs[prefabIndex] : null;
            enumToPrefabDict[enumValue] = prefab;
            string name = prefab ? prefab.name : "NULL";
        }

        return ret;
    }
}