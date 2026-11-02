using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingOarsmen
{
    /// <summary>
    /// A carved wooden panel on the left of the screen while you row: how many aboard are rowing at the top, and
    /// a top-down drawing of the ship below, turned to match the camera, with a token for every player at their
    /// place. Rowers' tokens
    /// are colored by gear with an oar that swings in time with their stroke; everyone else aboard is grey.
    /// </summary>
    /// <remarks>
    /// The artwork is embedded in the DLL (see CrewHudArt). Reads who rows and in which gear from each player's
    /// ZDO, like the ship's thrust and sound, so it also shows remote rowers without sending anything.
    /// </remarks>
    internal class CrewHud : MonoBehaviour
    {
        // Panel layout, in canvas units: keep in sync with art/crew_hud/preview.html.
        private static readonly Vector2 s_panelSize = new Vector2(180f, 340f);
        private const float PanelX = 110f;
        private static readonly Vector2 s_shipArea = new Vector2(138f, 232f);
        private const float ShipAreaY = -21f;

        // BOW and STERN captions above and below the ship area, so the drawing reads the right way round.
        private const float EndLabelOffset = 124f;

        // The drawing turns stern up while the camera looks towards the stern (as it does behind a rower, who
        // faces aft), so left and right on the panel match the screen. The margin keeps it from flickering
        // while the camera looks across the ship.
        private const float FlipMargin = 0.2f;

        // Header: an icon, the count beside it and a caption under both, for the rowers and for everyone aboard.
        private const float IconSize = 18f;
        private const float CountFontSize = 22f;
        private const float CaptionFontSize = 10f;
        private static readonly Color s_countColor = new Color32(0x3a, 0x22, 0x0e, 0xff);
        private static readonly Color s_captionColor = new Color32(0x5c, 0x3a, 0x1c, 0xff);

        // Tokens shrink on big ships so neighbouring seats don't overlap; the oar is sized with the token.
        private const float MaxToken = 16f;
        private const float MinToken = 11f;
        private const float TokenPerMeter = 1.6f;
        private static readonly Vector2 s_oarSize = new Vector2(32f, 6f);
        private const float RingScale = 1.5f;

        // How far the oar swings either side of square to the ship during a stroke, in degrees.
        private const float SwingDegrees = 30f;

        private static readonly Color s_passengerColor = new Color(0.55f, 0.55f, 0.55f);

        private readonly List<Player> _aboard = new List<Player>();
        private readonly List<Marker> _markers = new List<Marker>();

        private Hud _hud;
        private RectTransform _root;
        private RectTransform _view;
        private Image _ship;
        private RectTransform _crew;
        private TextMeshProUGUI _topLabel;
        private TextMeshProUGUI _bottomLabel;
        private bool _sternUp;
        private TextMeshProUGUI _rowingCount;
        private TextMeshProUGUI _aboardCount;
        private int _shownRowers = -1;
        private int _shownAboard = -1;

        private class Marker
        {
            public RectTransform Oar;
            public RectTransform Ring;
            public Image Token;
            public RectTransform Helmet;
        }

        private void Start()
        {
            _hud = GetComponent<Hud>();
            Transform parent = _hud.m_rootObject != null ? _hud.m_rootObject.transform : transform;

            _root = NewRect("VikingOarsmen_CrewHud", parent);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 0.5f);
            _root.sizeDelta = s_panelSize;
            _root.anchoredPosition = new Vector2(PanelX, 0f);
            Image frame = NewImage("Frame", _root, "frame");
            frame.rectTransform.anchorMin = Vector2.zero;
            frame.rectTransform.anchorMax = Vector2.one;
            frame.rectTransform.sizeDelta = Vector2.zero;

            _rowingCount = BuildCounter(24f, "icon_oar", "ROWING");
            _aboardCount = BuildCounter(102f, "icon_crew", "ABOARD");

            // The ship and its crew turn together; the oars' angles are drawn in the ship's frame.
            _view = NewRect("View", _root);
            _view.anchoredPosition = new Vector2(0f, ShipAreaY);
            _ship = NewImage("Ship", _view, null);
            _crew = NewRect("Crew", _view);

            _topLabel = NewEndLabel("Top", EndLabelOffset);
            _bottomLabel = NewEndLabel("Bottom", -EndLabelOffset);
            ApplyOrientation();

            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_root == null)
            {
                return;
            }

            if (!Plugin.ShowCrewHud.Value || !TryCollectAboard(out Ship ship, out ZDOID shipId))
            {
                _root.gameObject.SetActive(false);
                return;
            }

            CrewHudArt.ShipArt art = CrewHudArt.For(ship);
            float length = art.ZMax - art.ZMin;
            float scale = Mathf.Min(s_shipArea.y / length, s_shipArea.x / (art.HalfWidth * 2f));
            float zMid = (art.ZMin + art.ZMax) / 2f;
            _ship.sprite = CrewHudArt.Get(art.Sprite);
            _ship.rectTransform.sizeDelta = new Vector2(art.HalfWidth * 2f * scale, length * scale);
            UpdateOrientation(ship);
            _root.gameObject.SetActive(true);

            float token = Mathf.Clamp(scale * TokenPerMeter, MinToken, MaxToken);
            AnimationClip stroke = ModAssets.RowStroke(false);
            int rowers = 0;
            for (int i = 0; i < _aboard.Count; i++)
            {
                Player player = _aboard[i];
                Marker marker = GetMarker(i);
                Transform seat = player.GetAttachPoint();
                Vector3 local = ship.transform.InverseTransformPoint(seat != null ? seat.position : player.transform.position);
                Vector2 position = new Vector2(local.x * scale, (local.z - zMid) * scale);

                bool rowing = RowingController.GetRowingShip(player) == shipId;
                RowingGear gear = rowing ? RowingController.GetRowingGear(player) : RowingGear.Stop;
                if (rowing)
                {
                    rowers++;
                }

                marker.Ring.gameObject.SetActive(player == Player.m_localPlayer);
                marker.Ring.anchoredPosition = position;
                marker.Ring.sizeDelta = Vector2.one * token * RingScale;
                marker.Token.rectTransform.anchoredPosition = position;
                marker.Token.rectTransform.sizeDelta = Vector2.one * token;
                marker.Token.color = rowing ? GearColor(gear) : s_passengerColor;
                marker.Helmet.anchoredPosition = position;
                marker.Helmet.sizeDelta = Vector2.one * token;
                marker.Oar.gameObject.SetActive(rowing);
                marker.Oar.anchoredPosition = position;
                marker.Oar.sizeDelta = s_oarSize * (token / MaxToken);
                marker.Oar.localRotation = Quaternion.Euler(0f, 0f, OarAngle(gear, local.x < 0f ? -1f : 1f, stroke));
            }
            for (int i = _aboard.Count; i < _markers.Count; i++)
            {
                HideMarker(_markers[i]);
            }

            // Rebuilding the text every frame would allocate; it only changes when someone sits down or gets up.
            if (rowers != _shownRowers || _aboard.Count != _shownAboard)
            {
                _shownRowers = rowers;
                _shownAboard = _aboard.Count;
                _rowingCount.text = rowers.ToString();
                _aboardCount.text = _aboard.Count.ToString();
            }
        }

        private void OnDestroy()
        {
            if (_root != null)
            {
                Destroy(_root.gameObject);
            }
        }

        /// <summary>
        /// Finds the ship the local player is rowing and fills _aboard with every player on it, in a stable
        /// order. False when the local player isn't rowing.
        /// </summary>
        private bool TryCollectAboard(out Ship ship, out ZDOID shipId)
        {
            _aboard.Clear();
            ship = null;
            shipId = ZDOID.None;
            Player local = Player.m_localPlayer;
            if (local == null || !RowingController.IsRowingMode(local))
            {
                return false;
            }

            shipId = RowingController.GetRowingShip(local);
            ship = local.GetComponent<OarVisual>()?.RowingShip;
            if (shipId == ZDOID.None || ship == null)
            {
                return false;
            }

            foreach (Player player in Player.GetAllPlayers())
            {
                if (player != null && (ship.IsPlayerInBoat(player) || RowingController.GetRowingShip(player) == shipId))
                {
                    _aboard.Add(player);
                }
            }
            return true;
        }

        /// <summary>
        /// Turns the drawing stern up when the camera looks towards the stern, bow up when it looks towards the
        /// bow, and leaves it as it is while the camera looks across the ship.
        /// </summary>
        private void UpdateOrientation(Ship ship)
        {
            Camera camera = Utils.GetMainCamera();
            if (camera == null)
            {
                return;
            }

            Vector3 look = Vector3.ProjectOnPlane(camera.transform.forward, ship.transform.up);
            if (look.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float towardsBow = Vector3.Dot(look.normalized, ship.transform.forward);
            bool sternUp = towardsBow < -FlipMargin || (_sternUp && towardsBow < FlipMargin);
            if (sternUp != _sternUp)
            {
                _sternUp = sternUp;
                ApplyOrientation();
            }
        }

        private void ApplyOrientation()
        {
            _view.localRotation = Quaternion.Euler(0f, 0f, _sternUp ? 180f : 0f);
            _topLabel.text = _sternUp ? "STERN" : "BOW";
            _bottomLabel.text = _sternUp ? "BOW" : "STERN";
        }

        private TextMeshProUGUI NewEndLabel(string name, float y)
        {
            TextMeshProUGUI label = NewText(name, CaptionFontSize, s_captionColor);
            label.rectTransform.anchoredPosition = new Vector2(0f, ShipAreaY + y);
            label.rectTransform.sizeDelta = new Vector2(80f, 14f);
            label.alignment = TextAlignmentOptions.Center;
            label.characterSpacing = 4f;
            return label;
        }

        /// <summary>
        /// Angle of the oar in the drawing: square to the ship on the rower's side, swung back and forth in time
        /// with the stroke (the other way in reverse), still in neutral.
        /// </summary>
        private static float OarAngle(RowingGear gear, float side, AnimationClip stroke)
        {
            // Bow is up, so an oar square to the ship points along +x (starboard) or -x (port).
            float outward = side > 0f ? 0f : 180f;
            float rate = stroke != null ? RowingBeat.StrokeRate(gear, stroke.length) : 0f;
            if (rate <= 0f)
            {
                return outward;
            }

            float swing = Mathf.Sin(RowingBeat.GetBeat(rate) * Mathf.PI * 2f) * SwingDegrees;
            if (gear == RowingGear.Back)
            {
                swing = -swing;
            }
            return outward + swing * side;
        }

        private static Color GearColor(RowingGear gear)
        {
            switch (gear)
            {
                case RowingGear.Back: return new Color(1f, 0.55f, 0.15f);
                case RowingGear.Slow: return new Color(0.45f, 0.85f, 0.4f);
                case RowingGear.Half: return new Color(1f, 0.9f, 0.3f);
                case RowingGear.Full: return new Color(0.95f, 0.3f, 0.25f);
                default: return new Color(0.7f, 0.7f, 0.7f); // Stop
            }
        }

        /// <summary>
        /// Adds an icon with a count beside it and a caption underneath to the header, x units from the left
        /// edge of the panel, and returns the count's text.
        /// </summary>
        private TextMeshProUGUI BuildCounter(float x, string icon, string caption)
        {
            Image image = NewImage(icon, _root, icon);
            PlaceTopLeft(image.rectTransform, x, 13f, new Vector2(IconSize, IconSize));

            TextMeshProUGUI count = NewText("Count", CountFontSize, s_countColor);
            PlaceTopLeft(count.rectTransform, x + IconSize + 4f, 9f, new Vector2(40f, 26f));

            TextMeshProUGUI label = NewText("Caption", CaptionFontSize, s_captionColor);
            PlaceTopLeft(label.rectTransform, x, 32f, new Vector2(70f, 14f));
            label.text = caption;
            label.characterSpacing = 4f;
            return count;
        }

        private TextMeshProUGUI NewText(string name, float size, Color color)
        {
            TextMeshProUGUI text = NewRect(name, _root).gameObject.AddComponent<TextMeshProUGUI>();
            if (_hud.m_hoverName != null)
            {
                text.font = _hud.m_hoverName.font;
            }
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private Marker GetMarker(int index)
        {
            while (_markers.Count <= index)
            {
                Marker marker = new Marker();
                marker.Oar = NewImage("Oar", _crew, "oar").rectTransform;
                marker.Oar.pivot = new Vector2(0f, 0.5f);
                marker.Ring = NewImage("Self", _crew, "self_ring").rectTransform;
                marker.Token = NewImage("Player", _crew, "token");
                marker.Helmet = NewImage("Helmet", _crew, "helmet").rectTransform;
                _markers.Add(marker);
            }

            Marker shown = _markers[index];
            shown.Token.gameObject.SetActive(true);
            shown.Helmet.gameObject.SetActive(true);
            return shown;
        }

        private static void HideMarker(Marker marker)
        {
            marker.Oar.gameObject.SetActive(false);
            marker.Ring.gameObject.SetActive(false);
            marker.Token.gameObject.SetActive(false);
            marker.Helmet.gameObject.SetActive(false);
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = size;
        }

        private static Image NewImage(string name, Transform parent, string sprite)
        {
            Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
            if (sprite != null)
            {
                image.sprite = CrewHudArt.Get(sprite);
            }
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return rect;
        }
    }
}
