using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Splash and sound of the blade hitting the water. Valheim has no oar splash, so the one an arrow
    /// makes when it falls into the water (Projectile.m_hitWaterEffects) is reused.
    /// </summary>
    /// <remarks>
    /// Every client plays it for every rower, from the oar it draws, so nothing is sent over the network.
    /// </remarks>
    internal static class OarSplash
    {
        // Projectiles tried first, in order of preference; any other arrow with a water hit effect is the fallback.
        private static readonly string[] s_sourceProjectiles = { "bow_projectile", "bow_projectile_fire", "bow_projectile_frost" };

        // Read by name, so a game update renaming it only turns the splash off instead of breaking the mod.
        private static readonly FieldInfo s_hitWaterEffectsField = AccessTools.Field(typeof(Projectile), "m_hitWaterEffects");

        private static readonly GameObject[] s_none = new GameObject[0];

        // Effect prefabs (particles and sound), resolved once.
        private static GameObject[] s_prefabs;

        /// <summary>
        /// Plays the splash on the water surface at this world position.
        /// </summary>
        internal static void Play(Vector3 position)
        {
            if (!Plugin.Splash.Value)
            {
                return;
            }

            foreach (GameObject prefab in GetPrefabs())
            {
                // The effects remove themselves when done, like every vanilla effect.
                Object.Instantiate(prefab, position, Quaternion.identity);
            }
        }

        private static GameObject[] GetPrefabs()
        {
            if (s_prefabs != null)
            {
                return s_prefabs;
            }

            // Try again next time if the prefabs aren't loaded yet.
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return s_none;
            }

            foreach (string name in s_sourceProjectiles)
            {
                if (TryGetSplash(scene.GetPrefab(name), out s_prefabs))
                {
                    return s_prefabs;
                }
            }
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab != null && (prefab.name.StartsWith("bow_projectile") || prefab.name.ToLowerInvariant().Contains("arrow"))
                    && TryGetSplash(prefab, out s_prefabs))
                {
                    return s_prefabs;
                }
            }

            Plugin.Log.LogWarning("No arrow water splash found in the game; the oar won't splash.");
            s_prefabs = s_none;
            return s_prefabs;
        }

        /// <summary>
        /// The effect prefabs a projectile spawns when it hits the water, if it has any.
        /// </summary>
        private static bool TryGetSplash(GameObject projectilePrefab, out GameObject[] prefabs)
        {
            prefabs = null;
            Projectile projectile = projectilePrefab != null ? projectilePrefab.GetComponent<Projectile>() : null;
            EffectList effects = projectile != null && s_hitWaterEffectsField != null
                ? s_hitWaterEffectsField.GetValue(projectile) as EffectList
                : null;
            if (effects == null || effects.m_effectPrefabs == null)
            {
                return false;
            }

            // Only plain local effects: anything networked would be spawned once per client.
            List<GameObject> found = new List<GameObject>();
            foreach (var effect in effects.m_effectPrefabs)
            {
                if (effect != null && effect.m_enabled && effect.m_prefab != null && effect.m_prefab.GetComponent<ZNetView>() == null)
                {
                    found.Add(effect.m_prefab);
                }
            }
            if (found.Count == 0)
            {
                return false;
            }

            prefabs = found.ToArray();
            Plugin.Log.LogInfo($"Oar splash: the {projectilePrefab.name} water hit effect ({string.Join(", ", found.ConvertAll(p => p.name))}).");
            return true;
        }
    }
}
