using UnityEngine;

namespace VikingOarsmen
{
    /// <summary>
    /// Handles the local player's rowing input, stamina drain and the synced rowing state.
    /// </summary>
    /// <remarks>
    /// The rowing state is stored on the player's own ZDO (which the local player always owns) as
    /// the ZDOID of the ship being rowed. Every client reads it: the ship owner to apply thrust
    /// (<see cref="ShipRowing"/>) and everyone to draw the oar (<see cref="OarVisual"/>).
    /// </remarks>
    internal static class RowingController
    {
        // ZDO key holding the ZDOID of the ship the player is rowing (ZDOID.None when idle).
        private const string RowingShipKey = "VikingOarsmen_RowingShip";

        // Whether the local player is currently rowing.
        private static bool s_rowing;

        // Time accumulated since the last stamina drain tick.
        private static float s_staminaTimer;

        /// <summary>
        /// Called every frame by Plugin.Update().
        /// </summary>
        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                s_rowing = false;
                return;
            }

            // Rowing requires being aboard a ship, alive and not swimming.
            Ship ship = Ship.GetLocalShip();
            bool canRow = ship != null && !player.IsDead() && !player.IsSwimming();

            // Resolve what the player wants based on the configured input mode.
            bool inputAllowed = CanTakeGameplayInput();
            KeyCode key = Plugin.RowKey.Value;
            bool wantsToRow = s_rowing;
            if (Plugin.HoldToRow.Value)
            {
                wantsToRow = inputAllowed && Input.GetKey(key);
            }
            else if (inputAllowed && Input.GetKeyDown(key))
            {
                wantsToRow = !s_rowing;
            }

            // Leaving the ship, dying or falling in the water stops rowing.
            if (!canRow)
            {
                wantsToRow = false;
            }

            if (wantsToRow && !s_rowing)
            {
                StartRowing(player);
            }
            else if (!wantsToRow && s_rowing)
            {
                StopRowing(player);
            }

            if (s_rowing)
            {
                DrainStamina(player);
            }

            // Keep the synced state pointing at the current ship (or none).
            SetRowingShip(player, s_rowing ? ship : null);
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

        private static void StartRowing(Player player)
        {
            // Refuse to start without enough stamina for the next drain tick.
            float cost = Plugin.StaminaDrainAmount.Value;
            if (cost > 0f && !player.HaveStamina(cost))
            {
                FlashStaminaBar();
                return;
            }

            s_rowing = true;
            s_staminaTimer = 0f;

            if (Plugin.ShowMessage.Value)
            {
                player.Message(MessageHud.MessageType.Center, "Remando!");
            }
        }

        private static void StopRowing(Player player)
        {
            s_rowing = false;
        }

        private static void DrainStamina(Player player)
        {
            float cost = Plugin.StaminaDrainAmount.Value;
            if (cost <= 0f)
            {
                return;
            }

            // Charge one tick every StaminaDrainInterval seconds of rowing.
            s_staminaTimer += Time.deltaTime;
            if (s_staminaTimer < Plugin.StaminaDrainInterval.Value)
            {
                return;
            }
            s_staminaTimer = 0f;

            if (!player.HaveStamina(cost))
            {
                // Exhausted: stop rowing and warn the player.
                FlashStaminaBar();
                player.Message(MessageHud.MessageType.Center, "Cansado demais para remar!");
                StopRowing(player);
                return;
            }

            player.UseStamina(cost);
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
