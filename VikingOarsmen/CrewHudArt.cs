using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// The crew HUD's artwork: sprites loaded from the PNGs embedded in the DLL (drawn by
    /// art/crew_hud/build_svgs.py), and which top-down drawing to use for each ship.
    /// </summary>
    internal static class CrewHudArt
    {
        private const string ResourcePrefix = "VikingOarsmen.Textures.";

        // Ships without a drawing of their own (modded ones) get the longship stretched over their float collider.
        private const string FallbackShip = "ship_longship";

        // Keyed by the ship's prefab name. The rectangle is the area the drawing covers, in ship-local meters
        // (half its width, then its stern and bow ends along z); it must match build_svgs.py.
        private static readonly Dictionary<string, ShipArt> s_ships = new Dictionary<string, ShipArt>
        {
            { "Raft", new ShipArt("ship_raft", 3.6f, -3.6f, 3.6f) },
            { "Karve", new ShipArt("ship_karve", 1.95f, -6.27f, 6.34f) },
            { "VikingShip", new ShipArt("ship_longship", 3.35f, -12.2f, 12.2f) },
            // The Trailership is a longship hull with the same benches.
            { "Trailership", new ShipArt("ship_longship", 3.35f, -12.2f, 12.2f) },
            { "VikingShip_Ashlands", new ShipArt("ship_drakkar", 6.6f, -18.8f, 19.8f) },
        };

        private static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();

        // ImageConversion.LoadImage, looked up at runtime: its module is built against netstandard 2.1, which a
        // net462 project can't reference at compile time.
        private static readonly MethodInfo s_loadImage = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
            ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        internal class ShipArt
        {
            internal readonly string Sprite;
            internal readonly float HalfWidth;
            internal readonly float ZMin;
            internal readonly float ZMax;

            internal ShipArt(string sprite, float halfWidth, float zMin, float zMax)
            {
                Sprite = sprite;
                HalfWidth = halfWidth;
                ZMin = zMin;
                ZMax = zMax;
            }
        }

        /// <summary>
        /// The drawing for a ship, or the fallback stretched over its float collider.
        /// </summary>
        internal static ShipArt For(Ship ship)
        {
            if (s_ships.TryGetValue(Utils.GetPrefabName(ship.gameObject), out ShipArt art))
            {
                return art;
            }

            BoxCollider box = ship.m_floatCollider;
            if (box == null)
            {
                return s_ships["VikingShip"];
            }
            return new ShipArt(FallbackShip, Mathf.Max(box.size.x, 0.5f) / 2f,
                box.center.z - Mathf.Max(box.size.z, 0.5f) / 2f, box.center.z + Mathf.Max(box.size.z, 0.5f) / 2f);
        }

        /// <summary>
        /// The sprite for an embedded PNG (by file name, without extension), loaded once; null if it's missing.
        /// </summary>
        internal static Sprite Get(string name)
        {
            if (s_sprites.TryGetValue(name, out Sprite sprite))
            {
                return sprite;
            }

            sprite = Load(name);
            s_sprites[name] = sprite;
            return sprite;
        }

        private static Sprite Load(string name)
        {
            using (Stream stream = typeof(CrewHudArt).Assembly.GetManifestResourceStream(ResourcePrefix + name + ".png"))
            {
                if (stream == null)
                {
                    Plugin.Log.LogError($"Embedded texture '{name}.png' not found in the DLL.");
                    return null;
                }

                byte[] data = new byte[stream.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int count = stream.Read(data, read, data.Length - read);
                    if (count <= 0)
                    {
                        break;
                    }
                    read += count;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (s_loadImage == null || !(bool)s_loadImage.Invoke(null, new object[] { texture, data }))
                {
                    Plugin.Log.LogError($"Embedded texture '{name}.png' couldn't be decoded.");
                    return null;
                }
                texture.name = "VikingOarsmen_" + name;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
        }
    }
}
