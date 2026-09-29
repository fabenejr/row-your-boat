using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Attached to every Player. Shows an animated oar while that player is rowing, on every client.
    /// </summary>
    /// <remarks>
    /// The oar is posed in ship space, not player space, so it always sticks out of the nearest
    /// side of the hull and sweeps along the ship's length, whichever way the player is facing.
    /// </remarks>
    internal class OarVisual : MonoBehaviour
    {
        // Stroke shape (degrees). Positive pitch tilts the blade down towards the water.
        private const float SweepAngle = 35f;     // Fore/aft swing of the blade around the oarlock
        private const float BasePitch = 30f;      // Resting downward tilt of the oar
        private const float DipAngle = 10f;       // Extra tilt while the blade pulls through the water
        private const float LiftAngle = 14f;      // Blade lift while returning above the water
        private const float FeatherAngle = 90f;   // Blade turned flat on the return, like real rowers do
        private const float DriveFraction = 0.4f; // Share of the cycle spent pulling (the rest is the return)

        // Oarlock position relative to the rower's feet, in ship space (meters).
        private const float PivotHeight = 0.9f;
        private const float PivotSideOffset = 0.4f;

        // How far from the centerline the rower must be before switching sides (avoids flicker).
        private const float SideSwitchThreshold = 0.25f;

        private Player _player;
        private GameObject _oar;
        private Ship _ship;
        private ZDOID _shipId = ZDOID.None;
        private float _phase;
        private float _side = 1f;

        private void Awake()
        {
            _player = GetComponent<Player>();
        }

        private void LateUpdate()
        {
            Ship ship = ResolveRowingShip();
            if (ship == null)
            {
                Hide();
                return;
            }

            // Lazily build the oar the first time this player rows.
            if (_oar == null)
            {
                _oar = OarModel.Create(transform);
            }

            // Restart the stroke from the catch whenever rowing (re)starts.
            if (!_oar.activeSelf)
            {
                _oar.SetActive(true);
                _phase = 0f;
            }

            // Advance the stroke cycle.
            float period = Mathf.Max(0.2f, Plugin.StrokePeriod.Value);
            _phase = (_phase + Time.deltaTime / period) % 1f;

            PoseOar(ship.transform);
        }

        private void OnDestroy()
        {
            if (_oar != null)
            {
                Destroy(_oar);
            }
        }

        /// <summary>
        /// Looks up the ship this player is rowing from the synced ZDO, caching the instance.
        /// </summary>
        private Ship ResolveRowingShip()
        {
            if (_player == null)
            {
                return null;
            }

            ZDOID shipId = RowingController.GetRowingShip(_player);
            if (shipId == ZDOID.None)
            {
                _shipId = ZDOID.None;
                _ship = null;
                return null;
            }

            // Only search the scene again when the rowed ship changes.
            if (shipId != _shipId || _ship == null)
            {
                _shipId = shipId;
                GameObject shipObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(shipId) : null;
                _ship = shipObject != null ? shipObject.GetComponent<Ship>() : null;
            }

            return _ship;
        }

        /// <summary>
        /// Places and rotates the oar for the current point of the stroke cycle.
        /// </summary>
        private void PoseOar(Transform ship)
        {
            // Row on the side of the hull the player is standing on.
            float localX = ship.InverseTransformPoint(transform.position).x;
            if (Mathf.Abs(localX) > SideSwitchThreshold)
            {
                _side = Mathf.Sign(localX);
            }

            // Map the phase to an angle: first half of the circle = drive, second half = recovery.
            // The warp lets the drive and recovery take different amounts of time.
            float angle = _phase < DriveFraction
                ? Mathf.PI * (_phase / DriveFraction)
                : Mathf.PI + Mathf.PI * ((_phase - DriveFraction) / (1f - DriveFraction));

            float drive = Mathf.Max(0f, Mathf.Sin(angle));     // 0..1 while pulling
            float recovery = Mathf.Max(0f, -Mathf.Sin(angle)); // 0..1 while returning

            // Blade swings from the bow (catch) to the stern (finish) and back.
            float sweep = SweepAngle * Mathf.Cos(angle);
            float yaw = _side * (90f - sweep);

            // Blade digs in on the drive and lifts clear of the water on the recovery.
            float pitch = BasePitch + DipAngle * drive - LiftAngle * recovery;

            // Blade is vertical on the drive and turned flat on the recovery.
            float roll = FeatherAngle * Mathf.Clamp01(recovery * 2f);

            // Euler applies roll (around the shaft), then pitch, then yaw, all in ship space.
            Vector3 pivot = transform.position + ship.up * PivotHeight + ship.right * (_side * PivotSideOffset);
            Quaternion rotation = ship.rotation * Quaternion.Euler(pitch, yaw, roll);
            _oar.transform.SetPositionAndRotation(pivot, rotation);
        }

        private void Hide()
        {
            if (_oar != null && _oar.activeSelf)
            {
                _oar.SetActive(false);
            }
        }
    }
}
