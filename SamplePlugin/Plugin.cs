using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.IO;
using System.Collections.Concurrent;

namespace SamplePlugin
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "Monk Telemetry Logger";

        [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] public static ICondition Condition { get; private set; } = null!;
        
        // 1. Inject the Action Effect Notification service to catch packet bursts
        [PluginService] public static IActionEffectNotification ActionEffectNotification { get; private set; } = null!;

        private readonly string logFilePath;
        private readonly ConcurrentQueue<string> dataQueue = new();
        private int diagnosticCounter = 0;
        private bool wasInDuel = false;

        // Persistent tracker to hold the last validated action ID fired by the target
        private uint lastTargetActionId = 0;

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            logFilePath = Path.Combine(docsPath, "ffxiv_duel_telemetry.csv");

            EnsureFileHeader();

            Framework.Update += OnFrameworkUpdate;
            
            // 2. Subscribe to the raw server/client action notification pipeline
            ActionEffectNotification.ActionEffectEvent += OnActionEffectEvent;
        }

        private void OnActionEffectEvent(ActionEffectEventArgs args)
        {
            // Only process packet updates if you are actively locked inside a duel profile
            if (!Condition[ConditionFlag.BoundByDuty56]) return;

            var player = ObjectTable.LocalPlayer;
            if (player == null) return;

            // 3. Catch the packet if and ONLY if the source actor matches your target's unique ID
            var target = player.TargetObject;
            if (target != null && args.SourceId == target.GameObjectId)
            {
                // Update our tracker with the true game database Action ID
                lastTargetActionId = args.ActionId;
            }
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            bool isInDuel = Condition[ConditionFlag.InDuelingArea] && Condition[ConditionFlag.InCombat];

            if (isInDuel && !wasInDuel)
            {
                PluginLog.Information("Telemetry Logger: Match start detected! Commencing frame logging.");
                dataQueue.Enqueue($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},MATCH_START,0,0,0,0,0,0,0,0,0,0");
                lastTargetActionId = 0; // Reset tracking bounds
            }
            else if (!isInDuel && wasInDuel)
            {
                PluginLog.Information("Telemetry Logger: Match concluded. Flushing session buffer to drive.");
                dataQueue.Enqueue($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},MATCH_END,0,0,0,0,0,0,0,0,0,0");
                FlushQueueToFile();
                diagnosticCounter = 0;
            }

            wasInDuel = isInDuel;
            if (!isInDuel) return;

            diagnosticCounter++;

            var player = ObjectTable.LocalPlayer;
            if (player == null) return;

            float pX = player.Position.X;
            float pZ = player.Position.Z; 
            float pRot = player.Rotation;

            float tX = 0f, tZ = 0f, tRot = 0f;
            double distance = 0.0;
            double facingDelta = 0.0;
            
            uint tCurrentHP = 0;
            uint tMaxHP = 0;

            var target = player.TargetObject;
            
            if (target is IBattleChara battleTarget)
            {
                tX = battleTarget.Position.X;
                tZ = battleTarget.Position.Z;
                tRot = battleTarget.Rotation;

                distance = Math.Sqrt(Math.Pow(pX - tX, 2) + Math.Pow(pZ - tZ, 2));
                if (distance > 0.01)
                {
                    facingDelta = CalculateFacingAngle(pRot, pX, pZ, tX, tZ);
                }

                tCurrentHP = battleTarget.CurrentHp;
                tMaxHP = battleTarget.MaxHp;
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // 4. Incorporate the persistent action tracker into the log structure
            string csvLine = $"{timestamp},{pX:F3},{pZ:F3},{pRot:F3},{tX:F3},{tZ:F3},{tRot:F3},{distance:F3},{facingDelta:F2},{tCurrentHP},{tMaxHP},{lastTargetActionId}";
            
            dataQueue.Enqueue(csvLine);

            if (diagnosticCounter % 15 == 0)
            {
                FlushQueueToFile();
            }
        }

        private void FlushQueueToFile()
        {
            var linesToWrite = new System.Collections.Generic.List<string>();
            while (dataQueue.TryDequeue(out var line))
            {
                linesToWrite.Add(line);
            }

            if (linesToWrite.Count == 0) return;

            try
            {
                File.AppendAllLines(logFilePath, linesToWrite);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Telemetry Logger IO Error during flush: {ex.Message}");
            }
        }

        private void EnsureFileHeader()
        {
            try
            {
                if (File.Exists(logFilePath))
                {
                    var lines = File.ReadAllLines(logFilePath);
                    if (lines.Length > 0 && !lines[0].Contains("tLastFiredActionId"))
                    {
                        File.Delete(logFilePath); // Overwrite stale schemas cleanly
                    }
                }

                if (!File.Exists(logFilePath))
                {
                    File.WriteAllText(logFilePath, "Timestamp,pX,pZ,pRot,tX,tZ,tRot,Distance,FacingDelta,tCurrentHP,tMaxHP,tLastFiredActionId\n");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Telemetry Logger Header Error: {ex.Message}");
            }
        }

        private double CalculateFacingAngle(float rot, float ax, float az, float tx, float tz)
        {
            double dirX = Math.Sin(rot);
            double dirZ = Math.Cos(rot);
            double diffX = tx - ax;
            double diffZ = tz - az;
            double magn = Math.Sqrt(diffX * diffX + diffZ * diffZ);

            if (magn == 0) return 0;
            
            double dot = ((dirX * (diffX / magn)) + (dirZ * (diffZ / magn)));
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            
            return Math.Acos(dot) * (180.0 / Math.PI);
        }

        public void Dispose()
        {
            Framework.Update -= OnFrameworkUpdate;
            ActionEffectNotification.ActionEffectEvent -= OnActionEffectEvent;
            FlushQueueToFile();
        }
    }
}