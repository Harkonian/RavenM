using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

using HarmonyLib;
using Ravenfield.Trigger;
using RavenM.RSPatch.Wrapper;
using Steamworks;
using UnityEngine;

namespace RavenM.Lobby;

[HarmonyPatch(typeof(ModManager), nameof(ModManager.OnGameManagerStart))]
public class CleanupListPatch
{
    static void Prefix(ModManager __instance)
    {
        if (__instance.noContentMods)
            __instance.noWorkshopMods = true;
    }
}
    
[HarmonyPatch(typeof(GameManager), nameof(GameManager.StartLevel))]
public class OnStartPatch
{
    static bool Prefix()
    {
        if (LobbySystem.instance.InLobby && !LobbySystem.instance.IsLobbyOwner && !LobbySystem.instance.ReadyToPlay)
            return false;
        OptionsPatch.SetConfigValues(false);

        // Only start if all members are ready.
        if (LobbySystem.instance.LobbyDataReady && LobbySystem.instance.IsLobbyOwner)
        {
            foreach (var memberId in LobbySystem.instance.GetLobbyMembers())
            {
                if (SteamMatchmaking.GetLobbyMemberData(LobbySystem.instance.LobbyID, memberId, "loaded") != "yes")
                {
                    if (!LobbySystem.instance.HasCommittedToStart) {
                        LobbySystem.instance.IntentionToStart = true;
                        return false;
                    }
                }
            }
            LobbySystem.instance.HasCommittedToStart = false;
        }

        if (LobbySystem.instance.IsLobbyOwner)
        {
            IngameNetManager.instance.OpenRelay();
            LobbySystem.instance.HostStartedMatch();
        }

        LobbySystem.instance.ReadyToPlay = false;
        return true;
    }
}

[HarmonyPatch(typeof(LoadoutUi), nameof(LoadoutUi.OnDeployClick))]
public class FirstDeployPatch
{
    static bool Prefix()
    {
        if (LobbySystem.instance.InLobby)
        {
            // Ignore players who joined mid-game.
            if ((bool)typeof(LoadoutUi).GetField("hasAcceptedLoadoutOnce", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(LoadoutUi.instance))
                return true;

            // Wait for everyone to load in first.
            foreach (var memberId in LobbySystem.instance.GetLobbyMembers())
            {
                if (SteamMatchmaking.GetLobbyMemberData(LobbySystem.instance.LobbyID, memberId, "ready") != "yes")
                {
                    // Ignore players that just joined and are loading mods.
                    if (SteamMatchmaking.GetLobbyMemberData(LobbySystem.instance.LobbyID, memberId, "loaded") != "yes")
                        continue;

                    return false;
                }
            }
        }
        if (IngameNetManager.instance.IsHost || LobbySystem.instance.IsLobbyOwner)
        {
            Plugin.logger.LogInfo("SendNetworkGameObjectsHashesPacket()");
            WLobby.SendNetworkGameObjectsHashesPacket();
        }
        return true;
    }
}

[HarmonyPatch(typeof(GameManager), "StartGame")]
public class FinalizeStartPatch
{
    // Maps sometimes have their own vehicles. We need to tag them.
    static void Prefix()
    {
        if (!LobbySystem.instance.InLobby)
            return;

        // The game will destroy any vehicles that have already spawned. Ignore them.
        var ignore = UnityEngine.Object.FindObjectsOfType<Vehicle>();
        Plugin.logger.LogInfo($"Ignore list: {ignore.Length}");

        var map = GameManager.instance.lastMapEntry;

        foreach (var vehicle in Resources.FindObjectsOfTypeAll<Vehicle>())
        {
            if (!vehicle.TryGetComponent(out PrefabTag _) && !Array.Exists(ignore, x => x == vehicle))
            {
                Plugin.logger.LogInfo($"Detected map vehicle with name: {vehicle.name}, and from map: {map.metaData.displayName}.");

                var tag = vehicle.gameObject.AddComponent<PrefabTag>();
                tag.NameHash = vehicle.name.GetHashCode();
                tag.Mod = (ulong)map.metaData.displayName.GetHashCode();
                IngameNetManager.PrefabCache[new Tuple<int, ulong>(tag.NameHash, tag.Mod)] = vehicle.gameObject;
            }
        }

        foreach (var projectile in Resources.FindObjectsOfTypeAll<Projectile>())
        {
            if (!projectile.TryGetComponent(out PrefabTag _))
            {
                Plugin.logger.LogInfo($"Detected map projectile with name: {projectile.name}, and from map: {map.metaData.displayName}.");

                var tag = projectile.gameObject.AddComponent<PrefabTag>();
                tag.NameHash = projectile.name.GetHashCode();
                tag.Mod = (ulong)map.metaData.displayName.GetHashCode();
                IngameNetManager.PrefabCache[new Tuple<int, ulong>(tag.NameHash, tag.Mod)] = projectile.gameObject;
            }
        }

        foreach (var destructible in Resources.FindObjectsOfTypeAll<Destructible>())
        {
            var prefab = DestructiblePacket.Root(destructible);

            if (!prefab.TryGetComponent(out PrefabTag _))
            {
                Plugin.logger.LogInfo($"Detected map destructible with name: {prefab.name}, and from map: {map.metaData.displayName}.");

                IngameNetManager.TagPrefab(prefab, (ulong)map.metaData.displayName.GetHashCode());
            }
        }

        foreach (var destructible in UnityEngine.Object.FindObjectsOfType<Destructible>())
        {
            // One shot created destructibles -- not cool!
            var root = DestructiblePacket.Root(destructible);

            if (IngameNetManager.instance.ClientDestructibles.ContainsValue(root))
                continue;

            // FIXME: Shitty hack. The assumption is map destructibles are consistent
            // and thus will always spawn in the same positions regardless of the
            // client run. I have no idea how correct this assumption actually is.
            int id = root.transform.position.GetHashCode() ^ root.name.GetHashCode();

            if (root.TryGetComponent(out GuidComponent guid))
                id = guid.guid;
            else
                root.AddComponent<GuidComponent>().guid = id;

            IngameNetManager.instance.ClientDestructibles[id] = root;

            Plugin.logger.LogInfo($"Registered new destructible root with name: {root.name} and id: {id}");
        }

        IngameNetManager.instance.MapWeapons.Clear();
        foreach (var triggerEquipWeapon in Resources.FindObjectsOfTypeAll<TriggerEquipWeapon>())
        {
            if (triggerEquipWeapon.weaponType == TriggerEquipWeapon.WeaponType.FromWeaponEntry
                && triggerEquipWeapon.weaponEntry != null
                && !WeaponManager.instance.allWeapons.Contains(triggerEquipWeapon.weaponEntry))
            {
                var entry = triggerEquipWeapon.weaponEntry;
                Plugin.logger.LogInfo($"Detected map weapon with name: {entry.name}, and from map: {map.metaData.displayName}.");
                IngameNetManager.instance.MapWeapons.Add(entry);
            }
        }
    }

    static void Postfix()
    {
        if (!LobbySystem.instance.LobbyDataReady)
            return;

        if (LobbySystem.instance.IsLobbyOwner)
            IngameNetManager.instance.StartAsServer();
        else
            IngameNetManager.instance.StartAsClient(LobbySystem.instance.OwnerID); 

        SteamMatchmaking.SetLobbyMemberData(LobbySystem.instance.LobbyID, "ready", "yes");
    }
}

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.GoBack))]
public class GoBackPatch
{
    static bool Prefix()
    {
        if (LobbySystem.instance.InLobby && (int)typeof(MainMenu).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(MainMenu.instance) == MainMenu.PAGE_INSTANT_ACTION)
            return false;

        return true;
    }
}

[HarmonyPatch(typeof(InstantActionMaps), "SetupSkinList")]
public class SkinListPatch
{
    static void Prefix() => ModManager.instance.actorSkins.Sort((x, y) => x.name.CompareTo(y.name));
}

[HarmonyPatch(typeof(ModManager), nameof(ModManager.FinalizeLoadedModContent))]
public class AfterModsLoadedPatch
{
    static void Postfix()
    {
        if (InstantActionMaps.instance != null)
        {
            // We need to update the skin dropdown with the new mods.
            typeof(InstantActionMaps).GetMethod("SetupSkinList", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(InstantActionMaps.instance, null);
        }

        ModManager.instance.ContentChanged();

        LobbySystem lobby = LobbySystem.instance;

        if (!lobby.InLobby || !lobby.LobbyDataReady || lobby.IsLobbyOwner || lobby.ModsToDownload.Count > 0)
            return;

        // We're in a lobby that we've just finished downloading mods for! Let's indicate to the lobby system we've finished loading.
        lobby.CompletedLoading();
    }
}

// TODO: Why does this class exist? Why do we need to override the steam matchmaking classes?
//       Is this just because we might leave the lobby in a few ways? As in from the chat manager or the normal lobby system?
//       If so this is extra wonky. Why not just roll this functionality into our lobby class with a LeaveLobby call?
[HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.LeaveLobby))]
public class OnLobbyLeavePatch
{
    static void Postfix()
    {
        LobbySystem.instance.ResetState();

        ChatManager.instance.ResetChat();
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.ReturnToMenu))]
public class LeaveOnEndGame
{
    static void Prefix()
    {
        if (!LobbySystem.instance.InLobby || LobbySystem.instance.IsLobbyOwner)
            return;

        // Exit the lobby if we actually want to leave.
        if (new StackFrame(2).GetMethod().Name == "Menu")
            SteamMatchmaking.LeaveLobby(LobbySystem.instance.LobbyID);
    }

    static void Postfix()
    {
        if (!LobbySystem.instance.InLobby)
            return;

        if (LobbySystem.instance.IsLobbyOwner)
        {
            LobbySystem.instance.HostEndedMatch();
        }

        SteamMatchmaking.SetLobbyMemberData(LobbySystem.instance.LobbyID, "ready", "no");
    }
}

[HarmonyPatch(typeof(ModManager), nameof(ModManager.GetActiveMods))]
public class ActiveModsPatch
{
    static bool Prefix(ModManager __instance, ref List<ModInformation> __result)
    {
        if (LobbySystem.instance.LoadedServerMods && LobbySystem.instance.ServerMods.Count > 0)
        {
            __result = new List<ModInformation>();
            foreach (var mod in __instance.mods)
            {
                if (LobbySystem.instance.ServerMods.Contains(mod.workshopItemId))
                {
                    __result.Add(mod);
                }
            }
            return false;
        }

        return true;
    }
}