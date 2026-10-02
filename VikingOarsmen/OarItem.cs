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
        private const float AttackDurationMultiplier = 1.75f;

        // Animator speed while swinging the oar (see OarSwing).
        internal const float SwingSpeed = 1f / AttackDurationMultiplier;

        // Embedded resource built in Unity (see art/oar and VikingOarsmen-Documents/Pipeline-Asset-e-Animacao.md).
        private const string BundleName = "vikingoarsmen";

        // Vanilla two-handed weapon whose hold pose and swing animations the oar borrows.
        private const string SwingSource = "Battleaxe";

        // Slides the oar along its shaft in the hand, so the grip falls nearer the middle of the oar
        // (tuned live with UnityExplorer).
        private const float HandOffsetZ = -0.4f;

        // Slides the oar along its shaft when sheathed on the back, so the blade doesn't go through the
        // ground (tuned live with UnityExplorer).
        private const float BackOffsetZ = -0.8708f;

        internal static bool IsOar(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName;
        }

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
            MakeTwoHanded(shared);

            Attack attack = shared.m_attack;
            Plugin.Log.LogInfo($"Oar: Club baseline was range {attack.m_attackRange:F2}.");
            attack.m_attackStamina = AttackStamina;
            attack.m_attackAdrenaline = AttackAdrenaline;
            attack.m_staggerMultiplier = StaggerMultiplier;
            attack.m_attackRange *= RangeMultiplier;

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

            Plugin.Log.LogInfo($"Oar item registered: range {attack.m_attackRange:F2}, swing speed x{SwingSpeed:F2}, " +
                $"durability {shared.m_maxDurability:F0}, knockback {shared.m_attackForce:F0}.");
        }

        /// <summary>
        /// Two-handed weapon with the Battleaxe's hold pose and swings (wide horizontal sweeps). Only the
        /// fields tied to the animation and the hit shape are borrowed; damage, stamina, range and skill
        /// (Clubs) stay the Club's. Two-handed items still go in the right hand, so the rowing check via
        /// GetCurrentWeapon is unaffected.
        /// </summary>
        private static void MakeTwoHanded(ItemDrop.ItemData.SharedData shared)
        {
            GameObject battleaxe = PrefabManager.Instance.GetPrefab(SwingSource);
            ItemDrop source = battleaxe != null ? battleaxe.GetComponent<ItemDrop>() : null;
            if (source == null)
            {
                Plugin.Log.LogWarning($"Oar item: '{SwingSource}' not found; keeping the Club's one-handed swings.");
                return;
            }

            ItemDrop.ItemData.SharedData sourceShared = source.m_itemData.m_shared;
            shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
            shared.m_animationState = sourceShared.m_animationState;
            CopySwing(sourceShared.m_attack, shared.m_attack);
            CopySwing(sourceShared.m_secondaryAttack, shared.m_secondaryAttack);
        }

        private static void CopySwing(Attack source, Attack target)
        {
            target.m_attackType = source.m_attackType;
            target.m_attackAnimation = source.m_attackAnimation;
            target.m_attackRandomAnimations = source.m_attackRandomAnimations;
            target.m_attackChainLevels = source.m_attackChainLevels;
            target.m_attackOriginJoint = source.m_attackOriginJoint;
            target.m_attackAngle = source.m_attackAngle;
            target.m_attackRayWidth = source.m_attackRayWidth;
            target.m_attackHeight = source.m_attackHeight;
            target.m_attackOffset = source.m_attackOffset;
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
            // Our mesh's origin is the top of the shaft; the hand holds it HandOffsetZ further down. Every
            // point under attach shifts with it so they stay on the same spots of the oar.
            Vector3 handOffset = new Vector3(0f, 0f, HandOffsetZ);
            model.localPosition = handOffset;
            model.localRotation = Quaternion.identity;
            AddBackAttach(itemPrefab.transform, attach, model);

            BoxCollider collider = attach.GetComponentInChildren<BoxCollider>(true);
            if (collider != null)
            {
                collider.transform.localPosition = handOffset;
                collider.center = mesh.bounds.center;
                collider.size = mesh.bounds.size;
            }

            // Reference points for the rowing code (grip_top, grip_bottom, fulcrum, blade_tip).
            foreach (Transform point in sourceAttach)
            {
                Transform copy = new GameObject(point.name).transform;
                copy.SetParent(attach, false);
                copy.localPosition = point.localPosition + handOffset;
                copy.localRotation = point.localRotation;
            }

            // Swing trail along the outer part of the oar, the part that actually hits.
            Transform trail = attach.Find("equiped/trail");
            Transform trailBase = trail != null ? trail.Find("base") : null;
            Transform trailTip = trail != null ? trail.Find("tip") : null;
            if (trailBase != null && trailTip != null)
            {
                trailBase.localPosition = sourceAttach.Find("fulcrum").localPosition + handOffset;
                trailTip.localPosition = sourceAttach.Find("blade_tip").localPosition + handOffset;
            }

            Plugin.Log.LogInfo($"Oar item: model '{mesh.name}' ({mesh.bounds.size.z:F2} m) from the bundle, shader '{material.shader.name}'.");
        }

        /// <summary>
        /// Sheathed on the back, VisEquipment uses an "attach_back" child if the prefab has one (else the
        /// hand's "attach") and zeroes its local position on the back joint, so the offset that keeps the
        /// long oar off the ground has to live on the model inside it.
        /// </summary>
        private static void AddBackAttach(Transform itemRoot, Transform attach, Transform model)
        {
            GameObject attachBack = new GameObject("attach_back");
            // Inactive like any attach in the prefab: VisEquipment activates its own instance, and an
            // active one here would render a second oar on the dropped item.
            attachBack.SetActive(false);
            attachBack.transform.SetParent(itemRoot, false);
            attachBack.transform.localPosition = attach.localPosition;
            attachBack.transform.localRotation = attach.localRotation;
            // VisEquipment takes the first child named attach_back or attach, so ours must come first.
            attachBack.transform.SetSiblingIndex(attach.GetSiblingIndex());

            Transform backModel = Object.Instantiate(model, attachBack.transform, false);
            backModel.name = model.name;
            backModel.localPosition = new Vector3(0f, 0f, BackOffsetZ);
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
