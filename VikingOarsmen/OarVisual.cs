using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Attached to every Player. Shows an animated oar while that player is rowing, on every client.
    /// </summary>
    /// <remarks>
    /// The oar rests on the gunwale next to the rower and its angle is computed from the actual
    /// water level, so it fits any ship (raft, karve, longship, drakkar) without per-ship tuning.
    /// </remarks>
    internal class OarVisual : MonoBehaviour
    {
        // Stroke shape (degrees).
        private const float SweepAngle = 35f;     // Fore/aft swing of the blade around the oarlock
        private const float FeatherAngle = 90f;   // Blade turned flat on the return, like real rowers do
        private const float DriveFraction = 0.4f; // Share of the cycle spent pulling (the rest is the return)

        // Blade tip depth below the water while pulling, and clearance above it while returning (meters).
        private const float BladeDepth = 0.45f;
        private const float BladeClearance = 0.35f;

        // Angle below the horizon the oar should rest at; the oar length adapts to each ship's height to keep it.
        private const float PreferredPitch = 35f;
        private const float MinOutboardLength = 2.2f;
        private const float MaxOutboardLength = 6f;

        // Heights (relative to the rower, meters) probed to find the gunwale, from top to bottom.
        private static readonly float[] s_gunwaleProbeHeights = { 0.8f, 0.5f, 0.2f, -0.1f, -0.4f };
        private const float GunwaleProbeDistance = 8f;

        // Oarlock placement used when no hull collider is found.
        private const float FallbackSideOffset = 0.6f;
        private const float FallbackHeight = 0.6f;

        // How far the rower must move on the ship before the oarlock is searched again (meters).
        private const float RelocateDistance = 0.3f;

        // Smoothing rate for the water height so waves don't make the oar jitter.
        private const float WaterSmoothing = 5f;

        private Player _player;
        private GameObject _oar;
        private Ship _ship;
        private ZDOID _shipId = ZDOID.None;
        private WaterVolume _waterVolume;

        private float _phase;
        private float _side = 1f;

        // Oarlock position in ship space, and the rower position it was computed for.
        private bool _hasOarlock;
        private Vector3 _localOarlock;
        private Vector3 _localRowerPosition;

        private float _outboardLength = OarModel.OutboardLength;
        private float _smoothedHeight;

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

            // Restart the stroke from the catch and re-fit the oar whenever rowing (re)starts.
            if (!_oar.activeSelf)
            {
                _oar.SetActive(true);
                _phase = 0f;
                _hasOarlock = false;
            }

            // Re-fit when the rower changes seat.
            Vector3 localRower = ship.transform.InverseTransformPoint(transform.position);
            if (!_hasOarlock || (localRower - _localRowerPosition).sqrMagnitude > RelocateDistance * RelocateDistance)
            {
                FitToShip(ship, localRower);
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
                _hasOarlock = false;
                GameObject shipObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(shipId) : null;
                _ship = shipObject != null ? shipObject.GetComponent<Ship>() : null;
            }

            return _ship;
        }

        /// <summary>
        /// Finds the oarlock on the gunwale beside the rower and sizes the oar so the blade reaches the water.
        /// </summary>
        private void FitToShip(Ship ship, Vector3 localRower)
        {
            Transform shipTransform = ship.transform;
            _localRowerPosition = localRower;

            // Row over the side of the hull the rower sits on (starboard when centered).
            _side = localRower.x >= 0f ? 1f : -1f;
            Vector3 outward = shipTransform.right * _side;

            // Default oarlock if the hull can't be found.
            Vector3 oarlock = transform.position + shipTransform.up * FallbackHeight + outward * FallbackSideOffset;

            // Cast from outside the ship back towards the rower; the first hull hit is the gunwale.
            Rigidbody shipBody = ship.GetComponent<Rigidbody>();
            foreach (float probeHeight in s_gunwaleProbeHeights)
            {
                Vector3 target = transform.position + shipTransform.up * probeHeight;
                if (TryHitHull(target + outward * GunwaleProbeDistance, -outward, shipBody, out Vector3 hullPoint))
                {
                    oarlock = hullPoint + shipTransform.up * 0.05f;
                    break;
                }
            }
            _localOarlock = shipTransform.InverseTransformPoint(oarlock);

            // Taller ships get longer oars so the resting angle stays the same everywhere.
            float height = oarlock.y - Floating.GetWaterLevel(oarlock, ref _waterVolume);
            _outboardLength = Mathf.Clamp(height / Mathf.Sin(PreferredPitch * Mathf.Deg2Rad), MinOutboardLength, MaxOutboardLength);
            _oar.transform.localScale = new Vector3(1f, 1f, _outboardLength / OarModel.OutboardLength);

            _smoothedHeight = height;
            _hasOarlock = true;
        }

        /// <summary>
        /// Closest point where a ray hits a collider belonging to the given ship.
        /// </summary>
        private static bool TryHitHull(Vector3 origin, Vector3 direction, Rigidbody shipBody, out Vector3 point)
        {
            point = Vector3.zero;
            float closest = float.MaxValue;

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, GunwaleProbeDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                // Skip anything that isn't part of this ship (water, terrain, players, other ships).
                if (hit.collider.attachedRigidbody != shipBody || hit.distance >= closest)
                {
                    continue;
                }

                closest = hit.distance;
                point = hit.point;
            }

            return closest < float.MaxValue;
        }

        /// <summary>
        /// Places and rotates the oar for the current point of the stroke cycle.
        /// </summary>
        private void PoseOar(Transform ship)
        {
            Vector3 oarlock = ship.TransformPoint(_localOarlock);

            // Heading-only frame so the pitch is measured from the true horizon even when the hull rolls.
            Vector3 forward = Vector3.ProjectOnPlane(ship.forward, Vector3.up);
            Quaternion heading = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : ship.rotation;

            // Oarlock height above the water where the blade enters, smoothed against wave jitter.
            Vector3 outward = heading * Vector3.right * _side;
            float height = oarlock.y - Floating.GetWaterLevel(oarlock + outward * (_outboardLength * 0.8f), ref _waterVolume);
            _smoothedHeight = Mathf.Lerp(_smoothedHeight, height, 1f - Mathf.Exp(-WaterSmoothing * Time.deltaTime));

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

            // Tilt so the blade tip is under the surface while pulling and above it while returning.
            float tipDrop = _smoothedHeight + BladeDepth * drive - BladeClearance * recovery;
            float pitch = Mathf.Asin(Mathf.Clamp(tipDrop / _outboardLength, -0.5f, 0.98f)) * Mathf.Rad2Deg;

            // Blade is vertical on the drive and turned flat on the recovery.
            float roll = FeatherAngle * Mathf.Clamp01(recovery * 2f);

            // Euler applies roll (around the shaft), then pitch, then yaw.
            _oar.transform.SetPositionAndRotation(oarlock, heading * Quaternion.Euler(pitch, yaw, roll));
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
