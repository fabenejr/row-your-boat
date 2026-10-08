using System.Collections.Generic;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// How a rower sits on one kind of ship, so the oar reaches the water from that ship's benches: how far
    /// the body slides towards the gunwale, and how far the upper body leans out over it. Both are applied
    /// to the visual model only (see OarVisual); the seat itself never moves. Tuned live in game, per ship.
    /// </summary>
    internal class ShipFit
    {
        // Ships not in the table: the benches as the game places them, no lean (the Karve's fit).
        private static readonly ShipFit s_default = new ShipFit(0f, 0f);

        // Keyed by the ship's prefab name.
        private static readonly Dictionary<string, ShipFit> s_fits = new Dictionary<string, ShipFit>
        {
            { "Karve", new ShipFit(0f, 0f) },
            { "VikingShip", new ShipFit(0.2f, 15f) },
            { "VikingShip_Ashlands", new ShipFit(0.2f, 15f) },
        };

        /// <summary>Meters the body slides from the seat towards the gunwale it rows over.</summary>
        internal float Outboard;

        /// <summary>
        /// Degrees the upper body (from the Spine bone up, oar included) leans out over the gunwale; the hips
        /// stay on the bench.
        /// </summary>
        internal float Lean;

        private ShipFit(float outboard, float lean)
        {
            Outboard = outboard;
            Lean = lean;
        }

        internal static ShipFit For(Ship ship)
        {
            return s_fits.TryGetValue(Utils.GetPrefabName(ship.gameObject), out ShipFit fit) ? fit : s_default;
        }
    }
}
