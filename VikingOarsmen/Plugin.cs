using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Mod entry point, instantiated by BepInEx when the game starts.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        // Plugin identity used by BepInEx and Harmony.
        public const string PluginGuid = "com.autor.vikingoarsmen";
        public const string PluginName = "Viking Oarsmen";
        public const string PluginVersion = "1.1.0";

        // Shared logger so other classes can write to the BepInEx log.
        internal static ManualLogSource Log;

        // User settings, stored in BepInEx/config/com.autor.vikingoarsmen.cfg.
        internal static ConfigEntry<KeyCode> RowKey;
        internal static ConfigEntry<bool> HoldToRow;
        internal static ConfigEntry<float> RowingPower;
        internal static ConfigEntry<float> RampUpTime;
        internal static ConfigEntry<float> StaminaDrainAmount;
        internal static ConfigEntry<float> StaminaDrainInterval;
        internal static ConfigEntry<float> StrokePeriod;
        internal static ConfigEntry<bool> ShowMessage;

        // Harmony instance that owns all patches applied by this mod.
        private Harmony _harmony;

        private void Awake()
        {
            // Expose the plugin logger to the rest of the mod.
            Log = Logger;

            // Bind config entries (created with defaults on first launch).
            RowKey = Config.Bind("Controls", "RowKey", KeyCode.R,
                "Key used to start/stop rowing. Note: R also toggles weapon visibility in vanilla Valheim.");
            HoldToRow = Config.Bind("Controls", "HoldToRow", false,
                "false = press once to start rowing and again to stop. true = row only while the key is held.");
            RowingPower = Config.Bind("Physics", "RowingPower", 1.0f,
                "1.0 = same thrust as a full sail with a strong tailwind. Lower it for slower rowing.");
            RampUpTime = Config.Bind("Physics", "RampUpTime", 1.5f,
                "Seconds for the rowing thrust to reach full power (and to fade out after stopping).");
            StaminaDrainAmount = Config.Bind("Gameplay", "StaminaDrainAmount", 1.0f,
                "Stamina consumed on every drain tick while rowing. Set to 0 to disable.");
            StaminaDrainInterval = Config.Bind("Gameplay", "StaminaDrainInterval", 10.0f,
                "Seconds between stamina drain ticks while rowing.");
            StrokePeriod = Config.Bind("Visual", "StrokePeriod", 2.0f,
                "Duration (seconds) of one full oar stroke animation.");
            ShowMessage = Config.Bind("UI", "ShowMessage", true,
                "Show \"Remando!\" in the center of the screen when rowing starts.");

            // Apply every [HarmonyPatch] found in this assembly.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            // Confirm successful initialization in BepInEx/LogOutput.log.
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded. Press {RowKey.Value} while aboard a ship to row.");
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
