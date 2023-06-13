using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RavenM.Lobby
{
    // Once all mods have been loaded in this data is cached once rather than repeatedly using reflection to grab it every frame.
    public class CachedMapData
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

        public Dictionary<string, InstantActionMaps.MapEntry> CustomMapEntries { get; private set; } = new Dictionary<string, InstantActionMaps.MapEntry>();

        public CachedMapData(InstantActionMaps instantActionMaps)
        {
            PopulateCustomMaps();
            UpdateCacheFromIAM(instantActionMaps);
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
            Plugin.logger.LogInfo($"Populating MapCache Custom Maps Entries.");

            foreach (ModInformation mod in ModManager.instance.GetActiveMods())
            {
                foreach (var map in mod.content.GetMaps())
                {
                    int substringEndIndex = map.Name.LastIndexOf('.');
                    substringEndIndex = substringEndIndex != -1 ? substringEndIndex : map.Name.Length;
                    string currentName = map.Name.Substring(0, substringEndIndex);

                    Sprite mapSprite = null;
                    string specificMapIconName = $"{map.FullName}.png";

                    if (File.Exists(specificMapIconName))
                    {
                        try
                        {
                            Plugin.logger.LogInfo($"Found map specific icon for map '{map.Name}'.");
                            Texture2D tex = new Texture2D(2, 2);
                            ImageConversion.LoadImage(tex, File.ReadAllBytes(specificMapIconName));
                            mapSprite = Sprite.Create(tex, new Rect(0.0f, 0.0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                        }
                        catch (Exception e)
                        {
                            Plugin.logger.LogError(e);
                        }
                    }
                    else if (mod.content.HasIconImage())
                    {
                        mapSprite = Sprite.Create(mod.iconTexture, new Rect(0f, 0f, mod.iconTexture.width, mod.iconTexture.height), new Vector2(0.5f, 0.5f));
                    }

                    InstantActionMaps.MapEntry entry = new InstantActionMaps.MapEntry
                    {
                        name = currentName,
                        sceneName = map.FullName,
                        isCustomMap = true,
                        hasLoadedMetaData = true,
                        image = mapSprite,
                        suggestedBots = 0,
                    };

                    CustomMapEntries[currentName] = entry;
                }
            }
        }
    }
}
