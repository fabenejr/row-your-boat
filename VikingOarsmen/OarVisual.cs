using System.Collections.Generic;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Attached to every Player. Shows the oar and the rowing pose while that player is rowing, on every client.
    /// </summary>
    /// <remarks>
    /// The oar is the ship's steering oar, held upright over the gunwale beside the rower like a paddle:
    /// the blade is pulled from the bow towards the stern through the water (drive), then lifted out and
    /// swung forward through the air (recovery).
    /// </remarks>
    internal class OarVisual : MonoBehaviour
    {
        // Duration of one stroke at StrokeSpeed 1 (seconds), and the share of it spent pulling.
        private const float StrokePeriod = 1.5f;
        private const float DriveFraction = 0.55f;

        // Fore/aft swing of the oar around the fulcrum (degrees), and how far the whole oar travels with it (meters).
        private const float SweepAngle = 30f;
        private const float SweepTravel = 0.25f;

        // The stroke is centered this far ahead of the seat (meters).
        private const float StrokeForward = 0.15f;

        // The shaft leans with its top towards the rower; on the recovery it leans further and lifts so the
        // blade clears the water (degrees, meters).
        private const float BaseTilt = 35f;
        private const float MaxTilt = 70f;
        private const float RecoveryLift = 0.2f;

        // The blade turns flat on the recovery, like real rowers do (degrees).
        private const float FeatherAngle = 70f;

        // Blade tip depth in the middle of the drive, and its clearance above the water on the recovery (meters).
        private const float BladeDepth = 0.35f;
        private const float BladeClearance = 0.15f;

        // The shaft passes this far outside the gunwale, so it doesn't cut through it, and its fulcrum may
        // rise at most this far above the gunwale to keep the blade shallow (meters).
        private const float ShaftGap = 0.08f;
        private const float MaxRaise = 0.5f;

        // Heights probed for the gunwale, from above the seat downwards (meters, relative to the seat).
        private const float GunwaleProbeTop = 1.5f;
        private const float GunwaleProbeBottom = -0.5f;
        private const float GunwaleProbeStep = 0.1f;
        private const float HullProbeDistance = 10f;

        // Hits this close to the hull's outermost point still count as the hull when looking for its top (meters).
        private const float HullThickness = 0.15f;

        // Probe hits in ship space, reused between fits.
        private static readonly List<Vector3> s_hullHits = new List<Vector3>();

        // Fulcrum placement used when no hull collider is found.
        private const float FallbackSideOffset = 0.6f;
        private const float FallbackHeight = 0.6f;

        // Accepted range for the OarScale setting.
        private const float MinOarScale = 0.2f;
        private const float MaxOarScale = 3f;

        // How far the rower must move on the ship before the oar is placed again (meters).
        private const float RelocateDistance = 0.3f;

        private Player _player;
        private RowerPose _pose;
        private OarRig _oar;
        private Ship _ship;
        private ZDOID _shipId = ZDOID.None;
        private WaterVolume _waterVolume;

        // Stroke cycle, 0..1 (0 = catch: blade forward, entering the water).
        private float _phase;

        // Whether the oar is placed, and the rower position (in ship space) it was placed for.
        private bool _fitted;
        private Vector3 _localRowerPosition;

        // Shaft lean on the recovery that lifts the blade clear of the water (degrees).
        private float _recoveryTilt = BaseTilt;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _pose = new RowerPose(_player);
        }

        private void LateUpdate()
        {
            Ship ship = ResolveRowingShip();
            if (ship == null)
            {
                Hide();
                return;
            }

            // Build the oar from this ship's steering oar when the player first rows it, and restart
            // the stroke and re-fit the oar whenever rowing (re)starts.
            if (_oar == null || !_oar.Root.activeSelf)
            {
                if (_oar == null)
                {
                    _oar = OarModel.Create(transform, ship);
                }
                _oar.Root.SetActive(true);
                _phase = 0f;
                _fitted = false;
                _pose.Reset();
            }

            // Re-fit when the rower changes seat.
            Vector3 localRower = ship.transform.InverseTransformPoint(transform.position);
            if (!_fitted || (localRower - _localRowerPosition).sqrMagnitude > RelocateDistance * RelocateDistance)
            {
                FitToShip(ship, localRower);
            }

            _phase = (_phase + Time.deltaTime * Mathf.Max(0f, Plugin.StrokeSpeed.Value) / StrokePeriod) % 1f;
            GetStroke(_phase, out float sweep, out float recovery);
            PoseOar(sweep, recovery);
            _pose.Apply(_oar, sweep);
        }

        private void OnDestroy()
        {
            DestroyOar();
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

            // Only search the scene again when the rowed ship changes; its steering oar may differ too.
            if (shipId != _shipId || _ship == null)
            {
                _shipId = shipId;
                GameObject shipObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(shipId) : null;
                _ship = shipObject != null ? shipObject.GetComponent<Ship>() : null;
                DestroyOar();
            }

            return _ship;
        }

        /// <summary>
        /// Rests the oar on the gunwale beside the rower, at a height where the blade bites the water.
        /// </summary>
        private void FitToShip(Ship ship, Vector3 localRower)
        {
            _localRowerPosition = localRower;
            Transform shipTransform = ship.transform;

            // Row over the side of the hull the rower sits on (starboard when centered): the root's X axis
            // points outboard. The model is mirrored if its steering oar hangs on port, so the tiller points inboard.
            float side = localRower.x >= 0f ? 1f : -1f;
            float scale = Mathf.Clamp(Plugin.OarScale.Value, MinOarScale, MaxOarScale);
            _oar.Root.transform.localScale = new Vector3(side, 1f, 1f);
            _oar.Size.localScale = new Vector3(_oar.SourceSide * scale, scale, scale);
            float bladeLength = _oar.BladeLength * scale;

            // Fulcrum just outside the gunwale, a little ahead of the seat.
            Vector3 fulcrum;
            bool onGunwale = TryFindGunwale(ship, localRower, side, out float halfWidth, out float gunwaleTop);
            fulcrum = onGunwale
                ? new Vector3(side * (halfWidth + ShaftGap), gunwaleTop, localRower.z + StrokeForward)
                : localRower + new Vector3(side * FallbackSideOffset, FallbackHeight, StrokeForward);

            // Raise it until the blade only dips BladeDepth into the water mid-drive, but keep it on the gunwale.
            float tilt = BaseTilt * Mathf.Deg2Rad;
            Vector3 bladeTip = fulcrum + new Vector3(side * bladeLength * Mathf.Sin(tilt), -bladeLength * Mathf.Cos(tilt), 0f);
            float water = WaterHeight(shipTransform, bladeTip);
            float bite = water - BladeDepth + bladeLength * Mathf.Cos(tilt);
            fulcrum.y = Mathf.Clamp(bite, fulcrum.y + 0.05f, fulcrum.y + MaxRaise);

            // Lean on the recovery just enough for the lifted blade to clear the water.
            float reach = (fulcrum.y + RecoveryLift - water - BladeClearance) / Mathf.Max(bladeLength, 0.01f);
            _recoveryTilt = Mathf.Clamp(Mathf.Acos(Mathf.Clamp(reach, -1f, 1f)) * Mathf.Rad2Deg, BaseTilt, MaxTilt);

            // The oar is fixed to the rower rather than the hull: both are carried by the seat, and the
            // player follows it at physics rate, so this keeps the hands and the oar from drifting apart.
            // (Remote players have no attach point; their synced transform sits on the seat too.)
            Transform seat = _player.GetAttachPoint();
            if (seat == null)
            {
                seat = transform;
            }
            Quaternion toSeat = Quaternion.Inverse(seat.rotation);
            _oar.Root.transform.localPosition = toSeat * (shipTransform.TransformPoint(fulcrum) - seat.position);
            _oar.Root.transform.localRotation = toSeat * shipTransform.rotation;

            PoseOar(0f, 0f);
            _pose.Fit(_oar, _oar.HandleLength * scale);

            Plugin.Log.LogInfo($"Oar on {ship.name}: rower at {localRower:F2}, gunwale {(onGunwale ? $"{halfWidth:F2} wide, top {gunwaleTop:F2}" : "not found")}, " +
                $"water {water:F2}, fulcrum {fulcrum:F2}, recovery tilt {_recoveryTilt:F0} (ship space).");
            _fitted = true;
        }

        /// <summary>
        /// Splits the stroke cycle into the oar's fore/aft swing (+1 = catch, blade forward; -1 = finish)
        /// and how far into the recovery it is (0 while the blade is in the water, 1 mid-recovery).
        /// </summary>
        private static void GetStroke(float phase, out float sweep, out float recovery)
        {
            // First half of the circle = drive, second half = recovery, warped so they can differ in length.
            float angle = phase < DriveFraction
                ? Mathf.PI * (phase / DriveFraction)
                : Mathf.PI + Mathf.PI * ((phase - DriveFraction) / (1f - DriveFraction));

            sweep = Mathf.Cos(angle);
            recovery = Mathf.Max(0f, -Mathf.Sin(angle));
        }

        /// <summary>
        /// Swings the oar for the current point of the stroke.
        /// </summary>
        private void PoseOar(float sweep, float recovery)
        {
            // Feather the blade around the shaft (tiller swinging outboard), lean it (top towards the rower,
            // blade outboard), then swing it fore/aft around the ship's beam.
            float tilt = Mathf.Lerp(BaseTilt, _recoveryTilt, recovery);
            _oar.Stroke.localRotation = Quaternion.Euler(-SweepAngle * sweep, 0f, 0f)
                * Quaternion.Euler(0f, 0f, tilt)
                * Quaternion.Euler(0f, -FeatherAngle * recovery, 0f);

            // The whole oar travels with the blade, so the lower hand pulls back while the top hand stays put.
            _oar.Stroke.localPosition = new Vector3(0f, RecoveryLift * recovery, SweepTravel * sweep);
        }

        /// <summary>
        /// Finds the gunwale beside the rower: the hull's half-width (outermost point) and the height of
        /// its top, probing from outside at the rower's station.
        /// </summary>
        private bool TryFindGunwale(Ship ship, Vector3 localRower, float side, out float halfWidth, out float top)
        {
            Transform shipTransform = ship.transform;
            Rigidbody shipBody = ship.GetComponent<Rigidbody>();
            Transform rudder = ship.m_rudderObject != null ? ship.m_rudderObject.transform : null;
            Vector3 inward = shipTransform.TransformDirection(Vector3.left * side);

            s_hullHits.Clear();
            for (float height = GunwaleProbeTop; height >= GunwaleProbeBottom; height -= GunwaleProbeStep)
            {
                Vector3 local = new Vector3(side * HullProbeDistance, localRower.y + height, localRower.z);
                if (TryHitHull(shipTransform.TransformPoint(local), inward, shipBody, rudder, out Vector3 point))
                {
                    s_hullHits.Add(shipTransform.InverseTransformPoint(point));
                }
            }

            // The outermost hits are the hull; rays passing over the gunwale stop on benches or the mast.
            halfWidth = -1f;
            top = float.MinValue;
            foreach (Vector3 hit in s_hullHits)
            {
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(hit.x));
            }
            foreach (Vector3 hit in s_hullHits)
            {
                if (Mathf.Abs(hit.x) > halfWidth - HullThickness)
                {
                    top = Mathf.Max(top, hit.y);
                }
            }

            return s_hullHits.Count > 0;
        }

        /// <summary>
        /// Closest point where a ray hits a collider belonging to the given ship, skipping the steering oar.
        /// </summary>
        private static bool TryHitHull(Vector3 origin, Vector3 direction, Rigidbody shipBody, Transform ignore, out Vector3 point)
        {
            point = Vector3.zero;
            float closest = float.MaxValue;

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, HullProbeDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                // Skip anything that isn't this ship's hull (water, terrain, players, other ships, the rudder).
                if (hit.collider.attachedRigidbody != shipBody || hit.distance >= closest
                    || (ignore != null && hit.transform.IsChildOf(ignore)))
                {
                    continue;
                }

                closest = hit.distance;
                point = hit.point;
            }

            return closest < float.MaxValue;
        }

        /// <summary>
        /// Water surface height (in ship space) at a ship-space point.
        /// </summary>
        private float WaterHeight(Transform ship, Vector3 local)
        {
            Vector3 world = ship.TransformPoint(local);
            world.y = Floating.GetWaterLevel(world, ref _waterVolume);
            return ship.InverseTransformPoint(world).y;
        }

        private void Hide()
        {
            if (_oar != null && _oar.Root.activeSelf)
            {
                _oar.Root.SetActive(false);
            }
        }

        private void DestroyOar()
        {
            if (_oar != null)
            {
                Destroy(_oar.Root);
                _oar = null;
            }
        }
    }
}
