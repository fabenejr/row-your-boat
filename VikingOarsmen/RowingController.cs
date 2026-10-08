using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Handles the local player's rowing input, stamina drain and the synced rowing state.
    /// </summary>
    /// <remarks>
    /// Sitting on a bench with the oar equipped activates rowing mode automatically, in neutral
    /// (RowingGear.Stop); W/S then step through the gears exactly like Ship's own rudder control
    /// (see VikingOarsmen-Documents/Plano-Marchas-e-Remo.md). Both the ship being rowed and the rower's
    /// own gear are stored on the player's own ZDO (which the local player always owns). Every client
    /// reads them: the ship owner to apply thrust (<see cref="ShipRowing"/>) and everyone to draw the
    /// oar (<see cref="OarVisual"/>).
    /// </remarks>
    internal static class RowingController
    {
        // ZDO key holding the ZDOID of the ship the player is rowing (ZDOID.None when idle).
        private const string RowingShipKey = "VikingOarsmen_RowingShip";

        // ZDO key holding the rower's own effective gear (RowingGear), synced the same way as the ship above.
        private const string RowingGearKey = "VikingOarsmen_Gear";

        // Chair.m_name of the ships' mast and prow, where the player holds on standing (benches are "$piece_stool").
        private const string HoldfastName = "$ship_holdfast";

        // Gear the player has selected with W/S, kept even while stamina suspends its thrust (see
        // ResolveEffectiveGear) so the boost resumes on its own once stamina allows, without re-pressing.
        private static RowingGear s_desiredGear = RowingGear.Stop;

        // Whether the current stroke is withheld for lack of stamina (effective gear forced to Stop).
        private static bool s_suspended;

        // Time accumulated since the last stamina check for the current gear.
        private static float s_staminaTimer;

        /// <summary>
        /// Called every frame by Plugin.Update().
        /// </summary>
        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                s_desiredGear = RowingGear.Stop;
                return;
            }

            Ship ship = Ship.GetLocalShip();
            bool onBench = ship != null && IsOnBench(player) && !player.IsDead();
            bool hasOar = HasOarEquipped(player);
            bool inputAllowed = CanTakeGameplayInput();

            if (!onBench || !hasOar)
            {
                // Hint why W/S does nothing, same spirit as the old "sit on a bench" message, but only
                // when the player is actually trying: seated, without the oar, pressing a rowing key.
                if (onBench && !hasOar && inputAllowed
                    && (ZInput.GetButtonDown("Forward") || ZInput.GetButtonDown("Backward")))
                {
                    player.Message(MessageHud.MessageType.Center, "Equip the oar to row.");
                }

                s_desiredGear = RowingGear.Stop;
                s_suspended = false;
                SetRowingGear(player, RowingGear.Stop);
                SetRowingShip(player, null);
                return;
            }

            // Exit the bench explicitly, same as leaving the helm: W/S only shift gear while rowing
            // (see IsRowingMode/the SetControls patch in ShipRowingPatch), so this is the way out.
            if (inputAllowed && ZInput.GetButtonDown("Use"))
            {
                player.AttachStop();
                return;
            }

            RowingGear previousGear = s_desiredGear;
            if (inputAllowed)
            {
                if (ZInput.GetButtonDown("Forward"))
                {
                    s_desiredGear = StepForward(s_desiredGear);
                }
                else if (ZInput.GetButtonDown("Backward"))
                {
                    s_desiredGear = StepBackward(s_desiredGear);
                }
            }

            bool gearChanged = s_desiredGear != previousGear;
            if (gearChanged)
            {
                Plugin.Log.LogDebug($"Gear {previousGear} -> {s_desiredGear}");
            }

            RowingGear effectiveGear = ResolveEffectiveGear(player, gearChanged);
            SetRowingGear(player, effectiveGear);
            SetRowingShip(player, ship);
        }

        /// <summary>
        /// Returns the ZDOID of the ship a player is rowing, or ZDOID.None.
        /// </summary>
        internal static ZDOID GetRowingShip(Player player)
        {
            ZNetView nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return ZDOID.None;
            }

            return nview.GetZDO().GetZDOID(RowingShipKey);
        }

        /// <summary>
        /// Returns the gear a player is currently rowing in (RowingGear.Stop/neutral when idle, or when
        /// the boost is momentarily suspended for lack of stamina).
        /// </summary>
        internal static RowingGear GetRowingGear(Player player)
        {
            ZNetView nview = player.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return RowingGear.Stop;
            }

            return (RowingGear)nview.GetZDO().GetInt(RowingGearKey, (int)RowingGear.Stop);
        }

        /// <summary>
        /// The gear the local player has selected with W/S, even while stamina withholds its thrust —
        /// what the gear indicator (GearHud) shows, same as the helm shows its setting, not its speed.
        /// </summary>
        internal static RowingGear GetSelectedGear()
        {
            return s_desiredGear;
        }

        /// <summary>
        /// Writes the rower's own gear to their ZDO, only when it changes (same discipline as SetRowingShip).
        /// </summary>
        private static void SetRowingGear(Player player, RowingGear gear)
        {
            ZNetView playerView = player.GetComponent<ZNetView>();
            if (playerView == null || !playerView.IsValid() || !playerView.IsOwner())
            {
                return;
            }

            ZDO zdo = playerView.GetZDO();
            if ((RowingGear)zdo.GetInt(RowingGearKey, (int)RowingGear.Stop) != gear)
            {
                zdo.Set(RowingGearKey, (int)gear);
            }
        }

        /// <summary>
        /// True while the local player is seated with the oar equipped — i.e. W/S shift gear instead of
        /// standing them up. Queried by the Player.SetControls patch in ShipRowingPatch, which is the
        /// only place that can stop the vanilla "any movement input stands you up" behavior (the helm
        /// avoids it by being a doodad controller instead; see the plan doc for why rowers aren't).
        /// </summary>
        internal static bool IsRowingMode(Player player)
        {
            Ship ship = Ship.GetLocalShip();
            return ship != null && !player.IsDead() && IsOnBench(player) && HasOarEquipped(player);
        }

        /// <summary>
        /// True while the player sits on one of the ship's benches. Taking the helm also attaches the
        /// player to the ship, but the helmsman is steering and can't row; holding on to the mast or the
        /// prow is a Chair too, but the player stands there.
        /// </summary>
        private static bool IsOnBench(Player player)
        {
            if (!player.IsAttachedToShip() || player.GetDoodadController() != null)
            {
                return false;
            }

            Transform seat = player.GetAttachPoint();
            Chair chair = seat != null ? seat.GetComponentInParent<Chair>() : null;
            return chair != null && chair.m_name != HoldfastName;
        }

        /// <summary>
        /// True while the player's current weapon is the oar (see OarItem) — required to row.
        /// </summary>
        private static bool HasOarEquipped(Player player)
        {
            return OarItem.IsOar(player.GetCurrentWeapon());
        }

        /// <summary>
        /// One degree-of-freedom step forward, mirroring Ship.RPC_Forward exactly (Stop→Slow→Half→Full,
        /// Back→Stop; no-op at Full) so W behaves just like steering the ship.
        /// </summary>
        private static RowingGear StepForward(RowingGear gear)
        {
            switch (gear)
            {
                case RowingGear.Stop: return RowingGear.Slow;
                case RowingGear.Slow: return RowingGear.Half;
                case RowingGear.Half: return RowingGear.Full;
                case RowingGear.Back: return RowingGear.Stop;
                default: return gear; // Full
            }
        }

        /// <summary>
        /// One degree-of-freedom step backward, mirroring Ship.RPC_Backward exactly.
        /// </summary>
        private static RowingGear StepBackward(RowingGear gear)
        {
            switch (gear)
            {
                case RowingGear.Stop: return RowingGear.Back;
                case RowingGear.Slow: return RowingGear.Stop;
                case RowingGear.Half: return RowingGear.Slow;
                case RowingGear.Full: return RowingGear.Half;
                default: return gear; // Back
            }
        }

        /// <summary>
        /// Weapon-swing style stamina check: each active gear (everything but Stop) costs
        /// StaminaDrainAmount at its own interval (StaminaDrainInterval*, Slow and Back share one). A
        /// failed check withholds thrust (returns Stop) without changing the player's selected gear,
        /// which resumes on its own the next time a check succeeds — no re-pressing W/S needed.
        /// </summary>
        private static RowingGear ResolveEffectiveGear(Player player, bool gearChanged)
        {
            if (s_desiredGear == RowingGear.Stop)
            {
                s_staminaTimer = 0f;
                s_suspended = false;
                return RowingGear.Stop;
            }

            float cost = Plugin.StaminaDrainAmount.Value;
            if (cost <= 0f)
            {
                return s_desiredGear;
            }

            if (gearChanged)
            {
                // Judge a freshly selected gear right away, same as the old StartRowing pre-check.
                s_staminaTimer = 0f;
                CheckStamina(player, cost);
            }
            else
            {
                s_staminaTimer += Time.deltaTime;
                if (s_staminaTimer >= GetInterval(s_desiredGear))
                {
                    s_staminaTimer = 0f;
                    CheckStamina(player, cost);
                }
            }

            return s_suspended ? RowingGear.Stop : s_desiredGear;
        }

        /// <summary>
        /// Spends a stroke's worth of stamina if available; otherwise suspends the boost. Only flashes
        /// the stamina bar on the transition into suspension, not on every retry while stuck.
        /// </summary>
        private static void CheckStamina(Player player, float cost)
        {
            if (player.HaveStamina(cost))
            {
                player.UseStamina(cost);
                s_suspended = false;
                return;
            }

            if (!s_suspended)
            {
                FlashStaminaBar();
            }
            s_suspended = true;
        }

        /// <summary>
        /// Seconds between stamina checks for a gear. Slow and Back share one value, so reverse costs the
        /// same stamina as gear 1; Half and Full check more often, draining faster overall at the same per-check cost.
        /// </summary>
        private static float GetInterval(RowingGear gear)
        {
            switch (gear)
            {
                case RowingGear.Half: return Plugin.StaminaDrainIntervalHalf.Value;
                case RowingGear.Full: return Plugin.StaminaDrainIntervalFull.Value;
                default: return Plugin.StaminaDrainIntervalSlow.Value; // Slow, Back
            }
        }

        /// <summary>
        /// Writes the rowing ship to the player's ZDO, only when it changes to avoid network spam.
        /// </summary>
        private static void SetRowingShip(Player player, Ship ship)
        {
            ZNetView playerView = player.GetComponent<ZNetView>();
            if (playerView == null || !playerView.IsValid() || !playerView.IsOwner())
            {
                return;
            }

            ZDOID shipId = ZDOID.None;
            if (ship != null)
            {
                ZNetView shipView = ship.GetComponent<ZNetView>();
                if (shipView != null && shipView.IsValid())
                {
                    shipId = shipView.GetZDO().m_uid;
                }
            }

            ZDO zdo = playerView.GetZDO();
            if (zdo.GetZDOID(RowingShipKey) != shipId)
            {
                zdo.Set(RowingShipKey, shipId);
            }
        }

        private static void FlashStaminaBar()
        {
            // Same feedback the game gives when stamina runs out.
            if (Hud.instance != null)
            {
                Hud.instance.StaminaBarEmptyFlash();
            }
        }

        /// <summary>
        /// Returns false while the player is typing in chat/console or has a menu open.
        /// </summary>
        private static bool CanTakeGameplayInput()
        {
            if (Chat.instance != null && Chat.instance.HasFocus()) return false; // Typing in chat
            if (global::Console.IsVisible()) return false;                       // Dev console open
            if (Menu.IsVisible()) return false;                                  // Pause menu
            if (InventoryGui.IsVisible()) return false;                          // Inventory/crafting
            if (TextInput.IsVisible()) return false;                             // Sign/text input dialog
            if (Minimap.IsOpen()) return false;                                  // Large map
            if (StoreGui.IsVisible()) return false;                              // Trader window
            return true;
        }
    }
}
