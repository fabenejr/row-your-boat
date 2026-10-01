using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Attached to every Ship. On the ship's owner, pushes the ship forward while anyone aboard is rowing.
    /// </summary>
    /// <remarks>
    /// Thrust only pushes along the bow direction and never applies torque, so whoever is at the
    /// helm keeps full control of the heading through the vanilla rudder.
    /// </remarks>
    internal class ShipRowing : MonoBehaviour
    {
        // Center of mass higher than this above the water means the ship is beached.
        private const float MaxHeightAboveWater = 1.5f;

        private Ship _ship;
        private ZNetView _nview;
        private Rigidbody _body;
        private WaterVolume _previousWater;

        // Current thrust as a fraction of a full sail in a strong tailwind, eased by RampUpTime.
        private float _power;

        private void Awake()
        {
            _ship = GetComponent<Ship>();
            _nview = GetComponent<ZNetView>();
            _body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            // Only the owner simulates ship physics; other clients get the synced position.
            if (_ship == null || _body == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                _power = 0f;
                return;
            }

            // Each rower adds a share of the thrust weighted by their gear (negative for reverse),
            // capped symmetrically so a crew rowing hard backward can't exceed full-ahead thrust either.
            float maxPower = Mathf.Max(0f, Plugin.MaxRowingPower.Value);
            float target = Mathf.Clamp(SumRowingWeight() * Plugin.PowerPerRower.Value, -maxPower, maxPower);

            // Ease towards the target so starting, stopping, crew changes and gear shifts feel smooth.
            float rampTime = Mathf.Max(0.01f, Plugin.RampUpTime.Value);
            _power = Mathf.MoveTowards(_power, target, Mathf.Max(maxPower, 0.01f) * Time.fixedDeltaTime / rampTime);
            if (Mathf.Approximately(_power, 0f) || !IsFloating())
            {
                return;
            }

            // Bow direction flattened so waves tilting the hull don't push it up or down.
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }
            forward.Normalize();

            // Vanilla sails push with m_sailForceFactor per physics step at full sail in a strong tailwind,
            // so scaling that value gives each ship type its own "wind at your back" speed.
            _body.AddForce(forward * (_ship.m_sailForceFactor * _power), ForceMode.VelocityChange);
        }

        /// <summary>
        /// Sum of every rower's gear weight aboard this ship (see VikingOarsmen-Documents/Plano-Marchas-e-Remo.md
        /// section 5): Slow is the PowerPerRower baseline (weight 1), Half/Full multiply it, Back negates it,
        /// Stop (neutral, or a stamina-suspended boost — see RowingController.ResolveEffectiveGear) contributes none.
        /// </summary>
        private float SumRowingWeight()
        {
            float sum = 0f;
            ZDOID shipId = _nview.GetZDO().m_uid;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player != null && RowingController.GetRowingShip(player) == shipId && _ship.IsPlayerInBoat(player))
                {
                    sum += GetGearWeight(RowingController.GetRowingGear(player));
                }
            }
            return sum;
        }

        private static float GetGearWeight(RowingGear gear)
        {
            switch (gear)
            {
                case RowingGear.Back: return Plugin.ReverseMultiplier.Value;
                case RowingGear.Slow: return 1f;
                case RowingGear.Half: return Plugin.Gear2Multiplier.Value;
                case RowingGear.Full: return Plugin.Gear3Multiplier.Value;
                default: return 0f; // Stop
            }
        }

        private bool IsFloating()
        {
            Vector3 center = _body.worldCenterOfMass;
            float waterLevel = Floating.GetWaterLevel(center, ref _previousWater);
            return center.y < waterLevel + MaxHeightAboveWater;
        }
    }
}
