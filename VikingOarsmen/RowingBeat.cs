using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// The stroke's pace per gear and the crew's shared beat, taken from the network clock so every client
    /// agrees on it without sending anything (Steering 1.3). Used by the rowers' animation and the ship's
    /// rowing sound, so both keep the same time.
    /// </summary>
    internal static class RowingBeat
    {
        // Stroke pace per gear, relative to the stroke clip's own length (1 = one stroke per clip length).
        private const float SlowPace = 0.75f;
        private const float HalfPace = 1f;
        private const float FullPace = 1.3f;

        /// <summary>
        /// Strokes per second for a gear (0 in neutral). StrokeSpeed scales every gear.
        /// </summary>
        internal static float StrokeRate(RowingGear gear, float clipLength)
        {
            float pace;
            switch (gear)
            {
                case RowingGear.Slow:
                case RowingGear.Back:
                    pace = SlowPace;
                    break;
                case RowingGear.Half:
                    pace = HalfPace;
                    break;
                case RowingGear.Full:
                    pace = FullPace;
                    break;
                default:
                    return 0f;
            }
            return Mathf.Max(0f, Plugin.StrokeSpeed.Value) * pace / Mathf.Max(clipLength, 0.01f);
        }

        /// <summary>
        /// Point of the stroke cycle everyone in the same gear should be at, from the network clock.
        /// </summary>
        internal static float GetBeat(float rate)
        {
            double time = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;
            double strokes = time * rate;
            return (float)(strokes - System.Math.Floor(strokes));
        }
    }
}
