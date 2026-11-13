using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace MyRLBridge.MyRLBridgeCode;

// Reads combat data for the HTTP bridge.
// Does not execute actions or serve HTTP requests.
public static class StateCapture
{
    // Creates one observation for GET /state.
    public static BridgeState Capture()
    {
        // "menu" currently represents any situation without active combat.
        var state = new BridgeState
        {
            Phase = "menu",
            InCombat = false
        };

        var combat = CombatManager.Instance;

        if (combat == null || !combat.IsInProgress)
        {
            return state;
        }

        var combatState = combat.DebugOnlyGetState();

        if (combatState == null)
        {
            return state;
        }

        state.Phase = "combat";
        state.InCombat = true;
        state.Round = combatState.RoundNumber;
        state.CurrentSide = combatState.CurrentSide.ToString();

        // Current implementation selects the first player.
        var player = combatState.Players?.FirstOrDefault();

        if (player == null)
        {
            return state;
        }

        // Capture relic identities.
        foreach (var relic in player.Relics)
        {
            state.Relics.Add(new RelicState
            {
                ModelType = relic.GetType().Name
            });
        }

        // Capture potion slots, including empty slots.
        for (int slot = 0; slot < player.PotionSlots.Count; slot++)
        {
            var potion = player.PotionSlots[slot];

            state.Potions.Add(new PotionSlotState
            {
                Slot = slot,
                IsEmpty = potion == null,
                ModelType = potion?.GetType().Name
            });
        }

        var playerCombatState = player.PlayerCombatState;
        var playerCreature = player.Creature;

        // Capture player health, block, and active powers.
        if (playerCreature != null)
        {
            state.Player.Hp = playerCreature.CurrentHp;
            state.Player.MaxHp = playerCreature.MaxHp;
            state.Player.Block = playerCreature.Block;

            foreach (var power in playerCreature.Powers)
            {
                state.Player.Powers.Add(new PowerState
                {
                    ModelType = power.GetType().Name
                });
            }
        }

        // Capture turn information, energy, and card piles.
        if (playerCombatState != null)
        {
            state.Turn = playerCombatState.TurnNumber;

            state.PlayerTurnPhase =
                playerCombatState.Phase.ToString();

            // Active side only; not a complete action-readiness check.
            state.IsPlayerTurn =
                combatState.CurrentSide == CombatSide.Player;

            state.Player.Energy = playerCombatState.Energy;
            state.Player.MaxEnergy = playerCombatState.MaxEnergy;

            state.HandCount =
                playerCombatState.Hand.Cards.Count;

            state.DrawPileCount =
                playerCombatState.DrawPile.Cards.Count;

            state.DiscardPileCount =
                playerCombatState.DiscardPile.Cards.Count;

            state.ExhaustPileCount =
                playerCombatState.ExhaustPile.Cards.Count;

            var handCards = playerCombatState.Hand.Cards;

            // Hand indices describe current positions, not permanent IDs.
            for (int index = 0; index < handCards.Count; index++)
            {
                state.Hand.Add(new HandCardState
                {
                    Index = index,
                    ModelType = handCards[index].GetType().Name
                });
            }
        }

        int enemyIndex = 0;

        // Capture each enemy and its active powers.
        foreach (var enemy in
                 combatState.Enemies ?? Array.Empty<Creature>())
        {
            var enemyState = new EnemyState
            {
                Index = enemyIndex,
                CombatId = enemy.CombatId ?? 0,
                Name = enemy.Name ?? "",
                ModelType = enemy.Monster?.GetType().Name,

                // Attack flag only; no damage or hit count yet.
                IntendsToAttack = enemy.Monster?.IntendsToAttack,

                Hp = enemy.CurrentHp,
                MaxHp = enemy.MaxHp,
                Block = enemy.Block,
                IsAlive = enemy.IsAlive
            };

            foreach (var power in enemy.Powers)
            {
                enemyState.Powers.Add(new PowerState
                {
                    ModelType = power.GetType().Name
                });
            }

            state.Enemies.Add(enemyState);
            enemyIndex++;
        }

        // Preliminary action listing; does not execute EndTurn.
        if (state.IsPlayerTurn)
        {
            state.LegalActions.Add(new BridgeAction
            {
                Type = "end_turn"
            });
        }

        return state;
    }

    // Writes information about the already-selected enemy moves.
    // Returns false if combat is not ready so MainFile can retry.
    public static bool DumpEnemyIntentDiagnostics()
    {
        var combat = CombatManager.Instance;

        if (combat == null || !combat.IsInProgress)
        {
            return false;
        }

        var combatState = combat.DebugOnlyGetState();

        if (combatState == null)
        {
            return false;
        }

        var player = combatState.Players.FirstOrDefault();

        // Wait for the player's Play phase.
        if (player?.PlayerCombatState == null ||
            combatState.CurrentSide != CombatSide.Player ||
            player.PlayerCombatState.Phase.ToString() != "Play")
        {
            return false;
        }

        var sb = new StringBuilder();

        sb.AppendLine(
            "========== ENEMY NEXT MOVE DIAGNOSTIC ==========");

        sb.AppendLine($"Captured: {DateTime.Now:O}");
        sb.AppendLine($"Round: {combatState.RoundNumber}");

        int monsterCount = 0;

        foreach (var enemy in combatState.Enemies)
        {
            var monster = enemy.Monster;

            if (monster == null)
            {
                continue;
            }

            monsterCount++;

            sb.AppendLine();
            sb.AppendLine($"Enemy: {enemy.Name}");
            sb.AppendLine($"CombatId: {enemy.CombatId}");
            sb.AppendLine(
                $"Monster type: {monster.GetType().FullName}");
            sb.AppendLine(
                $"IntendsToAttack: {monster.IntendsToAttack}");

            var nextMove = monster.NextMove;

            if (nextMove == null)
            {
                sb.AppendLine("NextMove: <null>");
            }
            else
            {
                // Inspects NextMove, not the individual intent objects.
                DumpPublicMembers(nextMove, "NextMove", sb);
            }
        }

        if (monsterCount == 0)
        {
            return false;
        }

        WriteDiagnostic(
            "sts2_enemy_intent_dump.txt",
            sb.ToString());

        return true;
    }

    // Diagnostic for discovering the hand pile's API.
    public static void DumpPileDiagnostics()
    {
        var combat = CombatManager.Instance;

        if (combat == null || !combat.IsInProgress)
        {
            return;
        }

        var combatState = combat.DebugOnlyGetState();
        var player = combatState?.Players.FirstOrDefault();
        var hand = player?.PlayerCombatState?.Hand;

        if (hand == null)
        {
            return;
        }

        var sb = new StringBuilder();

        sb.AppendLine(
            "========== CARD PILE DIAGNOSTIC ==========");

        sb.AppendLine($"Captured: {DateTime.Now:O}");
        sb.AppendLine($"Hand card count: {hand.Cards.Count}");

        DumpPublicMembers(hand, "Hand pile", sb);

        WriteDiagnostic(
            "sts2_card_pile_dump.txt",
            sb.ToString());
    }

    // Reads public property values and lists method signatures.
    // Listed methods are not invoked.
    private static void DumpPublicMembers(
        object instance,
        string label,
        StringBuilder sb)
    {
        var type = instance.GetType();

        sb.AppendLine($"--- {label}: {type.FullName} ---");
        sb.AppendLine("--- PUBLIC PROPERTIES ---");

        foreach (var property in type.GetProperties(
                     BindingFlags.Public |
                     BindingFlags.Instance))
        {
            // Skip indexers and properties without getters.
            if (property.GetIndexParameters().Length != 0 ||
                property.GetMethod == null)
            {
                continue;
            }

            try
            {
                var value = property.GetValue(instance);

                sb.AppendLine(
                    $"{property.PropertyType.FullName} " +
                    $"{property.Name} = {value ?? "<null>"}");

                if (value != null)
                {
                    sb.AppendLine(
                        $"  Runtime type: {value.GetType().FullName}");
                }
            }
            catch (Exception ex)
            {
                // Continue even if one property getter fails.
                sb.AppendLine(
                    $"{property.Name} = <getter failed: " +
                    $"{ex.GetBaseException().Message}>");
            }
        }

        sb.AppendLine("--- PUBLIC METHODS ---");

        foreach (var method in type.GetMethods(
                     BindingFlags.Public |
                     BindingFlags.Instance))
        {
            if (method.IsSpecialName ||
                method.DeclaringType == typeof(object))
            {
                continue;
            }

            var parameters = string.Join(
                ", ",
                method.GetParameters().Select(parameter =>
                    $"{parameter.ParameterType.FullName ?? parameter.ParameterType.Name} " +
                    $"{parameter.Name}"));

            sb.AppendLine(
                $"{method.ReturnType.FullName ?? method.ReturnType.Name} " +
                $"{method.Name}({parameters})");
        }
    }

    // Replaces the existing diagnostic file with the new text.
    private static void WriteDiagnostic(
        string fileName,
        string text)
    {
        var directory =
            @"C:\Users\super\Documents\MyRLBridge";

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);

        File.WriteAllText(path, text);

        GD.Print($"MyRLBridge diagnostic written to: {path}");
    }
}

// Top-level JSON observation.
public sealed class BridgeState
{
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "menu";

    [JsonPropertyName("in_combat")]
    public bool InCombat { get; set; }

    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    [JsonPropertyName("current_side")]
    public string CurrentSide { get; set; } = "";

    [JsonPropertyName("is_player_turn")]
    public bool IsPlayerTurn { get; set; }

    [JsonPropertyName("player_turn_phase")]
    public string PlayerTurnPhase { get; set; } = "";

    [JsonPropertyName("player")]
    public PlayerState Player { get; set; } = new();

    [JsonPropertyName("relics")]
    public List<RelicState> Relics { get; set; } = new();

    [JsonPropertyName("hand")]
    public List<HandCardState> Hand { get; set; } = new();

    [JsonPropertyName("potions")]
    public List<PotionSlotState> Potions { get; set; } = new();

    [JsonPropertyName("hand_count")]
    public int HandCount { get; set; }

    [JsonPropertyName("draw_pile_count")]
    public int DrawPileCount { get; set; }

    [JsonPropertyName("discard_pile_count")]
    public int DiscardPileCount { get; set; }

    [JsonPropertyName("exhaust_pile_count")]
    public int ExhaustPileCount { get; set; }

    [JsonPropertyName("enemies")]
    public List<EnemyState> Enemies { get; set; } = new();

    [JsonPropertyName("legal_actions")]
    public List<BridgeAction> LegalActions { get; set; } = new();
}

// Player combat values and active powers.
public sealed class PlayerState
{
    [JsonPropertyName("hp")]
    public int Hp { get; set; }

    [JsonPropertyName("max_hp")]
    public int MaxHp { get; set; }

    [JsonPropertyName("block")]
    public int Block { get; set; }

    [JsonPropertyName("energy")]
    public int Energy { get; set; }

    [JsonPropertyName("max_energy")]
    public int MaxEnergy { get; set; }

    [JsonPropertyName("powers")]
    public List<PowerState> Powers { get; set; } = new();
}

// One card's current position and internal class name.
public sealed class HandCardState
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("model_type")]
    public string ModelType { get; set; } = "";
}

// One potion slot, including empty slots.
public sealed class PotionSlotState
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("is_empty")]
    public bool IsEmpty { get; set; }

    [JsonPropertyName("model_type")]
    public string? ModelType { get; set; }
}

// Relic identity only; counters and charges are not captured yet.
public sealed class RelicState
{
    [JsonPropertyName("model_type")]
    public string ModelType { get; set; } = "";
}

// Active buff/debuff identity only; amounts are not captured yet.
public sealed class PowerState
{
    [JsonPropertyName("model_type")]
    public string ModelType { get; set; } = "";
}

// One enemy's combat values and active powers.
public sealed class EnemyState
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("combat_id")]
    public uint CombatId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("model_type")]
    public string? ModelType { get; set; }

    [JsonPropertyName("intends_to_attack")]
    public bool? IntendsToAttack { get; set; }

    [JsonPropertyName("hp")]
    public int Hp { get; set; }

    [JsonPropertyName("max_hp")]
    public int MaxHp { get; set; }

    [JsonPropertyName("block")]
    public int Block { get; set; }

    [JsonPropertyName("is_alive")]
    public bool IsAlive { get; set; }

    [JsonPropertyName("powers")]
    public List<PowerState> Powers { get; set; } = new();
}

// Action description; execution happens in ActionExecutor.
public sealed class BridgeAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("card_index")]
    public int? CardIndex { get; set; }

    [JsonPropertyName("target_index")]
    public int? TargetIndex { get; set; }
}