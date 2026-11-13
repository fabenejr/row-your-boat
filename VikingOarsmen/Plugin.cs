using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Utils;

namespace VikingOarsmen
{
    /// <summary>
    /// Mod entry point, instantiated by BepInEx when the game starts.
    /// </summary>
    /// <remarks>
    /// No [NetworkCompatibility] here: this mod stays usable by only part of the crew and dedicated
    /// servers never need it (see README "Multiplayer"), unlike a typical Jotunn content mod.
    /// </remarks>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        // Plugin identity used by BepInEx and Harmony.
        public const string PluginGuid = "com.fabenejr.vikingoarsmen";
        public const string PluginName = "Viking Oarsmen";
        public const string PluginVersion = "3.4.0";

        // Shared logger so other classes can write to the BepInEx log.
        internal static ManualLogSource Log;

        // User settings, stored in BepInEx/config/com.fabenejr.vikingoarsmen.cfg.
        internal static ConfigEntry<float> PowerPerRower;
        internal static ConfigEntry<float> Gear2Multiplier;
        internal static ConfigEntry<float> Gear3Multiplier;
        internal static ConfigEntry<float> ReverseMultiplier;
        internal static ConfigEntry<float> MaxRowingPower;
        internal static ConfigEntry<float> MaxRowingSpeed;
        internal static ConfigEntry<float> CrewFalloff;
        internal static ConfigEntry<float> SteeringMultiplier;
        internal static ConfigEntry<float> RampUpTime;
        internal static ConfigEntry<float> StaminaDrainAmount;
        internal static ConfigEntry<float> StaminaDrainIntervalSlow;
        internal static ConfigEntry<float> StaminaDrainIntervalHalf;
        internal static ConfigEntry<float> StaminaDrainIntervalFull;
        internal static ConfigEntry<bool> ShowOarHint;
        internal static ConfigEntry<float> StrokeSpeed;
        internal static ConfigEntry<bool> Splash;
        internal static ConfigEntry<bool> ShowCrewHud;
        internal static ConfigEntry<bool> RowingSound;
        internal static ConfigEntry<float> RowingSoundVolume;
        internal static ConfigEntry<bool> DebugRowingSync;
        internal static ConfigEntry<bool> DebugSkipFaceStern;
        internal static ConfigEntry<bool> DebugSkipLean;

        // Entries that decide how the ship rows (not how it looks). Marked admin-only, Jotunn makes a joining client
        // use the server's values, so the ship's owner and every rower play by the same rules.
        private ConfigEntry<T> BindSynced<T>(string section, string key, T defaultValue, string description,
            AcceptableValueBase range = null)
        {
            return Config.Bind(section, key, defaultValue,
                new ConfigDescription(description, range, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }

        // Harmony instance that owns all patches applied by this mod.
        private Harmony _harmony;

        private void Awake()
        {
            // Expose the plugin logger to the rest of the mod.
            Log = Logger;

            // Bind config entries (created with defaults on first launch).
            PowerPerRower = BindSynced("Physics", "PowerPerRower", 0.08f,
                "Share of the maximum thrust a rower in Slow gear (1) adds. 0.25 = four rowers in Slow gear reach full speed.");
            Gear2Multiplier = BindSynced("Physics", "Gear2Multiplier", 1.4f,
                "Thrust in Half gear (2), as a multiple of PowerPerRower.");
            Gear3Multiplier = BindSynced("Physics", "Gear3Multiplier", 1.8f,
                "Thrust in Full gear (3), as a multiple of PowerPerRower.");
            ReverseMultiplier = BindSynced("Physics", "ReverseMultiplier", -1.0f,
                "Thrust in reverse, as a multiple of PowerPerRower. Negative pushes the ship backward.");
            MaxRowingPower = BindSynced("Physics", "MaxRowingPower", 1.5f,
                "Maximum total rowing thrust, ahead or astern. 1.0 = same as a full sail with a strong tailwind.");
            MaxRowingSpeed = BindSynced("Physics", "MaxRowingSpeed", 0f,
                "Speed in m/s (along the bow) above which rowing stops adding thrust, so oars and sail can't stack without limit. " +
                "0 = no limit.");
            CrewFalloff = BindSynced("Physics", "CrewFalloff", 0f,
                "How much each extra rower adds less than the one before, from 0 (every rower adds the same) to 0.5. " +
                "With 0.1 the second rower adds 90%, the third 81%, and so on; the strongest strokes count first.",
                new AcceptableValueRange<float>(0f, 0.5f));
            SteeringMultiplier = BindSynced("Physics", "SteeringMultiplier", 0f,
                "How strongly rowing turns the ship. 0 = off. Above 0 the ship turns away from the side the rowers sit on, " +
                "and the other way when rowing in reverse; rowers on both sides cancel out. Negative values turn it towards that side.");
            RampUpTime = BindSynced("Physics", "RampUpTime", 1.5f,
                "Seconds for the rowing thrust to reach a new target (crew changes, gear shifts, stopping).");
            StaminaDrainAmount = BindSynced("Gameplay", "StaminaDrainAmount", 6.0f,
                "Stamina spent on every stroke in any gear but neutral. Set to 0 to disable the cost entirely.");
            StaminaDrainIntervalSlow = BindSynced("Gameplay", "StaminaDrainIntervalSlow", 2.0f,
                "Seconds between strokes in Slow gear (1) and reverse.");
            StaminaDrainIntervalHalf = BindSynced("Gameplay", "StaminaDrainIntervalHalf", 1.5f,
                "Seconds between strokes in Half gear (2).");
            StaminaDrainIntervalFull = BindSynced("Gameplay", "StaminaDrainIntervalFull", 1.0f,
                "Seconds between strokes in Full gear (3).");
            ShowOarHint = Config.Bind("Gameplay", "ShowOarHint", true,
                "Show the \"Equip the oar to row.\" message (and the Drakkar Oar one) when you sit on a bench without the right oar and press W or S.");
            StrokeSpeed = Config.Bind("Visual", "StrokeSpeed", 1.0f,
                "Speed of the rowing animation in every gear. 1.0 = the animation's own pace in Half gear (2); Slow and reverse are slower, Full faster.");
            Splash = Config.Bind("Visual", "Splash", true,
                "Splash and play a sound when the oar blade hits the water.");
            ShowCrewHud = Config.Bind("Visual", "ShowCrewHud", true,
                "While you row, show a panel with how many aboard are rowing and a top-down drawing of the ship with where each of them sits.");
            RowingSound = Config.Bind("Audio", "RowingSound", true,
                "Play the sound of the oars on every stroke: a lone rower's, or the whole crew's when two or more row together.");
            RowingSoundVolume = Config.Bind("Audio", "RowingSoundVolume", 1.0f,
                new ConfigDescription("Volume of the rowing sound, relative to the game's sound effects volume.",
                    new AcceptableValueRange<float>(0f, 1f)));
            DebugRowingSync = Config.Bind("Debug", "DebugRowingSync", false,
                "Log every rower's stroke phase against the crew's beat to BepInEx/LogOutput.log, to track down rowers whose animation drifts or stalls in multiplayer.");
            DebugSkipFaceStern = Config.Bind("Debug", "DebugSkipFaceStern", false,
                "Do not turn the rower to face the stern. Only to find out what makes the cape or head jitter while rowing; the rower then faces the way the bench does.");
            DebugSkipLean = Config.Bind("Debug", "DebugSkipLean", false,
                "Do not lean the rower over the gunwale. Only to find out what makes the cape or head jitter while rowing.");

            // Apply every [HarmonyPatch] found in this assembly.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            OarItem.Setup();

            // Confirm successful initialization in BepInEx/LogOutput.log.
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded. Equip the oar and sit on a ship bench to row.");
        }

        // Input is polled per frame here so key presses are never missed.
        private void Update()
        {
            RowingController.Update();
        }

        private void OnDestroy()
        {
            // Remove only the patches applied by this mod.
            _harmony?.UnpatchSelf();
        }
    }
}
