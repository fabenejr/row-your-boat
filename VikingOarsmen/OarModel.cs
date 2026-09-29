using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Builds the oar GameObject. Valheim has no rowing oar asset, so the Karve's steering oar
    /// (Ship.m_rudderObject) is reused as the model. If it can't be found, a simple procedural
    /// oar is built from primitives instead.
    /// </summary>
    /// <remarks>
    /// Oar convention used by <see cref="OarVisual"/>: the root sits at the oarlock pivot, the shaft
    /// runs along local +Z (handle inboard at -Z, blade outboard at +Z) and the blade's thin axis is X.
    /// </remarks>
    internal static class OarModel
    {
        // Overall oar length in meters and how much of it stays inboard of the pivot.
        internal const float Length = 4.0f;
        internal const float InboardLength = 1.0f;
        internal const float OutboardLength = Length - InboardLength;

        // Ship prefabs searched for a steering oar to reuse, in order of preference.
        private static readonly string[] s_sourceShips = { "Karve", "VikingShip" };

        // Fallback material for the procedural oar, resolved once.
        private static Material s_woodMaterial;

        internal static GameObject Create(Transform parent)
        {
            // Root object positioned at the oarlock pivot.
            GameObject root = new GameObject("VikingOarsmen_Oar");
            root.transform.SetParent(parent, false);

            if (!TryBuildFromGameRudder(root.transform))
            {
                BuildProcedural(root.transform);
            }

            return root;
        }

        /// <summary>
        /// Clones the steering oar mesh from a vanilla ship prefab and aligns it to the oar convention.
        /// </summary>
        private static bool TryBuildFromGameRudder(Transform root)
        {
            if (ZNetScene.instance == null)
            {
                return false;
            }

            foreach (string shipName in s_sourceShips)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(shipName);
                Ship ship = prefab != null ? prefab.GetComponent<Ship>() : null;
                if (ship == null || ship.m_rudderObject == null)
                {
                    continue;
                }

                // The rudder may contain several meshes (hinges, LODs); the longest one is the oar itself.
                MeshFilter best = null;
                float bestLength = 0f;
                foreach (MeshFilter filter in ship.m_rudderObject.GetComponentsInChildren<MeshFilter>(true))
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

                if (best == null)
                {
                    continue;
                }

                AttachAlignedMesh(root, best, prefab.transform);
                Plugin.Log.LogInfo($"Using '{best.sharedMesh.name}' from {shipName} as the oar model.");
                return true;
            }

            Plugin.Log.LogWarning("No steering oar found in vanilla ships; using procedural oar.");
            return false;
        }

        /// <summary>
        /// Copies a mesh under the oar root, rotated and scaled so its long axis maps to +Z (blade end
        /// outboard) and its thinnest axis maps to X.
        /// </summary>
        private static void AttachAlignedMesh(Transform root, MeshFilter source, Transform prefabRoot)
        {
            Bounds bounds = source.sharedMesh.bounds;
            Vector3 extents = bounds.extents;

            // Long axis = largest extent, thin axis = smallest, mid axis = the remaining one.
            int longAxis = MaxAxis(extents);
            int thinAxis = MinAxis(extents);
            int midAxis = 3 - longAxis - thinAxis;

            Vector3 longDir = Axis(longAxis);
            Vector3 endA = bounds.center + longDir * extents[longAxis];
            Vector3 endB = bounds.center - longDir * extents[longAxis];

            // On the ship the steering oar hangs with its blade in the water, so the end that sits
            // lower in ship space is the blade.
            Matrix4x4 meshToShip = prefabRoot.worldToLocalMatrix * source.transform.localToWorldMatrix;
            bool aIsBlade = meshToShip.MultiplyPoint3x4(endA).y < meshToShip.MultiplyPoint3x4(endB).y;
            Vector3 bladeDir = aIsBlade ? longDir : -longDir;
            Vector3 handleEnd = aIsBlade ? endB : endA;

            // Rotation mapping blade direction -> +Z and mid axis -> +Y (so the thin axis ends up on X).
            Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(bladeDir, Axis(midAxis)));
            float scale = Length / (extents[longAxis] * 2f);

            GameObject meshObject = new GameObject("OarMesh");
            meshObject.transform.SetParent(root, false);
            meshObject.transform.localRotation = rotation;
            meshObject.transform.localScale = Vector3.one * scale;

            // Shift so the handle end lands InboardLength behind the pivot.
            meshObject.transform.localPosition = new Vector3(0f, 0f, -InboardLength) - rotation * (handleEnd * scale);

            meshObject.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            meshObject.AddComponent<MeshRenderer>().sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
        }

        /// <summary>
        /// Fallback: a Viking-style oar (long round shaft, knob grip, narrow blade) made of primitives.
        /// </summary>
        private static void BuildProcedural(Transform root)
        {
            Material wood = GetWoodMaterial();
            Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f);   // Cylinder axis Y -> Z
            Quaternion acrossX = Quaternion.Euler(0f, 0f, 90f);  // Cylinder axis Y -> X

            float handleZ = -InboardLength;
            float tipZ = Length - InboardLength;

            // Shaft from the handle to the start of the blade.
            float shaftEnd = tipZ - 1.1f;
            AddPart(root, PrimitiveType.Cylinder, "Shaft", new Vector3(0f, 0f, (handleZ + shaftEnd) * 0.5f), alongZ,
                new Vector3(0.07f, (shaftEnd - handleZ) * 0.5f, 0.07f), wood);

            // Rounded knob at the handle end.
            AddPart(root, PrimitiveType.Sphere, "Grip", new Vector3(0f, 0f, handleZ), Quaternion.identity,
                new Vector3(0.09f, 0.09f, 0.09f), wood);

            // Tapered neck between shaft and blade.
            AddPart(root, PrimitiveType.Cube, "Neck", new Vector3(0f, 0f, shaftEnd + 0.12f), Quaternion.identity,
                new Vector3(0.05f, 0.1f, 0.3f), wood);

            // Long narrow blade, thin along X.
            AddPart(root, PrimitiveType.Cube, "Blade", new Vector3(0f, 0f, tipZ - 0.45f), Quaternion.identity,
                new Vector3(0.03f, 0.17f, 0.9f), wood);

            // Disc that rounds off the blade tip.
            AddPart(root, PrimitiveType.Cylinder, "BladeTip", new Vector3(0f, 0f, tipZ), acrossX,
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

        private static int MinAxis(Vector3 v)
        {
            return v.x <= v.y && v.x <= v.z ? 0 : (v.y <= v.z ? 1 : 2);
        }

        private static Vector3 Axis(int index)
        {
            return index == 0 ? Vector3.right : (index == 1 ? Vector3.up : Vector3.forward);
        }
    }
}
