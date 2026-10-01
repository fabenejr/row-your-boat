using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Shows the local rower's selected gear with the same arrows the helm uses (one arrow up = gear 1,
    /// two = gear 2, three = gear 3, one down = reverse, none = neutral), around the helm's wheel icon.
    /// </summary>
    /// <remarks>
    /// Built by cloning the vanilla ship HUD instead of shipping our own sprites, so it looks exactly like
    /// the helm's. The wheel in the middle is a placeholder until there is an oar icon. Vanilla only
    /// drives its own copy for the helmsman (Player.GetControlledShip), so ours never overlaps with it.
    /// </remarks>
    internal class GearHud : MonoBehaviour
    {
        // How far above the gunwale beside the seat the controls float, in meters (the helm uses
        // Ship.m_controlGuiPos). Anchored to the ship, not the rower, whose stroke animation would shake it.
        private const float HeightAboveGunwale = 0.3f;

        private Hud _hud;
        private GameObject _root;
        private Transform _controlsRoot;
        private GameObject _slow;
        private GameObject _half;
        private GameObject _full;
        private GameObject _back;

        private void Start()
        {
            _hud = GetComponent<Hud>();
            if (!Build())
            {
                Plugin.Log.LogWarning("Ship HUD layout not recognized; the gear indicator is disabled.");
                if (_root != null)
                {
                    Destroy(_root);
                    _root = null;
                }
            }
        }

        /// <summary>
        /// Clones Hud.m_shipHudRoot and keeps only the speed arrows and the wheel icon. Counterparts are
        /// found by their path under the root, so a game update that moves things around disables the
        /// indicator (Build returns false) instead of breaking the HUD.
        /// </summary>
        private bool Build()
        {
            GameObject source = _hud.m_shipHudRoot;
            if (source == null)
            {
                return false;
            }

            _root = Instantiate(source, source.transform.parent);
            _root.name = "VikingOarsmen_GearHud";
            _root.SetActive(false);

            _controlsRoot = FindCounterpart(_hud.m_shipControlsRoot);
            _slow = FindCounterpartObject(_hud.m_rudderSlow);
            _half = FindCounterpartObject(_hud.m_rudderForward);
            _full = FindCounterpartObject(_hud.m_rudderFastForward);
            _back = FindCounterpartObject(_hud.m_rudderBackward);
            if (_controlsRoot == null || _slow == null || _half == null || _full == null || _back == null
                || FindCounterpart(_hud.m_shipRudderIcon) == null)
            {
                return false;
            }

            // Everything about steering and sailing is meaningless for a rower.
            Hide(_hud.m_rudderLeft);
            Hide(_hud.m_rudderRight);
            Hide(_hud.m_halfSail);
            Hide(_hud.m_fullSail);
            Hide(_hud.m_rudder);
            Hide(_hud.m_shipWindIndicatorRoot);
            Hide(_hud.m_shipRudderIndicator);
            return true;
        }

        private void LateUpdate()
        {
            if (_root == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || !RowingController.IsRowingMode(player))
            {
                _root.SetActive(false);
                return;
            }

            Camera camera = Utils.GetMainCamera();
            OarVisual oar = player.GetComponent<OarVisual>();
            if (camera == null || oar == null || !oar.TryGetGunwalePoint(out Vector3 gunwale))
            {
                _root.SetActive(false);
                return;
            }

            RowingGear gear = RowingController.GetSelectedGear();
            _root.SetActive(true);
            _slow.SetActive(gear == RowingGear.Slow);
            _half.SetActive(gear == RowingGear.Half);
            _full.SetActive(gear == RowingGear.Full);
            _back.SetActive(gear == RowingGear.Back);
            _controlsRoot.position = camera.WorldToScreenPointScaled(gunwale + Vector3.up * HeightAboveGunwale);
        }

        private void OnDestroy()
        {
            if (_root != null)
            {
                Destroy(_root);
            }
        }

        /// <summary>
        /// Returns the clone's copy of a vanilla ship HUD element, or null if it isn't under m_shipHudRoot.
        /// </summary>
        private Transform FindCounterpart(Component original)
        {
            return original != null ? FindCounterpart(original.gameObject) : null;
        }

        private Transform FindCounterpart(GameObject original)
        {
            if (original == null)
            {
                return null;
            }

            Transform sourceRoot = _hud.m_shipHudRoot.transform;
            Transform current = original.transform;
            if (current == sourceRoot)
            {
                return _root.transform;
            }

            string path = current.name;
            for (current = current.parent; current != null && current != sourceRoot; current = current.parent)
            {
                path = current.name + "/" + path;
            }

            return current == sourceRoot ? _root.transform.Find(path) : null;
        }

        private GameObject FindCounterpartObject(GameObject original)
        {
            Transform copy = FindCounterpart(original);
            return copy != null ? copy.gameObject : null;
        }

        private void Hide(Component original)
        {
            if (original != null)
            {
                Hide(original.gameObject);
            }
        }

        private void Hide(GameObject original)
        {
            Transform copy = FindCounterpart(original);
            if (copy != null)
            {
                copy.gameObject.SetActive(false);
            }
        }
    }
}
