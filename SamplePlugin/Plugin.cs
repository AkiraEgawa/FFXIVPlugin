using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
// Crucial: This namespace houses the game character interfaces in modern Dalamud
using Dalamud.Game.ClientState.Objects.Types; 
using Dalamud.Game.ClientState.Objects.SubKinds;
using System;
using System.IO;

public class TelemetryLoggerPlugin : IDalamudPlugin
{
    public string Name => "Monk Telemetry Logger";

    // Injecting the ObjectTable service where LocalPlayer now lives
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;

    private readonly string logFilePath;
    private bool isLogging = false;

    public TelemetryLoggerPlugin()
    {
        var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        logFilePath = Path.Combine(docsPath, "ffxiv_duel_telemetry.csv");

        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "Timestamp,pX,pZ,pRot,tX,tZ,tRot,Distance,FacingDelta\n");
        }

        Framework.Update += OnFrameworkUpdate;
        isLogging = true;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!isLogging) return;

        // FIXED: Pulling LocalPlayer directly from the ObjectTable service per API 14 specifications
        var player = ObjectTable.LocalPlayer;
        if (player == null) return;

        var target = player.TargetObject as IPlayerCharacter;
        if (target == null) return;

        float pX = player.Position.X;
        float pZ = player.Position.Z; 
        float pRot = player.Rotation;

        float tX = target.Position.X;
        float tZ = target.Position.Z;
        float tRot = target.Rotation;

        double distance = Math.Sqrt(Math.Pow(pX - tX, 2) + Math.Pow(pZ - tZ, 2));
        double facingDelta = CalculateFacingAngle(pRot, pX, pZ, tX, tZ);

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string csvLine = $"{timestamp},{pX:F3},{pZ:F3},{pRot:F3},{tX:F3},{tZ:F3},{tRot:F3},{distance:F3},{facingDelta:F2}\n";
        
        File.AppendAllText(logFilePath, csvLine);
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
    }
}