using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Attached to every Player. While that player is rowing, on every client: plays the rowing animation
    /// (RowerAnimation), moves the oar in their hand to the rowing grip, splashes as the blade enters the
    /// water, turns them to face the stern like a real rower, and finds the gunwale beside their seat for
    /// the gear indicator (GearHud).
    /// </summary>
    /// <remarks>
    /// The oar itself is the equipped weapon, already in the rower's hand (see OarItem). Only the visual
    /// model turns: the player's own transform keeps following the seat, as the game expects.
    /// </remarks>
    internal class OarVisual : MonoBehaviour
    {
        // Heights probed for the gunwale, from above the seat downwards (meters, relative to the seat).
        private const float GunwaleProbeTop = 1.5f;
        private const float GunwaleProbeBottom = -0.5f;
        private const float GunwaleProbeStep = 0.1f;
        private const float HullProbeDistance = 10f;

        // Hits this close to the hull's outermost point still count as the hull when looking for its top (meters).
        private const float HullThickness = 0.15f;

        // Probe hits in ship space, reused between fits.
        private static readonly List<Vector3> s_hullHits = new List<Vector3>();

        // Gunwale placement used when no hull collider is found.
        private const float FallbackSideOffset = 0.6f;
        private const float FallbackHeight = 0.6f;

        // How far the rower must move on the ship before the gunwale is found again (meters).
        private const float RelocateDistance = 0.3f;

        // Sea level used when the game can't tell (meters), and Floating.GetWaterLevel's answer below this
        // means no water volume was found.
        private const float DefaultSeaLevel = 30f;
        private const float NoWater = -1000f;

        // Read by name, so a game update renaming it only leaves the oar in its weapon grip.
        private static readonly FieldInfo s_rightItemInstanceField = AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance");

        private Player _player;
        private VisEquipment _visEquipment;
        private RowerAnimation _animation;
        private Ship _ship;
        private ZDOID _shipId = ZDOID.None;

        // Whether the gunwale was found, and the rower position (in ship space) it was found for.
        private bool _fitted;
        private Vector3 _localRowerPosition;

        // Top of the gunwale right beside the seat, in ship space: where the gear indicator sits (see GearHud).
        private Vector3 _localGunwalePoint;

        // The visual model turned to face the stern, so it can be turned back when rowing stops.
        private Transform _turnedVisual;

        // The hand-held oar last put in the rowing grip, and whether it was moved to the left hand (mirrored
        // clips), so it is set again only on change.
        private GameObject _rowingGripInstance;
        private bool _rowingGripMirrored;

        // Whether the blade tip was under the water last frame, once that is known (no splash on the first frame).
        private bool _bladeWet;
        private bool _bladeTracked;
        private WaterVolume _waterVolume;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _visEquipment = GetComponent<VisEquipment>();
            _animation = new RowerAnimation(_player);
        }

        // Before the Animator evaluates this frame.
        private void Update()
        {
            Ship ship = ResolveRowingShip();
            bool rowing = ship != null;
            // The authored clips row over the rower's left: facing the stern, that is starboard (+X), the
            // side FitToShip picks for a centered seat too. Port-side seats use the mirrored clips.
            bool mirrored = rowing && ship.transform.InverseTransformPoint(transform.position).x < 0f;
            _animation.Update(rowing, rowing ? RowingController.GetRowingGear(_player) : RowingGear.Stop, mirrored);
            UpdateGrip(rowing, mirrored);
        }

        private void LateUpdate()
        {
            Ship ship = _ship;
            if (ship == null)
            {
                _fitted = false;
                FaceForward();
                return;
            }

            Vector3 localRower = ship.transform.InverseTransformPoint(transform.position);
            if (!_fitted || (localRower - _localRowerPosition).sqrMagnitude > RelocateDistance * RelocateDistance)
            {
                FitToShip(ship, localRower);
            }

            FaceStern(ship);
            TrackSplash();
        }

        private void OnDestroy()
        {
            FaceForward();
            _animation.Destroy();
        }

        /// <summary>
        /// Splashes where the blade tip breaks the surface on its way in. Runs after the Animator and the turn
        /// towards the stern, so the tip is where it is drawn this frame.
        /// </summary>
        private void TrackSplash()
        {
            Transform model = _rowingGripInstance != null ? _rowingGripInstance.transform.Find("model") : null;
            if (model == null)
            {
                _bladeTracked = false;
                return;
            }

            Vector3 tip = model.TransformPoint(0f, 0f, OarItem.BladeTipZ);
            float water = WaterLevel(tip);
            bool wet = tip.y < water;
            if (wet && !_bladeWet && _bladeTracked)
            {
                OarSplash.Play(new Vector3(tip.x, water, tip.z));
            }
            _bladeWet = wet;
            _bladeTracked = true;
        }

        /// <summary>
        /// World height of the water surface, waves included, at a world position.
        /// </summary>
        private float WaterLevel(Vector3 world)
        {
            // Probe at sea level, which is always inside the water volume, however high or low the blade is.
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : DefaultSeaLevel;
            float level = Floating.GetWaterLevel(new Vector3(world.x, sea, world.z), ref _waterVolume);
            return level > NoWater ? level : sea;
        }

        /// <summary>
        /// Rowing grip on the oar in the hand while rowing, weapon grip otherwise. VisEquipment rebuilds the
        /// hand-held instance whenever the equipment changes, so it is checked every frame.
        /// </summary>
        /// <remarks>
        /// The oar hangs from the hand at the top of its handle: the right hand in the authored clips, the
        /// left one in the mirrored clips. Left in the right hand there, it would follow the lower hand, whose
        /// wrist the Humanoid retargeting doesn't reproduce closely enough, and the blade swings into the ship.
        /// VisEquipment only ever destroys the instance through its own reference, so the move is safe.
        /// </remarks>
        private void UpdateGrip(bool rowing, bool mirrored)
        {
            GameObject held = s_rightItemInstanceField != null && _visEquipment != null
                ? s_rightItemInstanceField.GetValue(_visEquipment) as GameObject
                : null;
            if (rowing && held != null && OarItem.IsOar(_player.GetCurrentWeapon()))
            {
                if (held != _rowingGripInstance || mirrored != _rowingGripMirrored)
                {
                    OarItem.SetRowingGrip(held, true);
                    Transform hand = mirrored ? _visEquipment.m_leftHand : _visEquipment.m_rightHand;
                    if (hand != null)
                    {
                        held.transform.SetParent(hand, false);
                    }
                    _rowingGripInstance = held;
                    _rowingGripMirrored = mirrored;
                }
                return;
            }

            if (_rowingGripInstance != null)
            {
                ReleaseGrip(_rowingGripInstance);
            }
            _rowingGripInstance = null;
        }

        /// <summary>
        /// Back to the weapon grip, in the right hand where VisEquipment put it.
        /// </summary>
        private void ReleaseGrip(GameObject instance)
        {
            OarItem.SetRowingGrip(instance, false);
            if (_visEquipment != null && _visEquipment.m_rightHand != null)
            {
                instance.transform.SetParent(_visEquipment.m_rightHand, false);
            }
        }

        /// <summary>
        /// World position of the gunwale beside the rower's seat, on the side they sit on. It moves only
        /// with the ship. False until it has been found.
        /// </summary>
        internal bool TryGetGunwalePoint(out Vector3 point)
        {
            if (!_fitted || _ship == null)
            {
                point = Vector3.zero;
                return false;
            }

            point = _ship.transform.TransformPoint(_localGunwalePoint);
            return true;
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
                _fitted = false;
            }

            return _ship;
        }

        /// <summary>
        /// Finds the gunwale beside the rower, on the side of the hull they sit on (starboard when centered).
        /// </summary>
        private void FitToShip(Ship ship, Vector3 localRower)
        {
            _localRowerPosition = localRower;
            float side = localRower.x >= 0f ? 1f : -1f;
            bool onGunwale = TryFindGunwale(ship, localRower, side, out float halfWidth, out float gunwaleTop);
            _localGunwalePoint = onGunwale
                ? new Vector3(side * halfWidth, gunwaleTop, localRower.z)
                : localRower + new Vector3(side * FallbackSideOffset, FallbackHeight, 0f);

            Plugin.Log.LogInfo($"Rower on {ship.name}: at {localRower:F2}, gunwale " +
                $"{(onGunwale ? $"{halfWidth:F2} wide, top {gunwaleTop:F2}" : "not found")} (ship space).");
            _fitted = true;
        }

        /// <summary>
        /// Turns the visual model to face the ship's stern, whichever way the bench faces. Done every frame
        /// because the visual is only ours to turn while rowing; the seat itself never turns on the ship.
        /// </summary>
        private void FaceStern(Ship ship)
        {
            GameObject visual = _player.GetVisual();
            if (visual == null)
            {
                return;
            }

            Vector3 up = transform.up;
            Vector3 stern = Vector3.ProjectOnPlane(-ship.transform.forward, up);
            if (stern.sqrMagnitude < 1e-6f)
            {
                return;
            }

            _turnedVisual = visual.transform;
            _turnedVisual.rotation = Quaternion.LookRotation(stern, up);
        }

        /// <summary>
        /// Gives the visual model back its own facing when rowing stops.
        /// </summary>
        private void FaceForward()
        {
            if (_turnedVisual != null)
            {
                _turnedVisual.localRotation = Quaternion.identity;
            }
            _turnedVisual = null;
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
    }
}
