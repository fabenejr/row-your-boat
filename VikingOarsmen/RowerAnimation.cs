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
                Destroy();
            }

            _weight = Mathf.MoveTowards(_weight, rowing ? 1f : 0f, Time.deltaTime / BlendTime);
            if (_weight <= 0f)
            {
                Destroy();
                return;
            }
            if (!_graph.IsValid())
            {
                if (!Create(idle, stroke))
                {
                    return;
                }
                _mirrored = mirrored;
            }

            bool stroking = rowing && gear != RowingGear.Stop;
            _strokeWeight = Mathf.MoveTowards(_strokeWeight, stroking ? 1f : 0f, Time.deltaTime / BlendTime);

            // Back water by rowing the same stroke backwards.
            float rate = RowingBeat.StrokeRate(gear, stroke.length);
            AdvancePhase(rate);
            float strokePhase = gear == RowingGear.Back ? 1f - _phase : _phase;

            _idle.SetTime(Mathf.Repeat(Time.time, idle.length));
            _stroke.SetTime(strokePhase * stroke.length);
            _mixer.SetInputWeight(0, 1f - _strokeWeight);
            _mixer.SetInputWeight(1, _strokeWeight);
            _output.SetWeight(_weight);
        }

        internal void Destroy()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _strokeWeight = 0f;
        }

        private bool Create(AnimationClip idle, AnimationClip stroke)
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
            _phase = RowingBeat.GetBeat(RowingBeat.StrokeRate(RowingGear.Half, stroke.length));
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

            _phase = Mathf.Repeat(_phase + Time.deltaTime * rate, 1f);

            // Close the gap to the beat the short way around the cycle, within about SyncTime.
            float gap = Mathf.Repeat(RowingBeat.GetBeat(rate) - _phase + 0.5f, 1f) - 0.5f;
            _phase = Mathf.Repeat(_phase + gap * Mathf.Clamp01(Time.deltaTime / SyncTime), 1f);
        }
    }
}
