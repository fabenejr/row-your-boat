using HarmonyLib;

namespace VikingOarsmen
{
    /// <summary>
    /// Harmony patches that attach the mod's components to vanilla objects when they spawn.
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
    }
}
