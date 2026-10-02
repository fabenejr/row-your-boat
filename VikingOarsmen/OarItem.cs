using System.IO;
using System.Linq;
using System.Reflection;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Registers the rower's oar: a weapon cloned from the Club, craftable at a tier-1 workbench, that
    /// will gate rowing once equipped (see VikingOarsmen-Documents/Plano-Marchas-e-Remo.md, items D5/D6
    /// for why stats mostly follow the Club as-is). The visual is our own oar model, loaded from the
    /// AssetBundle embedded in this DLL.
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

        // Embedded resource built in Unity (see art/oar and VikingOarsmen-Documents/Pipeline-Asset-e-Animacao.md).
        private const string BundleName = "vikingoarsmen";

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
        /// Puts our oar model (from the embedded AssetBundle) into the cloned Club, keeping the Club's own
        /// hierarchy (attach/model, attach/collider, attach/equiped/trail, UpgraderGlow) so everything the
        /// game wires to those names keeps working. Only the mesh, material, collider and trail change.
        /// </summary>
        private static void ApplyOarVisual(GameObject itemPrefab)
        {
            Transform source = LoadBundlePrefab();
            Transform sourceAttach = source != null ? source.Find("attach") : null;
            Transform attach = itemPrefab.transform.Find("attach");
            Transform model = attach != null ? attach.Find("model") : null;
            if (sourceAttach == null || model == null)
            {
                Plugin.Log.LogWarning("Oar item: bundle prefab or the Club's attach/model not found; keeping the Club's own mesh.");
                return;
            }

            Mesh mesh = sourceAttach.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer renderer = model.GetComponent<MeshRenderer>();
            // Our material only carries the texture: the bundle's shader is compiled for the build
            // platform's graphics API only, while the Club's shader is the game's own, lit like every
            // other item, and valid on any platform.
            Material material = new Material(sourceAttach.GetComponent<MeshRenderer>().sharedMaterial)
            {
                shader = renderer.sharedMaterial.shader,
            };
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer.sharedMaterials = new[] { material };
            // Our mesh's origin is already the grip, unlike the Club's model, which sits offset from attach.
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;

            BoxCollider collider = attach.GetComponentInChildren<BoxCollider>(true);
            if (collider != null)
            {
                collider.transform.localPosition = Vector3.zero;
                collider.center = mesh.bounds.center;
                collider.size = mesh.bounds.size;
            }

            // Reference points for the rowing code (grip_top, grip_bottom, fulcrum, blade_tip).
            foreach (Transform point in sourceAttach)
            {
                Transform copy = new GameObject(point.name).transform;
                copy.SetParent(attach, false);
                copy.localPosition = point.localPosition;
                copy.localRotation = point.localRotation;
            }

            // Swing trail along the outer part of the oar, the part that actually hits.
            Transform trail = attach.Find("equiped/trail");
            Transform trailBase = trail != null ? trail.Find("base") : null;
            Transform trailTip = trail != null ? trail.Find("tip") : null;
            if (trailBase != null && trailTip != null)
            {
                trailBase.localPosition = sourceAttach.Find("fulcrum").localPosition;
                trailTip.localPosition = sourceAttach.Find("blade_tip").localPosition;
            }

            Plugin.Log.LogInfo($"Oar item: model '{mesh.name}' ({mesh.bounds.size.z:F2} m) from the bundle, shader '{material.shader.name}'.");
        }

        private static Transform LoadBundlePrefab()
        {
            // Not Jötunn's LoadAssetBundleFromResources: it disposes the resource stream right after
            // LoadFromStream, and Unity 6 still reads from it on LoadAsset (fails, and the game hangs
            // on the loading screen). LoadFromMemory owns its own copy of the bytes.
            Assembly assembly = typeof(OarItem).Assembly;
            string resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(BundleName));
            if (resource == null)
            {
                Plugin.Log.LogError($"Oar item: embedded AssetBundle '{BundleName}' not found in the DLL.");
                return null;
            }

            byte[] bytes;
            using (Stream stream = assembly.GetManifestResourceStream(resource))
            using (MemoryStream memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                bytes = memory.ToArray();
            }

            AssetBundle bundle = AssetBundle.LoadFromMemory(bytes);
            if (bundle == null)
            {
                Plugin.Log.LogError($"Oar item: failed to load the AssetBundle '{BundleName}' (built for another platform or Unity version?).");
                return null;
            }

            GameObject prefab = bundle.LoadAsset<GameObject>(PrefabName);
            return prefab != null ? prefab.transform : null;
        }
    }
}
