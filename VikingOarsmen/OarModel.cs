using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// A rower's oar: a copy of a ship's steering oar, re-framed so the stroke can move it freely.
    /// </summary>
    /// <remarks>
    /// Hierarchy: Root (at the fulcrum, where the oar rests on the gunwale, with the ship's axes and
    /// mirrored for the port side) → Stroke (swept, tilted, feathered and lifted by the stroke; the shaft
    /// runs along its Y axis, blade down) → Size (OarScale, and turned a quarter around the shaft so the
    /// blade faces fore/aft and the tiller points aft) → the oar mesh, framed like the steering oar at rest
    /// on the starboard side (blade facing sideways, tiller pointing inboard). Stroke is unscaled, so
    /// heights along its Y axis are in meters.
    /// </remarks>
    internal class OarRig
    {
        internal GameObject Root;
        internal Transform Stroke;
        internal Transform Size;

        // Shaft length below the fulcrum (to the blade tip) and above it (to the top), at scale 1 (meters).
        internal float BladeLength;
        internal float HandleLength;

        // Side of the ship the steering oar hangs on (+1 = starboard); its tiller points to the other side.
        internal float SourceSide = 1f;

        /// <summary>
        /// World position of the point on the shaft this far (meters) above the fulcrum.
        /// </summary>
        internal Vector3 ShaftPoint(float height)
        {
            return Stroke.TransformPoint(0f, height, 0f);
        }
    }

    /// <summary>
    /// Builds the oar. Valheim has no rowing oar asset, so the steering oar (Ship.m_rudderObject) is
    /// reused: the rowed ship's own when it has one, else the Karve's. If none can be found, a simple
    /// procedural oar is built from primitives instead.
    /// </summary>
    internal static class OarModel
    {
        // Ship prefabs searched for a steering oar when the rowed ship has none, in order of preference.
        private static readonly string[] s_sourceShips = { "Karve", "VikingShip" };

        // Procedural oar: shaft length below and above the fulcrum (meters).
        private const float ProceduralBlade = 1.4f;
        private const float ProceduralHandle = 0.7f;

        // Fallback material for the procedural oar, resolved once.
        private static Material s_woodMaterial;

        internal static OarRig Create(Transform parent, Ship rowedShip)
        {
            OarRig rig = new OarRig { Root = new GameObject("VikingOarsmen_Oar") };
            rig.Root.transform.SetParent(parent, false);

            rig.Stroke = new GameObject("Stroke").transform;
            rig.Stroke.SetParent(rig.Root.transform, false);
            rig.Size = new GameObject("Size").transform;
            rig.Size.SetParent(rig.Stroke, false);

            // Square the blade to the stroke. This also swings the tiller from the rower's chest to pointing aft.
            rig.Size.localRotation = Quaternion.Euler(0f, -90f, 0f);

            if (TryCopyRudder(rig, rowedShip, rowedShip.name))
            {
                return rig;
            }

            foreach (string shipName in s_sourceShips)
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(shipName) : null;
                Ship ship = prefab != null ? prefab.GetComponent<Ship>() : null;
                if (ship != null && TryCopyRudder(rig, ship, shipName))
                {
                    return rig;
                }
            }

            Plugin.Log.LogWarning("No steering oar found in vanilla ships; using procedural oar.");
            BuildProcedural(rig);
            return rig;
        }

        /// <summary>
        /// Copies a ship's steering oar mesh under the rig, turned upright around the rudder's pivot.
        /// </summary>
        private static bool TryCopyRudder(OarRig rig, Ship ship, string label)
        {
            if (ship.m_rudderObject == null)
            {
                return false;
            }

            Transform rudder = ship.m_rudderObject.transform;
            MeshFilter source = FindLongestMesh(rudder);
            if (source == null || rudder.parent == null)
            {
                return false;
            }

            // Ship.UpdateRudder overwrites the rudder's local rotation with a pure turn around Y,
            // so at rest the rudder is oriented like its parent.
            Matrix4x4 restPose = ship.transform.worldToLocalMatrix * rudder.parent.localToWorldMatrix
                * Matrix4x4.Translate(rudder.localPosition);
            Matrix4x4 meshToShip = restPose * Matrix4x4.Scale(rudder.localScale)
                * rudder.worldToLocalMatrix * source.transform.localToWorldMatrix;
            Vector3 pivot = restPose.GetColumn(3);

            // The shaft runs along the mesh's longest axis; the end hanging lower on the ship is the blade.
            Bounds bounds = source.sharedMesh.bounds;
            int longAxis = MaxAxis(bounds.extents);
            Vector3 half = Axis(longAxis) * bounds.extents[longAxis];
            Vector3 endA = meshToShip.MultiplyPoint3x4(bounds.center + half);
            Vector3 endB = meshToShip.MultiplyPoint3x4(bounds.center - half);
            Vector3 blade = endA.y < endB.y ? endA : endB;
            Vector3 top = endA.y < endB.y ? endB : endA;
            Vector3 down = (blade - top).normalized;

            // The rudder turns around its shaft, so the shaft passes through the pivot (the bounds are
            // off-center because of the blade and the tiller). Measure the shaft from there.
            rig.BladeLength = Vector3.Dot(blade - pivot, down);
            rig.HandleLength = Vector3.Dot(pivot - top, down);
            rig.SourceSide = pivot.x >= 0f ? 1f : -1f;

            // Oar frame: fulcrum at the pivot, shaft pointing straight down, blade still facing sideways.
            Matrix4x4 meshToOar = Matrix4x4.Rotate(Quaternion.FromToRotation(down, Vector3.down))
                * Matrix4x4.Translate(-pivot) * meshToShip;

            // Copy only the mesh and its materials.
            GameObject meshObject = new GameObject("OarMesh");
            meshObject.transform.SetParent(rig.Size, false);
            meshObject.transform.localPosition = meshToOar.GetColumn(3);
            meshObject.transform.localRotation = meshToOar.rotation;
            meshObject.transform.localScale = meshToOar.lossyScale;
            meshObject.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            meshObject.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;

            Plugin.Log.LogInfo($"Oar: '{source.sharedMesh.name}' from the {label} steering oar. " +
                $"Pivot {pivot:F2}, blade {blade:F2}, top {top:F2}; shaft {rig.BladeLength:F2} m below the pivot, {rig.HandleLength:F2} m above.");
            return true;
        }

        /// <summary>
        /// The rudder (or any prefab hierarchy) may contain several meshes (hinges, LODs); the longest
        /// one is the oar itself. Also reused by OarItem to find the mesh to replace on the item prefab.
        /// </summary>
        internal static MeshFilter FindLongestMesh(Transform rudder)
        {
            MeshFilter best = null;
            float bestLength = 0f;
            foreach (MeshFilter filter in rudder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
                {
                    continue;
                }

                Vector3 size = filter.sharedMesh.bounds.size;
                float length = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (length > bestLength)
                {
                    best = filter;
                    bestLength = length;
                }
            }
            return best;
        }

        /// <summary>
        /// Fallback: a Viking-style oar (long round shaft, knob grip, narrow blade) made of primitives,
        /// built straight down from the grip to the blade.
        /// </summary>
        private static void BuildProcedural(OarRig rig)
        {
            rig.BladeLength = ProceduralBlade;
            rig.HandleLength = ProceduralHandle;

            Transform root = rig.Size;
            Material wood = GetWoodMaterial();
            Quaternion acrossX = Quaternion.Euler(0f, 0f, 90f); // Cylinder axis Y -> X

            float topY = ProceduralHandle;
            float tipY = -ProceduralBlade;

            // Shaft from the grip to the start of the blade.
            float shaftEnd = tipY + 0.8f;
            AddPart(root, PrimitiveType.Cylinder, "Shaft", new Vector3(0f, (topY + shaftEnd) * 0.5f, 0f), Quaternion.identity,
                new Vector3(0.07f, (topY - shaftEnd) * 0.5f, 0.07f), wood);

            // Rounded knob at the grip.
            AddPart(root, PrimitiveType.Sphere, "Grip", new Vector3(0f, topY, 0f), Quaternion.identity,
                new Vector3(0.09f, 0.09f, 0.09f), wood);

            // Tapered neck between shaft and blade.
            AddPart(root, PrimitiveType.Cube, "Neck", new Vector3(0f, shaftEnd - 0.1f, 0f), Quaternion.identity,
                new Vector3(0.05f, 0.25f, 0.1f), wood);

            // Blade, thin along X like the steering oar's.
            AddPart(root, PrimitiveType.Cube, "Blade", new Vector3(0f, tipY + 0.35f, 0f), Quaternion.identity,
                new Vector3(0.03f, 0.7f, 0.17f), wood);

            // Disc that rounds off the blade tip.
            AddPart(root, PrimitiveType.Cylinder, "BladeTip", new Vector3(0f, tipY, 0f), acrossX,
                new Vector3(0.17f, 0.015f, 0.17f), wood);
        }

        private static void AddPart(Transform root, PrimitiveType type, string name, Vector3 position,
            Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;

            // Visual only: the collider would push the ship and the player around.
            Object.DestroyImmediate(part.GetComponent<Collider>());

            part.transform.SetParent(root, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Borrows a wood material from a vanilla building piece so the oar matches the game's art.
        /// </summary>
        private static Material GetWoodMaterial()
        {
            if (s_woodMaterial != null)
            {
                return s_woodMaterial;
            }

            foreach (string pieceName in new[] { "wood_pole", "wood_pole2", "wood_beam" })
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(pieceName) : null;
                MeshRenderer renderer = prefab != null ? prefab.GetComponentInChildren<MeshRenderer>(true) : null;
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    s_woodMaterial = renderer.sharedMaterial;
                    return s_woodMaterial;
                }
            }

            // Last resort: plain brown material.
            s_woodMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.3f, 0.18f) };
            return s_woodMaterial;
        }

        private static int MaxAxis(Vector3 v)
        {
            return v.x >= v.y && v.x >= v.z ? 0 : (v.y >= v.z ? 1 : 2);
        }

        private static Vector3 Axis(int index)
        {
            return index == 0 ? Vector3.right : (index == 1 ? Vector3.up : Vector3.forward);
        }
    }
}
