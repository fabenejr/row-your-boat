using HarmonyLib;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Harmony patches that attach the mod's components to vanilla objects when they spawn, and the one
    /// behavior patch rowing needs (see Player_SetControls_Prefix).
    /// </summary>
    [HarmonyPatch]
    internal static class ShipRowingPatch
    {
        /// <summary>
        /// Adds rowing propulsion to every ship.
        /// </summary>
        [HarmonyPatch(typeof(Ship), "Awake")]
        [HarmonyPostfix]
        private static void Ship_Awake_Postfix(Ship __instance)
        {
            // Build-preview ghosts have no valid ZDO and never float.
            ZNetView nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return;
            }

            if (__instance.GetComponent<ShipRowing>() == null)
            {
                __instance.gameObject.AddComponent<ShipRowing>();
            }
        }

        /// <summary>
        /// Adds the oar visual to every player (local and remote) so everyone sees each other row.
        /// </summary>
        [HarmonyPatch(typeof(Player), "Awake")]
        [HarmonyPostfix]
        private static void Player_Awake_Postfix(Player __instance)
        {
            if (__instance.GetComponent<OarVisual>() == null)
            {
                __instance.gameObject.AddComponent<OarVisual>();
            }
        }

        /// <summary>
        /// Adds the rower's gear indicator to the HUD.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        private static void Hud_Awake_Postfix(Hud __instance)
        {
            if (__instance.GetComponent<GearHud>() == null)
            {
                __instance.gameObject.AddComponent<GearHud>();
            }
        }

        /// <summary>
        /// Slows the oar's swing animation (see OarSwing).
        /// </summary>
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        [HarmonyPostfix]
        private static void CharacterAnimEvent_CustomFixedUpdate_Postfix(CharacterAnimEvent __instance)
        {
            OarSwing.OnFixedUpdate(__instance);
        }

        /// <summary>
        /// Keeps the oar's swing slowed when an animation event changes the speed mid-attack (see OarSwing).
        /// </summary>
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
        [HarmonyPostfix]
        private static void CharacterAnimEvent_Speed_Postfix(CharacterAnimEvent __instance)
        {
            OarSwing.OnSpeedSet(__instance);
        }

        /// <summary>
        /// Stops movement input from standing the rower up while rowing. Vanilla auto-detaches any
        /// seated player on movement input unless they're a doodad controller (like the helm) — rowers
        /// aren't one (see RowingController.IsRowingMode), so without this W/S would stand them up
        /// instead of shifting gear. Jump still exits normally; RowingController.Update() also handles
        /// the interact (E) key explicitly, same two ways out as the helm.
        /// </summary>
        [HarmonyPatch(typeof(Player), "SetControls")]
        [HarmonyPrefix]
        private static void Player_SetControls_Prefix(Player __instance, ref Vector3 movedir)
        {
            if (__instance == Player.m_localPlayer && RowingController.IsRowingMode(__instance))
            {
                movedir = Vector3.zero;
            }
        }
    }
}
