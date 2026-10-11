using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;

namespace VikingOarsmen
{
    /// <summary>
    /// The mod's sounds, embedded in this DLL as 16-bit mono WAV (converted by art/sound/convert_sounds.py),
    /// and the game's mixer group they play through.
    /// </summary>
    /// <remarks>
    /// Kept out of the AssetBundle so a sound can be swapped without a Unity build. Each clip is decoded
    /// once, on first use, and kept for the whole session.
    /// </remarks>
    internal static class ModSounds
    {
        private const string ResourcePrefix = "VikingOarsmen.Sounds.";

        private static AudioClip[] s_single;
        private static AudioClip s_multiple;
        private static bool s_multipleLoaded;
        private static AudioMixerGroup s_sfxGroup;
        private static bool s_sfxGroupResolved;

        /// <summary>
        /// The strokes of a lone rower, picked at random (may be empty if missing from the DLL).
        /// </summary>
        internal static AudioClip[] Single
        {
            get
            {
                if (s_single == null)
                {
                    s_single = new[] { Load("single"), Load("single2") }.Where(clip => clip != null).ToArray();
                }
                return s_single;
            }
        }

        /// <summary>
        /// The stroke of a crew of two or more, or null if missing from the DLL.
        /// </summary>
        internal static AudioClip Multiple
        {
            get
            {
                if (!s_multipleLoaded)
                {
                    s_multipleLoaded = true;
                    s_multiple = Load("multiple");
                }
                return s_multiple;
            }
        }

        /// <summary>
        /// The mixer group the game's own sound effects play through, so the options' effects volume
        /// applies to ours too. Null until the game's prefabs are loaded, or if none could be found.
        /// </summary>
        internal static AudioMixerGroup SfxGroup
        {
            get
            {
                if (!s_sfxGroupResolved)
                {
                    ResolveSfxGroup();
                }
                return s_sfxGroup;
            }
        }

        private static void ResolveSfxGroup()
        {
            // Try again next time if the prefabs aren't loaded yet.
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || AudioMan.instance == null)
            {
                return;
            }
            s_sfxGroupResolved = true;

            // AudioMan keeps no field for the effects group: it's set on each vanilla sfx prefab instead.
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null || !prefab.name.StartsWith("sfx_"))
                {
                    continue;
                }
                AudioSource source = prefab.GetComponentInChildren<AudioSource>(true);
                if (source != null && source.outputAudioMixerGroup != null)
                {
                    s_sfxGroup = source.outputAudioMixerGroup;
                    Plugin.Log.LogInfo($"Rowing sound plays through mixer group '{s_sfxGroup.name}' (from {prefab.name}).");
                    return;
                }
            }

            AudioMixer mixer = AudioMan.instance.m_masterMixer;
            AudioMixerGroup[] groups = mixer != null ? mixer.FindMatchingGroups("SFX") : null;
            if (groups != null && groups.Length > 0)
            {
                s_sfxGroup = groups[0];
                Plugin.Log.LogWarning($"No vanilla sound effect to copy the mixer group from; using '{s_sfxGroup.name}'.");
                return;
            }

            Plugin.Log.LogWarning("Game's sound effects mixer group not found; the rowing sound ignores the effects volume.");
        }

        private static AudioClip Load(string name)
        {
            Assembly assembly = typeof(ModSounds).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(ResourcePrefix + name + ".wav"))
            {
                if (stream == null)
                {
                    Plugin.Log.LogError($"Embedded sound '{name}.wav' not found in the DLL.");
                    return null;
                }
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    return Decode(name, reader);
                }
            }
        }

        /// <summary>
        /// Reads a 16-bit mono PCM WAV into a clip, or returns null (with a warning) for any other format.
        /// </summary>
        private static AudioClip Decode(string name, BinaryReader reader)
        {
            if (ReadId(reader) != "RIFF")
            {
                return Reject(name, "not a RIFF file");
            }
            reader.ReadInt32();
            if (ReadId(reader) != "WAVE")
            {
                return Reject(name, "not a WAVE file");
            }

            int rate = 0;
            bool formatOk = false;
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
            {
                string chunk = ReadId(reader);
                int size = reader.ReadInt32();
                long next = reader.BaseStream.Position + size + (size & 1);
                if (chunk == "fmt ")
                {
                    short format = reader.ReadInt16();
                    short channels = reader.ReadInt16();
                    rate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    short bits = reader.ReadInt16();
                    formatOk = format == 1 && channels == 1 && bits == 16;
                    if (!formatOk)
                    {
                        return Reject(name, $"format {format}, {channels} channels, {bits} bit; expected 16-bit mono PCM");
                    }
                }
                else if (chunk == "data")
                {
                    if (!formatOk)
                    {
                        return Reject(name, "data before fmt chunk");
                    }
                    int count = size / 2;
                    float[] samples = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        samples[i] = reader.ReadInt16() / 32768f;
                    }
                    // Filled through the reader callback rather than SetData: the compiler can't resolve
                    // SetData against net462, because one of its overloads takes a netstandard 2.1 Span.
                    int position = 0;
                    return AudioClip.Create("VikingOarsmen_" + name, count, 1, rate, false,
                        data =>
                        {
                            int copied = Mathf.Clamp(count - position, 0, data.Length);
                            System.Array.Copy(samples, position, data, 0, copied);
                            System.Array.Clear(data, copied, data.Length - copied);
                            position += copied;
                        },
                        newPosition => position = newPosition);
                }
                reader.BaseStream.Position = next;
            }
            return Reject(name, "no data chunk");
        }

        // Chunk ids as raw ASCII bytes; ReadChars would decode them as UTF-8.
        private static string ReadId(BinaryReader reader)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(4));
        }

        private static AudioClip Reject(string name, string reason)
        {
            Plugin.Log.LogWarning($"Embedded sound '{name}.wav' can't be read ({reason}); it won't play.");
            return null;
        }
    }
}
