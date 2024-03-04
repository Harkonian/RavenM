﻿using System.IO;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace RavenM
{
    [HarmonyPatch(typeof(ExplodingProjectile), "Explode")]
    public class ProjectileExplodePatch
    {
        // TODO: Should we send over the exact position and up vector as well?
        static void Prefix(ExplodingProjectile __instance, Vector3 position, Vector3 up)
        {
            if (!IngameNetManager.instance.IsClient)
                return;

            var guidComponent = __instance.GetComponent<GuidComponent>();

            if (guidComponent == null)
                return;

            var id = guidComponent.guid;

            if (!IngameNetManager.instance.OwnedProjectiles.Contains(id))
                return;

            using MemoryStream memoryStream = new MemoryStream();
            var explodePacket = new ExplodeProjectilePacket
            {
                Id = id,
            };

            using (var writer = new ProtocolWriter(memoryStream))
            {
                writer.Write(explodePacket);
            }

            byte[] data = memoryStream.ToArray();
            IngameNetManager.instance.SendPacketToServer(data, PacketType.Explode, Constants.k_nSteamNetworkingSend_Reliable);
        }
    }

    public class ExplodeProjectilePacket
    {
        public int Id;
    }
}
