using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Registers the rower's oar: a weapon cloned from the Club, craftable at a tier-1 workbench, that
    /// will gate rowing once equipped (see VikingOarsmen-Documents/Plano-Marchas-e-Remo.md, items D5/D6
    /// for why stats mostly follow the Club as-is). The visual is the same steering-oar mesh OarModel
    /// already copies for the stroke animation, applied once to this item's prefab instead of per-rower.
    /// </summary>
    internal static class OarItem
    {
        internal const string PrefabName = "VikingOarsmen_Oar";

        // Overrides from the Club baseline (section 1.2 of the plan). Fields not listed here are left
        // exactly as the Club has them (D5: "seguir igual ao Club por padrão" for anything undecided).
        private const float Weight = 4f;
        private const float Durability = 50f;
        private const float Knockback = 50f;
        private const float BackstabBonus = 2f;
        private const float StaggerMultiplier = 1.5f;
        private const float AttackStamina = 12f;
        private const float AttackAdrenaline = 1f;
        private const float RangeMultiplier = 2f;
        private const float AttackDurationMultiplier = 3f;

        // Ship whose steering oar is copied onto the item's mesh (same source OarModel prefers).
        private const string VisualSourceShip = "Karve";

        internal static void Setup()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Create;
        }

        private static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;

            ItemConfig config = new ItemConfig
            {
                Name = "Remo",
                Description = "Remo viking. Precisa estar equipado para remar, sentado no banco de um barco.",
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Weight = Weight,
            };
            config.AddRequirement("FineWood", 6);

            CustomItem oar = new CustomItem(PrefabName, "Club", config);
            if (oar.ItemPrefab == null)
            {
                Plugin.Log.LogError("Failed to create the oar item: couldn't clone 'Club' (not loaded yet?).");
                return;
            }

            ItemDrop.ItemData.SharedData shared = oar.ItemDrop.m_itemData.m_shared;
            shared.m_maxDurability = Durability;
            shared.m_attackForce = Knockback;
            shared.m_backstabBonus = BackstabBonus;

            Attack attack = shared.m_attack;
            Plugin.Log.LogInfo($"Oar: Club baseline was range {attack.m_attackRange:F2}, speedFactor {attack.m_speedFactor:F2}.");
            attack.m_attackStamina = AttackStamina;
            attack.m_attackAdrenaline = AttackAdrenaline;
            attack.m_staggerMultiplier = StaggerMultiplier;
            attack.m_attackRange *= RangeMultiplier;
            // Higher m_speedFactor plays the swing faster in the Club's own data, so dividing it slows
            // the attack down to the requested "3x longer". Flip to a multiply if testing shows otherwise.
            attack.m_speedFactor /= AttackDurationMultiplier;

            ApplyOarVisual(oar.ItemPrefab);

            if (shared.m_icons == null || shared.m_icons.Length == 0)
            {
                Sprite icon = RenderManager.Instance.Render(oar.ItemPrefab);
                if (icon != null)
                {
                    shared.m_icons = new[] { icon };
                }
            }

            if (!ItemManager.Instance.AddItem(oar))
            {
                Plugin.Log.LogError("Failed to register the oar item with Jötunn.");
                return;
            }

            Plugin.Log.LogInfo($"Oar item registered: range {attack.m_attackRange:F2}, speedFactor {attack.m_speedFactor:F2}, " +
                $"durability {shared.m_maxDurability:F0}, knockback {shared.m_attackForce:F0}.");
        }

        /// <summary>
        /// Replaces the Club's mesh with a copy of a ship's steering-oar mesh, once, on the item prefab
        /// itself (not per-rower/per-frame like the live stroke animation in OarModel). Temporary asset,
        /// per the backlog: a dedicated oar model is tracked separately.
        /// </summary>
        private static void ApplyOarVisual(GameObject itemPrefab)
        {
            GameObject shipPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(VisualSourceShip) : null;
            Ship ship = shipPrefab != null ? shipPrefab.GetComponent<Ship>() : null;
            Transform rudder = ship != null && ship.m_rudderObject != null ? ship.m_rudderObject.transform : null;
            MeshFilter source = rudder != null ? OarModel.FindLongestMesh(rudder) : null;
            MeshFilter target = OarModel.FindLongestMesh(itemPrefab.transform);
            if (source == null || target == null)
            {
                Plugin.Log.LogWarning("Oar item: no steering-oar mesh found to reuse; keeping the Club's own mesh.");
                return;
            }

            target.sharedMesh = source.sharedMesh;
            MeshRenderer targetRenderer = target.GetComponent<MeshRenderer>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            if (targetRenderer != null && sourceRenderer != null)
            {
                targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            }
        }
    }
}
