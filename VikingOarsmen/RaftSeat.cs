using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Gives the Raft a bench, so one more player can sit and help row: a copy of the VikingShip's stool box
    /// (collider, Chair and mesh as the game ships them), at the front of the raft's left side, ahead of the
    /// mast. Added to every raft as it spawns (see ShipRowingPatch).
    /// </summary>
    /// <remarks>
    /// Not added to the Raft prefab itself: prefabs come from the game's asset bundles, and Unity refuses to
    /// create objects under them ("Cannot instantiate objects with a parent which is persistent"). Only
    /// players with the mod see and use the bench; the raft's network state is untouched.
    /// </remarks>
    internal static class RaftSeat
    {
        private const string RaftPrefab = "Raft";
        private const string StoolSource = "VikingShip";
        private const string StoolPath = "interactive/sit_box (1)";
        private const string SeatName = "VikingOarsmen_sit_box";

        // Raft-local position of the bench: on top of the deck (deck box top at 0.48), its outer edge just inside
        // the left edge of the deck (half-width 1.6), ahead of the mast (z 0.86).
        private static readonly Vector3 s_seatPosition = new Vector3(-1.1f, 0.48f, 1.9f);

        /// <summary>
        /// Adds the bench to a ship just spawned, if it is a raft that doesn't have one yet.
        /// </summary>
        internal static void AddTo(Ship ship)
        {
            if (Utils.GetPrefabName(ship.gameObject) != RaftPrefab)
            {
                return;
            }

            Transform interactive = ship.transform.Find("interactive");
            GameObject source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(StoolSource) : null;
            Transform stool = source != null ? source.transform.Find(StoolPath) : null;
            if (interactive == null || stool == null)
            {
                Plugin.Log.LogWarning("Couldn't add the raft bench: raft interactive or VikingShip stool not found.");
                return;
            }

            if (interactive.Find(SeatName) != null)
            {
                return;
            }

            GameObject seat = Object.Instantiate(stool.gameObject, interactive, false);
            seat.name = SeatName;
            seat.transform.localPosition = s_seatPosition;
            seat.transform.localRotation = Quaternion.identity;
        }
    }
}
