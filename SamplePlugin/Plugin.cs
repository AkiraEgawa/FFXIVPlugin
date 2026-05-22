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
        
        // 1. Inject the Game Condition service
        [PluginService] public static ICondition Condition { get; private set; } = null!;

        private readonly string logFilePath;
        private readonly ConcurrentQueue<string> dataQueue = new();
        private int diagnosticCounter = 0;
        private bool wasInDuel = false;

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            logFilePath = Path.Combine(docsPath, "ffxiv_duel_telemetry.csv");

            EnsureFileHeader();

            Framework.Update += OnFrameworkUpdate;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            // 2. Check if the client is actively synchronized inside a PvP duty zone
            // BoundByDuty56 covers all active instances of instanced PvP engagements
            bool isInDuel = Condition[ConditionFlag.BoundByDuty56];

            // Handle session state changes for your tracking logs
            if (isInDuel && !wasInDuel)
            {
                PluginLog.Information("Telemetry Logger: Match start detected! Commencing frame logging.");
                dataQueue.Enqueue($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},MATCH_START,0,0,0,0,0,0,0");
            }
            else if (!isInDuel && wasInDuel)
            {
                PluginLog.Information("Telemetry Logger: Match concluded. Flushing session buffer to drive.");
                dataQueue.Enqueue($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},MATCH_END,0,0,0,0,0,0,0");
                FlushQueueToFile(); // Force an immediate drop to disk the second the match wraps up
                diagnosticCounter = 0;
            }

            wasInDuel = isInDuel;

            // 3. Early exit guard — if you aren't inside the instance, waste zero CPU cycles
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

            var target = player.TargetObject;
            if (target != null)
            {
                tX = target.Position.X;
                tZ = target.Position.Z;
                tRot = target.Rotation;

                distance = Math.Sqrt(Math.Pow(pX - tX, 2) + Math.Pow(pZ - tZ, 2));
                if (distance > 0.01)
                {
                    facingDelta = CalculateFacingAngle(pRot, pX, pZ, tX, tZ);
                }
            }

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string csvLine = $"{timestamp},{pX:F3},{pZ:F3},{pRot:F3},{tX:F3},{tZ:F3},{tRot:F3},{distance:F3},{facingDelta:F2}";
            
            dataQueue.Enqueue(csvLine);

            // Keep the memory queue tight during the match by flushing in short frame chunks
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
                if (!File.Exists(logFilePath))
                {
                    File.WriteAllText(logFilePath, "Timestamp,pX,pZ,pRot,tX,tZ,tRot,Distance,FacingDelta\n");
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
            FlushQueueToFile();
        }
    }
}