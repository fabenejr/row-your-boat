using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;

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
        public const string PluginVersion = "2.0.0";

        // Shared logger so other classes can write to the BepInEx log.
        internal static ManualLogSource Log;

        // User settings, stored in BepInEx/config/com.fabenejr.vikingoarsmen.cfg.
        internal static ConfigEntry<float> PowerPerRower;
        internal static ConfigEntry<float> Gear2Multiplier;
        internal static ConfigEntry<float> Gear3Multiplier;
        internal static ConfigEntry<float> ReverseMultiplier;
        internal static ConfigEntry<float> MaxRowingPower;
        internal static ConfigEntry<float> RampUpTime;
        internal static ConfigEntry<float> StaminaDrainAmount;
        internal static ConfigEntry<float> StaminaDrainIntervalSlow;
        internal static ConfigEntry<float> StaminaDrainIntervalHalf;
        internal static ConfigEntry<float> StaminaDrainIntervalFull;
        internal static ConfigEntry<float> StrokeSpeed;
        internal static ConfigEntry<bool> Splash;

        // Harmony instance that owns all patches applied by this mod.
        private Harmony _harmony;

        private void Awake()
        {
            // Expose the plugin logger to the rest of the mod.
            Log = Logger;

            // Bind config entries (created with defaults on first launch).
            PowerPerRower = Config.Bind("Physics", "PowerPerRower", 0.25f,
                "Share of the maximum thrust a rower in Slow gear (1) adds. 0.25 = four rowers reach full speed.");
            Gear2Multiplier = Config.Bind("Physics", "Gear2Multiplier", 1.5f,
                "Thrust in Half gear (2), as a multiple of PowerPerRower.");
            Gear3Multiplier = Config.Bind("Physics", "Gear3Multiplier", 2.0f,
                "Thrust in Full gear (3), as a multiple of PowerPerRower.");
            ReverseMultiplier = Config.Bind("Physics", "ReverseMultiplier", -1.0f,
                "Thrust in reverse, as a multiple of PowerPerRower. Negative pushes the ship backward.");
            MaxRowingPower = Config.Bind("Physics", "MaxRowingPower", 1.5f,
                "Maximum total rowing thrust, ahead or astern. 1.0 = same as a full sail with a strong tailwind.");
            RampUpTime = Config.Bind("Physics", "RampUpTime", 1.5f,
                "Seconds for the rowing thrust to reach a new target (crew changes, gear shifts, stopping).");
            StaminaDrainAmount = Config.Bind("Gameplay", "StaminaDrainAmount", 6.0f,
                "Stamina spent on every stroke in any gear but neutral. Set to 0 to disable the cost entirely.");
            StaminaDrainIntervalSlow = Config.Bind("Gameplay", "StaminaDrainIntervalSlow", 2.0f,
                "Seconds between strokes in Slow gear (1) and reverse.");
            StaminaDrainIntervalHalf = Config.Bind("Gameplay", "StaminaDrainIntervalHalf", 1.5f,
                "Seconds between strokes in Half gear (2).");
            StaminaDrainIntervalFull = Config.Bind("Gameplay", "StaminaDrainIntervalFull", 1.0f,
                "Seconds between strokes in Full gear (3).");
            StrokeSpeed = Config.Bind("Visual", "StrokeSpeed", 1.0f,
                "Speed of the rowing animation in every gear. 1.0 = the animation's own pace in Half gear (2); Slow and reverse are slower, Full faster.");
            Splash = Config.Bind("Visual", "Splash", true,
                "Splash and play a sound when the oar blade hits the water.");

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
