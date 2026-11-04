using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VikingOarsmen
{
    /// <summary>
    /// Plays the rowing clips (ModAssets.RowIdle / RowStroke, authored in Blender on the game's own
    /// skeleton) on a rower, over the game's own Animator.
    /// </summary>
    /// <remarks>
    /// A PlayableGraph with its own output on the player's Animator: at weight 1 it overrides the vanilla
    /// controller, which keeps running underneath untouched and takes over again as the weight fades out.
    /// The stroke's timing comes from the network clock, so the whole crew rows in step on every client
    /// without sending anything (Steering 1.3).
    /// </remarks>
    internal class RowerAnimation
    {
        // Seconds to blend onto/off the oar and between holding it still and rowing.
        private const float BlendTime = 0.3f;

        // Seconds a rower takes to fall back in step with the crew's beat after drifting off it.
        private const float SyncTime = 0.5f;

        // Most the catch-up may speed up or slow down the stroke, as a share of its pace. Without it a rower
        // far ahead of the beat (a gear shift lands anywhere in the cycle) is held still, or played backwards.
        private const float MaxCatchUp = 0.5f;

        // Seconds between the periodic lines of DebugRowingSync.
        private const float DebugInterval = 1f;

        private readonly Player _player;
        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _idle;
        private AnimationClipPlayable _stroke;

        // Overall weight over the vanilla animation, and the stroke's share against holding still.
        private float _weight;
        private float _strokeWeight;

        // Stroke cycle, 0..1 (0 = catch).
        private float _phase;

        // Which side's clips the graph was built with (see ModAssets.RowIdle).
        private bool _mirrored;

        // The stroke pace of the last frame (0 = not stroking), to tell when it changes.
        private float _rate;

        // How far the cycle was behind the crew's beat (-0.5..0.5) and how fast it ran, as a share of its pace,
        // on the last frame it advanced; only for DebugRowingSync.
        private float _gap;
        private float _speed = 1f;
        private float _debugTimer;

        internal RowerAnimation(Player player)
        {
            _player = player;
        }

        /// <summary>
        /// Call every frame from Update (before the Animator evaluates).
        /// </summary>
        /// <param name="mirrored">Rowing over the rower's right (port side) instead of their left.</param>
        internal void Update(bool rowing, RowingGear gear, bool mirrored)
        {
            AnimationClip idle = ModAssets.RowIdle(mirrored);
            AnimationClip stroke = ModAssets.RowStroke(mirrored);
            if (idle == null || stroke == null)
            {
                return;
            }

            // Changing seats to the other side swaps the clips (the graph is rebuilt below).
            if (_graph.IsValid() && mirrored != _mirrored)
            {
                DebugLog("seat side changed, rebuilding the graph");
                Destroy();
            }

            _weight = Mathf.MoveTowards(_weight, rowing ? 1f : 0f, Time.deltaTime / BlendTime);
            if (_weight <= 0f)
            {
                Destroy();
                return;
            }

            // The pace this rower strokes at now (0 in neutral, or when stamina withholds the stroke).
            float rate = RowingBeat.StrokeRate(gear, stroke.length);
            if (!_graph.IsValid())
            {
                if (!Create(idle, stroke, rate))
                {
                    return;
                }
                _mirrored = mirrored;
            }

            bool stroking = rowing && gear != RowingGear.Stop;
            _strokeWeight = Mathf.MoveTowards(_strokeWeight, stroking ? 1f : 0f, Time.deltaTime / BlendTime);

            // Starting to stroke (leaving neutral, or stamina back) lands on the beat at once: the cycle
            // stood still meanwhile, and the stroke is still fading in over the held oar.
            if (_rate <= 0f && rate > 0f)
            {
                _phase = RowingBeat.GetBeat(rate);
                DebugLog($"stroke starts in {gear}: phase set to the beat {_phase:F2}");
            }
            else if (rate > 0f && !Mathf.Approximately(rate, _rate))
            {
                DebugLog($"pace {_rate:F3} -> {rate:F3} ({gear}): {BeatGap(rate):F2} off the beat");
            }
            _rate = rate;

            // Back water by rowing the same stroke backwards.
            AdvancePhase(rate);
            float strokePhase = gear == RowingGear.Back ? 1f - _phase : _phase;

            _idle.SetTime(Mathf.Repeat(Time.time, idle.length));
            _stroke.SetTime(strokePhase * stroke.length);
            _mixer.SetInputWeight(0, 1f - _strokeWeight);
            _mixer.SetInputWeight(1, _strokeWeight);
            _output.SetWeight(_weight);

            if (Plugin.DebugRowingSync.Value)
            {
                _debugTimer -= Time.deltaTime;
                if (_debugTimer <= 0f)
                {
                    _debugTimer = DebugInterval;
                    DebugLog($"{gear} rate {rate:F3} phase {_phase:F2} beat {(rate > 0f ? RowingBeat.GetBeat(rate) : 0f):F2} " +
                        $"gap {_gap:F2} speed x{_speed:F2} weight {_weight:F2} stroke {_strokeWeight:F2} mirrored {_mirrored}");
                }
            }
        }

        internal void Destroy()
        {
            if (_graph.IsValid())
            {
                DebugLog("graph destroyed");
                _graph.Destroy();
            }
            _strokeWeight = 0f;
            _rate = 0f;
        }

        private bool Create(AnimationClip idle, AnimationClip stroke, float rate)
        {
            GameObject visual = _player.GetVisual();
            Animator animator = visual != null ? visual.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman)
            {
                return false;
            }

            _graph = PlayableGraph.Create("VikingOarsmen_Rowing");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _idle = AnimationClipPlayable.Create(_graph, idle);
            _stroke = AnimationClipPlayable.Create(_graph, stroke);
            // Time is set by hand every frame (beat), never advanced by the graph.
            _idle.SetSpeed(0);
            _stroke.SetSpeed(0);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_idle, 0, _mixer, 0);
            _graph.Connect(_stroke, 0, _mixer, 1);
            _output = AnimationPlayableOutput.Create(_graph, "Rowing", animator);
            _output.SetSourcePlayable(_mixer);
            _output.SetWeight(0f);
            _graph.Play();

            // On the beat of the gear actually rowed, not of a fixed one; in neutral there is no beat yet and
            // the stroke starts on it (see Update).
            _phase = rate > 0f ? RowingBeat.GetBeat(rate) : 0f;
            _rate = rate;
            DebugLog($"graph created, phase {_phase:F2}, pace {rate:F3}");
            return true;
        }

        /// <summary>
        /// Advances the stroke cycle frame by frame, while pulling it onto the crew's shared beat.
        /// </summary>
        private void AdvancePhase(float rate)
        {
            if (rate <= 0f)
            {
                return;
            }

            float before = _phase;
            float dt = Time.deltaTime;
            _phase = Mathf.Repeat(_phase + dt * rate, 1f);

            // Close the gap to the beat the short way around the cycle, within about SyncTime, but never by
            // more than MaxCatchUp of the pace: a rower ahead of the beat slows down, not stops or turns back.
            float gap = BeatGap(rate);
            float step = Mathf.Clamp(gap * Mathf.Clamp01(dt / SyncTime), -MaxCatchUp * rate * dt, MaxCatchUp * rate * dt);
            _phase = Mathf.Repeat(_phase + step, 1f);

            _gap = gap;
            if (dt > 0f)
            {
                _speed = (Mathf.Repeat(_phase - before + 0.5f, 1f) - 0.5f) / (dt * rate);
            }
        }

        /// <summary>
        /// How far the cycle is behind the beat at this pace, the short way around (-0.5..0.5; negative = ahead).
        /// </summary>
        private float BeatGap(float rate)
        {
            return Mathf.Repeat(RowingBeat.GetBeat(rate) - _phase + 0.5f, 1f) - 0.5f;
        }

        private void DebugLog(string message)
        {
            if (!Plugin.DebugRowingSync.Value)
            {
                return;
            }

            string who = _player != null ? _player.GetPlayerName() : "?";
            string where = _player == Player.m_localPlayer ? "local" : "remote";
            double net = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : -1;
            Plugin.Log.LogInfo($"[sync] {who} ({where}) t={net:F2}: {message}");
        }
    }
}
