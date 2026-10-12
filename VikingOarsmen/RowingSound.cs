using System.Collections.Generic;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// The sound of the oars, once per stroke: a lone rower's stroke (single / single2, picked at random), or
    /// the whole crew's (multiple) when two or more row the ship. See Steering item 13.
    /// </summary>
    /// <remarks>
    /// One per ship rather than per rower, so a crew doesn't stack the same sound once per rower. Added to
    /// every ship on every client: it reads who rows from each player's ZDO and times the strokes from the
    /// network clock (RowingBeat), like the rowers' animation, so nothing is sent over the network.
    /// </remarks>
    internal class RowingSound : MonoBehaviour
    {
        // Point of the stroke clip (0..1, 0 = catch) the sound plays at: just after the blade goes into the
        // water and the pull starts.
        private const float SoundPhase = 0.1f;

        // Same for reverse, which plays the clip backwards: the blade goes in where the pull ends (about 0.5)
        // and the push runs back towards the catch.
        private const float BackSoundPhase = 0.45f;

        // Random pitch change per stroke, so the same sound doesn't feel mechanical.
        private const float PitchVariation = 0.05f;

        // Most times a lone rower's stroke may repeat the same sound in a row.
        private const int MaxRepeats = 2;

        // Full volume up to MinDistance metres from the rowers, fading out to silence at MaxDistance.
        private const float MinDistance = 4f;
        private const float MaxDistance = 40f;

        // Gears from fastest to slowest, so a tie for the crew's beat goes to the faster one.
        private static readonly RowingGear[] s_gearsByPace = { RowingGear.Full, RowingGear.Half, RowingGear.Slow, RowingGear.Back };

        private readonly List<Player> _rowers = new List<Player>();
        private readonly int[] _gearCounts = new int[s_gearsByPace.Length];

        private Ship _ship;
        private ZNetView _nview;
        private AudioSource _source;

        // Beat and pace of the last frame, to catch the moment the beat goes past the sound's point.
        private float _rate;
        private float _beat;

        private int _lastSingle = -1;
        private int _repeats;

        private void Awake()
        {
            _ship = GetComponent<Ship>();
            _nview = GetComponent<ZNetView>();

            GameObject holder = new GameObject("VikingOarsmen_RowingSound");
            holder.transform.SetParent(transform, false);
            _source = holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = MinDistance;
            _source.maxDistance = MaxDistance;
            _source.dopplerLevel = 0f;
        }

        private void Update()
        {
            if (!Plugin.RowingSound.Value || _nview == null || !_nview.IsValid() || !CollectRowers())
            {
                _rate = 0f;
                return;
            }

            RowingGear gear = _rowers.Count == 1 ? RowingController.GetRowingGear(_rowers[0]) : CrewGear();
            AnimationClip stroke = ModAssets.RowStroke(false);
            float rate = stroke != null ? RowingBeat.StrokeRate(gear, stroke.length) : 0f;
            if (rate <= 0f)
            {
                _rate = 0f;
                return;
            }

            float beat = RowingBeat.GetBeat(rate);

            // A new pace (gear shift, StrokeSpeed, someone joining) makes the beat jump: start counting
            // from here instead of playing a stroke at the wrong moment.
            if (!Mathf.Approximately(rate, _rate))
            {
                _rate = rate;
                _beat = beat;
                return;
            }

            // Reverse shows the clip at 1 - beat (see RowerAnimation).
            float target = gear == RowingGear.Back ? 1f - BackSoundPhase : SoundPhase;
            // Crossed when the target lies in (last beat, this beat]; a hitch longer than half a stroke skips it.
            float advanced = Mathf.Repeat(beat - _beat, 1f);
            float toTarget = Mathf.Repeat(target - _beat, 1f);
            bool crossed = advanced < 0.5f && toTarget > 0f && toTarget <= advanced;
            _beat = beat;
            if (crossed)
            {
                Play();
            }
        }

        /// <summary>
        /// Fills _rowers with everyone rowing this ship, the same way ShipRowing counts its thrust, and moves
        /// the sound to the middle of them. False when no one rows.
        /// </summary>
        private bool CollectRowers()
        {
            _rowers.Clear();
            ZDOID shipId = _nview.GetZDO().m_uid;
            Vector3 center = Vector3.zero;
            foreach (Player player in Player.GetAllPlayers())
            {
                // Neutral, or a gear suspended for lack of stamina, reads as Stop: holding the oar still.
                if (player != null && RowingController.GetRowingShip(player) == shipId && _ship.IsPlayerInBoat(player)
                    && RowingController.GetRowingGear(player) != RowingGear.Stop)
                {
                    _rowers.Add(player);
                    center += player.transform.position;
                }
            }
            if (_rowers.Count == 0)
            {
                return false;
            }
            _source.transform.position = center / _rowers.Count;
            return true;
        }

        /// <summary>
        /// The gear most of the crew rows in, whose beat the crew's sound follows. Ties go to the faster gear.
        /// </summary>
        private RowingGear CrewGear()
        {
            for (int i = 0; i < _gearCounts.Length; i++)
            {
                _gearCounts[i] = 0;
            }
            foreach (Player player in _rowers)
            {
                int index = System.Array.IndexOf(s_gearsByPace, RowingController.GetRowingGear(player));
                if (index >= 0)
                {
                    _gearCounts[index]++;
                }
            }

            int best = 0;
            for (int i = 1; i < _gearCounts.Length; i++)
            {
                if (_gearCounts[i] > _gearCounts[best])
                {
                    best = i;
                }
            }
            return s_gearsByPace[best];
        }

        private void Play()
        {
            AudioClip clip = _rowers.Count == 1 ? PickSingle() : ModSounds.Multiple;
            if (clip == null)
            {
                return;
            }

            // Resolved late: the game's prefabs may not be loaded yet when the ship wakes up.
            if (_source.outputAudioMixerGroup == null)
            {
                _source.outputAudioMixerGroup = ModSounds.SfxGroup;
            }
            _source.pitch = 1f + Random.Range(-PitchVariation, PitchVariation);
            _source.PlayOneShot(clip, Mathf.Clamp01(Plugin.RowingSoundVolume.Value));
        }

        /// <summary>
        /// A lone rower's stroke, at random, but never the same one more than MaxRepeats times in a row.
        /// </summary>
        private AudioClip PickSingle()
        {
            AudioClip[] clips = ModSounds.Single;
            if (clips.Length == 0)
            {
                return null;
            }

            int pick = Random.Range(0, clips.Length);
            if (pick == _lastSingle && _repeats >= MaxRepeats && clips.Length > 1)
            {
                pick = (pick + Random.Range(1, clips.Length)) % clips.Length;
            }
            _repeats = pick == _lastSingle ? _repeats + 1 : 1;
            _lastSingle = pick;
            return clips[pick];
        }
    }
}
