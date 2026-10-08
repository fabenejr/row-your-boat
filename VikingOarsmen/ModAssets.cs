using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// The mod's own assets, from the AssetBundle embedded in this DLL (built in Unity, see
    /// VikingOarsmen-Documents/Pipeline-Asset-e-Animacao.md): the oar models and icons and the rowing
    /// animations.
    /// Loaded once, on first use, and kept loaded for the whole session.
    /// </summary>
    internal static class ModAssets
    {
        private const string BundleName = "vikingoarsmen";
        private const string RowIdleName = "row_idle";
        private const string RowStrokeName = "row_stroke";
        private const string MirrorSuffix = "_mirror";

        private static bool s_loaded;
        private static AssetBundle s_bundle;
        private static AnimationClip s_rowIdle;
        private static AnimationClip s_rowStroke;
        private static AnimationClip s_rowIdleMirror;
        private static AnimationClip s_rowStrokeMirror;

        /// <summary>
        /// An oar model's prefab (see OarItem), or null if the bundle doesn't have it.
        /// </summary>
        internal static GameObject LoadPrefab(string name)
        {
            Load();
            return s_bundle != null ? s_bundle.LoadAsset<GameObject>(name) : null;
        }

        /// <summary>
        /// An oar's inventory icon (see OarItem), or null if the bundle doesn't have it.
        /// </summary>
        internal static Sprite LoadSprite(string name)
        {
            Load();
            return s_bundle != null ? s_bundle.LoadAsset<Sprite>(name) : null;
        }

        /// <summary>
        /// Seated, holding the oar still (neutral gear). Loops. The authored clip rows over the rower's left
        /// (the starboard side, facing the stern); the mirrored one over their right, for the port side.
        /// </summary>
        internal static AnimationClip RowIdle(bool mirrored)
        {
            Load();
            return mirrored ? s_rowIdleMirror : s_rowIdle;
        }

        /// <summary>
        /// One full stroke, catch to catch. Loops. Same sides as <see cref="RowIdle"/>.
        /// </summary>
        internal static AnimationClip RowStroke(bool mirrored)
        {
            Load();
            return mirrored ? s_rowStrokeMirror : s_rowStroke;
        }

        private static void Load()
        {
            if (s_loaded)
            {
                return;
            }
            s_loaded = true;

            // Not Jötunn's LoadAssetBundleFromResources: it disposes the resource stream right after
            // LoadFromStream, and Unity 6 still reads from it on LoadAsset (fails, and the game hangs
            // on the loading screen). LoadFromMemory owns its own copy of the bytes.
            Assembly assembly = typeof(ModAssets).Assembly;
            string resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(BundleName));
            if (resource == null)
            {
                Plugin.Log.LogError($"Embedded AssetBundle '{BundleName}' not found in the DLL.");
                return;
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
                Plugin.Log.LogError($"Failed to load the AssetBundle '{BundleName}' (built for another platform or Unity version?).");
                return;
            }

            s_bundle = bundle;
            s_rowIdle = bundle.LoadAsset<AnimationClip>(RowIdleName);
            s_rowStroke = bundle.LoadAsset<AnimationClip>(RowStrokeName);
            s_rowIdleMirror = bundle.LoadAsset<AnimationClip>(RowIdleName + MirrorSuffix);
            s_rowStrokeMirror = bundle.LoadAsset<AnimationClip>(RowStrokeName + MirrorSuffix);
            if (s_rowIdle == null || s_rowStroke == null || s_rowIdleMirror == null || s_rowStrokeMirror == null)
            {
                Plugin.Log.LogWarning("Rowing animations missing from the AssetBundle; rowers will just sit.");
            }
        }
    }
}
