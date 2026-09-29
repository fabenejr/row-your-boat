using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Poses a seated rower on top of the sitting animation: both hands hold the oar and follow it, and
    /// the torso reaches forward at the catch, pulls back through the drive and leans towards the oar.
    /// </summary>
    /// <remarks>
    /// Applied in LateUpdate, after the Animator has written this frame's pose, so it works the same on
    /// every client without touching the animator controller.
    /// </remarks>
    internal class RowerPose
    {
        // The top hand holds the shaft this far up the handle (share of its length), the other hand
        // HandSpacing below it but not under the fulcrum (meters).
        private const float TopGrip = 0.75f;
        private const float HandSpacing = 0.45f;
        private const float MinGrip = 0.05f;

        // The hand bone sits at the wrist, a bit short of the palm that wraps the shaft.
        private const float WristToPalm = 0.08f;

        // Torso motion (degrees): lean towards the bow at the catch and back at the finish, turn the oar-side
        // shoulder forward at the catch, and bend sideways towards the oar the whole time.
        private const float LeanAngle = 14f;
        private const float TwistAngle = 15f;
        private const float SideLean = 10f;

        // Share of the torso motion taken by the lower spine; the chest takes the rest.
        private const float SpineShare = 0.55f;

        // Seconds to blend from the sitting pose onto the oar.
        private const float BlendTime = 0.3f;

        private struct Arm
        {
            internal Transform Upper;
            internal Transform Lower;
            internal Transform Hand;
            internal float Outward; // -1 = left, +1 = right
        }

        /// <summary>
        /// A spine bone rotated on top of the animation.
        /// </summary>
        private class SwayBone
        {
            internal Transform Bone;

            // Local rotation the Animator wrote last frame, and ours on top of it.
            private Quaternion _animated;
            private Quaternion _written = new Quaternion(0f, 0f, 0f, 0f);

            internal void Rotate(Quaternion worldRotation)
            {
                // If the Animator skipped a frame (culled), the bone still holds our last rotation: undo it first.
                if (Bone.localRotation == _written)
                {
                    Bone.localRotation = _animated;
                }
                _animated = Bone.localRotation;
                Bone.rotation = worldRotation * Bone.rotation;
                _written = Bone.localRotation;
            }
        }

        private readonly Player _player;
        private Arm _left;
        private Arm _right;
        private readonly SwayBone _spine = new SwayBone();
        private readonly SwayBone _chest = new SwayBone();
        private bool _hasBones;

        // Where the hands hold the shaft (meters above the fulcrum).
        private float _topGrip;
        private float _lowerGrip;

        // Side of the rower the oar is on (+1 = their right).
        private float _side = 1f;
        private float _weight;

        internal RowerPose(Player player)
        {
            _player = player;
        }

        /// <summary>
        /// Starts blending in again (call when rowing starts).
        /// </summary>
        internal void Reset()
        {
            _weight = 0f;
        }

        /// <summary>
        /// Picks where the hands hold the oar and which side of the rower it is on.
        /// </summary>
        /// <param name="handleLength">Shaft length above the fulcrum (meters).</param>
        internal void Fit(OarRig oar, float handleLength)
        {
            _topGrip = handleLength * TopGrip;
            _lowerGrip = Mathf.Min(Mathf.Max(_topGrip - HandSpacing, MinGrip), _topGrip);

            // Like on a paddle, the hand on the far side from the oar holds the top.
            Transform body = _player.transform;
            _side = Vector3.Dot(oar.ShaftPoint(0f) - body.position, body.right) >= 0f ? 1f : -1f;
        }

        /// <summary>
        /// Moves the torso and puts both hands on the oar. Call after the oar is posed for this frame.
        /// </summary>
        /// <param name="sweep">Fore/aft swing of the oar: +1 at the catch (blade forward), -1 at the finish.</param>
        internal void Apply(OarRig oar, float sweep)
        {
            if (!ResolveBones())
            {
                return;
            }

            _weight = Mathf.MoveTowards(_weight, 1f, Time.deltaTime / BlendTime);
            Transform body = _player.transform;

            float swing = sweep * _weight;
            Quaternion torso = Quaternion.AngleAxis(-_side * SideLean * _weight, body.forward)
                * Quaternion.AngleAxis(-_side * TwistAngle * swing, body.up)
                * Quaternion.AngleAxis(LeanAngle * swing, body.right);
            if (_chest.Bone != null)
            {
                _spine.Rotate(Quaternion.Slerp(Quaternion.identity, torso, SpineShare));
                _chest.Rotate(Quaternion.Slerp(Quaternion.identity, torso, 1f - SpineShare));
            }
            else
            {
                _spine.Rotate(torso);
            }

            Vector3 top = oar.ShaftPoint(_topGrip);
            Vector3 lower = oar.ShaftPoint(_lowerGrip);
            bool leftOnTop = _side > 0f;
            SolveArm(_left, leftOnTop ? top : lower, body);
            SolveArm(_right, leftOnTop ? lower : top, body);
        }

        /// <summary>
        /// Two-bone IK: bends the elbow (down and out) so the palm lands on the grip.
        /// </summary>
        private void SolveArm(Arm arm, Vector3 grip, Transform body)
        {
            Vector3 shoulder = arm.Upper.position;
            Vector3 hand = arm.Hand.position;

            // Aim the wrist a palm's width short of the grip, then blend in from the animated pose.
            Vector3 target = grip + (shoulder - grip).normalized * WristToPalm;
            target = Vector3.Lerp(hand, target, _weight);

            float upperLength = Vector3.Distance(shoulder, arm.Lower.position);
            float lowerLength = Vector3.Distance(arm.Lower.position, hand);
            Vector3 toTarget = target - shoulder;
            if (toTarget.sqrMagnitude < 1e-6f || upperLength < 1e-3f || lowerLength < 1e-3f)
            {
                return;
            }

            // Out-of-reach grips are clamped: the arm stretches towards them instead of breaking.
            Vector3 direction = toTarget.normalized;
            float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);

            // Elbow on the circle around the shoulder-wrist line, on the side of the bend hint.
            float along = (reach * reach + upperLength * upperLength - lowerLength * lowerLength) / (2f * reach);
            float radius = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 bend = Vector3.ProjectOnPlane(-body.up + body.right * (arm.Outward * 0.5f), direction);
            if (bend.sqrMagnitude < 1e-6f)
            {
                bend = Vector3.ProjectOnPlane(-body.forward, direction);
            }
            Vector3 elbow = shoulder + direction * along + bend.normalized * radius;

            arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            Vector3 wrist = shoulder + direction * reach;
            arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Lower.position, wrist - arm.Lower.position) * arm.Lower.rotation;
        }

        /// <summary>
        /// Caches the humanoid bones; they only change if the player model is rebuilt.
        /// </summary>
        private bool ResolveBones()
        {
            if (_hasBones && _spine.Bone != null && _left.Hand != null && _right.Hand != null)
            {
                return true;
            }

            _hasBones = false;
            GameObject visual = _player.GetVisual();
            Animator animator = visual != null ? visual.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman)
            {
                return false;
            }

            _spine.Bone = animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest.Bone = animator.GetBoneTransform(HumanBodyBones.Chest);
            _left = GetArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -1f);
            _right = GetArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1f);
            _hasBones = _spine.Bone != null
                && _left.Upper != null && _left.Lower != null && _left.Hand != null
                && _right.Upper != null && _right.Lower != null && _right.Hand != null;
            return _hasBones;
        }

        private static Arm GetArm(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, float outward)
        {
            return new Arm
            {
                Upper = animator.GetBoneTransform(upper),
                Lower = animator.GetBoneTransform(lower),
                Hand = animator.GetBoneTransform(hand),
                Outward = outward,
            };
        }
    }
}
