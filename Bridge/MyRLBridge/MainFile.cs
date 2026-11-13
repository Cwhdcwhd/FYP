using System;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace MyRLBridge.MyRLBridgeCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "MyRLBridge";

    private BridgeServer? server;
    private bool enemyIntentDiagnosticWritten;
    private double diagnosticRetryTimer;
    private bool actionApiDiagnosticWritten;

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();

        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(
            assembly);

        var harmony = new Harmony(ModId);
        harmony.PatchAll(assembly);

        if (Engine.GetMainLoop() is SceneTree tree)
        {
            var main = new MainFile();
            tree.Root.CallDeferred("add_child", main);

            Logger.Info("MyRLBridge node creation queued");
        }
        else
        {
            Logger.Error("Could not get Godot SceneTree");
        }
    }

    public override void _Ready()
    {
        server = new BridgeServer();
        server.Start();

        GD.Print("MyRLBridge loaded: hand, potions, enemy diagnostic");
    }

    public override void _Process(double delta)
    {
        try
        {
            var state = StateCapture.Capture();
            if (!actionApiDiagnosticWritten &&
                state.InCombat &&
                state.PlayerTurnPhase == "Play")
            {
                ApiProbe.DumpAll();
                actionApiDiagnosticWritten = true;
            }

            BridgeServer.LatestStateJson =
                JsonSerializer.Serialize(state);

            if (!state.InCombat)
            {
                enemyIntentDiagnosticWritten = false;
                diagnosticRetryTimer = 0;
            }
            else if (!enemyIntentDiagnosticWritten)
            {
                diagnosticRetryTimer += delta;

                if (diagnosticRetryTimer >= 1.0)
                {
                    diagnosticRetryTimer = 0;

                    try
                    {
                        enemyIntentDiagnosticWritten =
                            StateCapture.DumpEnemyIntentDiagnostics();
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr(
                            $"Enemy intent diagnostic failed: {ex}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"State capture failed: {ex}");
        }

        while (ActionQueue.TryDequeue(out var actionJson))
        {
            ActionExecutor.Execute(actionJson);
        }
    }
}