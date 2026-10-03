using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Plays the oar's swings slower than the borrowed Battleaxe animations. Attack.m_speedFactor can't do
    /// this: it scales the character's movement during the attack, not the animation. The game drives
    /// swing speed through Animator.speed instead: animation events call CharacterAnimEvent.Speed, and
    /// CustomFixedUpdate resets it to 1 outside attacks. Only the owner scales it; ZSyncAnimation already
    /// syncs Animator.speed to the other clients.
    /// </summary>
    internal static class OarSwing
    {
        // Read by name, so a game update renaming them only turns the slowdown off instead of breaking the mod.
        private static readonly FieldInfo s_characterField = AccessTools.Field(typeof(CharacterAnimEvent), "m_character");
        private static readonly FieldInfo s_animatorField = AccessTools.Field(typeof(CharacterAnimEvent), "m_animator");

        // Animators already slowed during the current attack, so a fixed update doesn't compound it.
        private static readonly HashSet<CharacterAnimEvent> s_slowed = new HashSet<CharacterAnimEvent>();

        /// <summary>
        /// After CustomFixedUpdate: slows the animator once when an oar attack starts, forgets it when it ends.
        /// </summary>
        internal static void OnFixedUpdate(CharacterAnimEvent animEvent)
        {
            if (!TryGetOarAttackAnimator(animEvent, out Animator animator))
            {
                s_slowed.Remove(animEvent);
                return;
            }

            if (s_slowed.Add(animEvent))
            {
                animator.speed *= OarItem.SwingSpeed;
            }
        }

        /// <summary>
        /// After CharacterAnimEvent.Speed: an animation event set a new base speed mid-attack; scale it too.
        /// </summary>
        internal static void OnSpeedSet(CharacterAnimEvent animEvent)
        {
            if (s_slowed.Contains(animEvent) && TryGetOarAttackAnimator(animEvent, out Animator animator))
            {
                animator.speed *= OarItem.SwingSpeed;
            }
        }

        private static bool TryGetOarAttackAnimator(CharacterAnimEvent animEvent, out Animator animator)
        {
            animator = null;
            if (s_characterField == null || s_animatorField == null)
            {
                return false;
            }

            Humanoid character = s_characterField.GetValue(animEvent) as Humanoid;
            if (character == null || !character.IsOwner() || !character.InAttack() || !OarItem.IsOar(character.GetCurrentWeapon()))
            {
                return false;
            }

            animator = s_animatorField.GetValue(animEvent) as Animator;
            return animator != null;
        }
    }
}
