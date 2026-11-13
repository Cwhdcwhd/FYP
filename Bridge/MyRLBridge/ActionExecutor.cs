using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MyRLBridge.MyRLBridgeCode;

public static class ActionExecutor
{
    // MainFile calls this on the Godot main thread.
    public static void Execute(string json)
    {
        try
        {
            Report($"Received action: {json}");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "Action requires a string 'type'.");
            }

            var type = typeElement.GetString();

            switch (type)
            {
                case "end_turn":
                    EndTurn();
                    break;

                case "play_card":
                    PlayCard(root);
                    break;

                case "use_potion":
                    UsePotion(root);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unknown action type: {type}");
            }
        }
        catch (Exception ex)
        {
            Report(
                $"ACTION FAILED: {ex.GetBaseException().Message}");
        }
    }

    private static void PlayCard(JsonElement action)
    {
        var (combat, state, player) = GetReadyCombat();

        int cardIndex = ReadRequiredInt(action, "card_index");
        var hand = player.PlayerCombatState!.Hand.Cards;

        if (cardIndex < 0 || cardIndex >= hand.Count)
        {
            throw new InvalidOperationException(
                $"Invalid card_index {cardIndex}. " +
                $"Hand contains {hand.Count} cards.");
        }

        var card = hand[cardIndex];
        var target = ResolveTarget(action, state, player);

        if (combat.IsExecutingCardOrPotionEffect(player))
        {
            throw new InvalidOperationException(
                "A card or potion effect is still executing.");
        }

        if (!card.CanPlay())
        {
            throw new InvalidOperationException(
                $"Card cannot currently be played: {card.Title}");
        }

        if (target != null && !card.IsValidTarget(target))
        {
            throw new InvalidOperationException(
                $"Invalid target for {card.Title}. " +
                $"TargetType: {card.TargetType}");
        }

        // Submit through the game's manual-play method.
        // A null target remains null; ! only suppresses nullable warnings.
        bool accepted = card.TryManualPlay(target!);

        if (!accepted)
        {
            throw new InvalidOperationException(
                $"Manual play rejected: {card.Title}. " +
                $"TargetType: {card.TargetType}; " +
                $"target={DescribeTarget(target)}");
        }

        Report(
            $"CARD REQUEST ACCEPTED: {card.Title}; " +
            $"target={DescribeTarget(target)}");
    }

    private static void UsePotion(JsonElement action)
    {
        var (combat, state, player) = GetReadyCombat();

        int slot = ReadRequiredInt(action, "potion_slot");

        if (slot < 0 || slot >= player.PotionSlots.Count)
        {
            throw new InvalidOperationException(
                $"Invalid potion_slot {slot}. " +
                $"Player has {player.PotionSlots.Count} slots.");
        }

        var potion = player.PotionSlots[slot];

        if (potion == null)
        {
            throw new InvalidOperationException(
                $"Potion slot {slot} is empty.");
        }

        if (combat.IsExecutingCardOrPotionEffect(player))
        {
            throw new InvalidOperationException(
                "A card or potion effect is still executing.");
        }

        if (potion.IsQueued)
        {
            throw new InvalidOperationException(
                "This potion is already queued.");
        }

        if (!potion.PassesCustomUsabilityCheck)
        {
            throw new InvalidOperationException(
                "Potion failed its custom usability check.");
        }

        var target = ResolveTarget(action, state, player);

        if (!potion.IsValidTarget(target!))
        {
            throw new InvalidOperationException(
                $"Invalid target for {potion.GetType().Name}. " +
                $"TargetType: {potion.TargetType}; " +
                $"target={DescribeTarget(target)}");
        }

        // This requests use; it does not return a completion result.
        potion.EnqueueManualUse(target!);

        Report(
            $"POTION USE REQUESTED: slot={slot}; " +
            $"potion={potion.GetType().Name}; " +
            $"target={DescribeTarget(target)}");
    }

    // Supports an enemy index, an explicit self target, or no target.
    private static Creature? ResolveTarget(
        JsonElement action,
        CombatState state,
        Player player)
    {
        bool hasEnemyTarget =
            action.TryGetProperty(
                "target_index",
                out var indexElement) &&
            indexElement.ValueKind != JsonValueKind.Null;

        bool hasNamedTarget =
            action.TryGetProperty(
                "target",
                out var targetElement) &&
            targetElement.ValueKind != JsonValueKind.Null;

        if (hasEnemyTarget && hasNamedTarget)
        {
            throw new InvalidOperationException(
                "Use either 'target_index' or 'target', not both.");
        }

        if (hasEnemyTarget)
        {
            if (indexElement.ValueKind != JsonValueKind.Number ||
                !indexElement.TryGetInt32(out int index))
            {
                throw new InvalidOperationException(
                    "'target_index' must be an integer.");
            }

            if (index < 0 || index >= state.Enemies.Count)
            {
                throw new InvalidOperationException(
                    $"Invalid target_index {index}.");
            }

            var enemy = state.Enemies[index];

            if (!enemy.IsAlive || !enemy.IsHittable)
            {
                throw new InvalidOperationException(
                    "Selected enemy is not alive or hittable.");
            }

            return enemy;
        }

        if (hasNamedTarget)
        {
            if (targetElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    "'target' must be 'self' or 'none'.");
            }

            switch (targetElement.GetString())
            {
                case "self":
                    return player.Creature;

                case "none":
                    return null;

                default:
                    throw new InvalidOperationException(
                        "Supported target values: 'self', 'none'.");
            }
        }

        // No target fields supplied.
        return null;
    }

    private static (
        CombatManager Combat,
        CombatState State,
        Player Player) GetReadyCombat()
    {
        var combat = CombatManager.Instance;

        if (combat == null || !combat.IsInProgress)
        {
            throw new InvalidOperationException(
                "No active combat.");
        }

        var state = combat.DebugOnlyGetState();

        if (state == null)
        {
            throw new InvalidOperationException(
                "Combat state is unavailable.");
        }

        var player = state.Players.FirstOrDefault();

        if (player?.PlayerCombatState == null)
        {
            throw new InvalidOperationException(
                "Player combat state is unavailable.");
        }

        if (state.CurrentSide != CombatSide.Player ||
            player.PlayerCombatState.Phase.ToString() != "Play")
        {
            throw new InvalidOperationException(
                "Player is not in the Play phase.");
        }

        if (combat.IsPaused ||
            combat.PlayerActionsDisabled ||
            combat.IsOverOrEnding ||
            combat.IsPlayerReadyToEndTurn(player))
        {
            throw new InvalidOperationException(
                "Combat is not accepting player actions.");
        }

        return (combat, state, player);
    }

    // Preserves the EndTurn pathway you already tested.
    private static void EndTurn()
    {
        var combat = CombatManager.Instance;

        if (combat == null || !combat.IsInProgress)
        {
            throw new InvalidOperationException(
                "No active combat.");
        }

        var state = combat.DebugOnlyGetState();

        if (state == null)
        {
            throw new InvalidOperationException(
                "No combat state.");
        }

        var player = state.Players.FirstOrDefault();

        if (player == null)
        {
            throw new InvalidOperationException(
                "No player found.");
        }

        if (state.CurrentSide != CombatSide.Player)
        {
            throw new InvalidOperationException(
                "It is not the player's turn.");
        }

        combat.SetReadyToEndTurn(
            player,
            canBackOut: true,
            actionDuringEnemyTurn: () => Task.CompletedTask);

        Report("END TURN REQUEST SENT.");
    }

    private static int ReadRequiredInt(
        JsonElement action,
        string name)
    {
        if (!action.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out int value))
        {
            throw new InvalidOperationException(
                $"Action requires integer '{name}'.");
        }

        return value;
    }

    private static string DescribeTarget(Creature? target)
    {
        return target == null
            ? "<none>"
            : $"{target.Name} (CombatId={target.CombatId})";
    }

    // Saves the latest receipt, rejection, or submission message.
    // This is not an action-completion record.
    private static void Report(string message)
    {
        string text = $"{DateTime.Now:O} {message}";

        GD.Print(text);

        try
        {
            var directory =
                @"C:\Users\super\Documents\MyRLBridge";

            Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path.Combine(directory, "last_action_result.txt"),
                text);
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"Could not write action result: {ex.Message}");
        }
    }
}