using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ForpakLauncher.Core
{
    /// <summary>
    /// « Boost Velox » : optimisations faites par le launcher, sans aucun mod
    /// (comme Lunar / Dawn) : réglages Java, priorité, carte graphique, économie d'énergie Windows.
    /// </summary>
    public static class Boost
    {
        /// <summary>Arguments Java optimisés pour le jeu (moins de micro-lags et de pics de ping).</summary>
        public static List<string> JvmArgs(int ramMb)
        {
            return new List<string>
            {
                // Mémoire fixe : Java ne redimensionne plus le tas en pleine partie (= moins de saccades)
                $"-Xmx{ramMb}M",
                $"-Xms{ramMb}M",
                "-XX:+IgnoreUnrecognizedVMOptions",
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UnlockDiagnosticVMOptions",
                // Ramasse-miettes G1 réglé pour des pauses très courtes
                "-XX:+UseG1GC",
                "-XX:MaxGCPauseMillis=37",
                "-XX:G1NewSizePercent=23",
                "-XX:G1MaxNewSizePercent=50",
                "-XX:G1ReservePercent=20",
                "-XX:G1HeapRegionSize=16M",
                "-XX:G1MixedGCCountTarget=3",
                "-XX:G1HeapWastePercent=20",
                "-XX:InitiatingHeapOccupancyPercent=15",
                "-XX:SurvivorRatio=32",
                "-XX:MaxTenuringThreshold=1",
                "-XX:+ParallelRefProcEnabled",
                "-XX:+DisableExplicitGC",
                "-XX:+PerfDisableSharedMem",
                "-XX:+UseStringDeduplication",
                // Code Java compilé plus vite
                "-XX:ReservedCodeCacheSize=400M",
                // Connexion : IPv4 en priorité (évite des connexions lentes ou instables en IPv6)
                "-Djava.net.preferIPv4Stack=true",
                "-Djava.net.preferIPv4Addresses=true",
                "-Dsun.net.client.defaultConnectTimeout=8000",
            };
        }

        /// <summary>Force la carte graphique « haute performance » pour Java (utile surtout sur PC portable).</summary>
        public static void PreferHighPerformanceGpu(string javaPath)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
                foreach (var exe in new[] { javaPath,
                                            Path.Combine(Path.GetDirectoryName(javaPath) ?? "", "java.exe"),
                                            Path.Combine(Path.GetDirectoryName(javaPath) ?? "", "javaw.exe") })
                    if (File.Exists(exe)) key.SetValue(exe, "GpuPreference=2;");
            }
            catch (Exception ex) { Log.Write("Boost GPU : " + ex.Message); }
        }

        /// <summary>Appliqué au jeu une fois lancé : priorité plus haute et pas de bridage Windows.</summary>
        public static void ApplyToProcess(Process p)
        {
            try { p.PriorityClass = ProcessPriorityClass.AboveNormal; }
            catch (Exception ex) { Log.Write("Boost priorité : " + ex.Message); }

            try
            {
                // Windows 11 ralentit les programmes en arrière-plan (« Efficiency mode ») :
                // on le désactive pour Minecraft, sinon FPS et ping chutent quand tu changes de fenêtre.
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = 0x1 | 0x4,   // EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                    StateMask = 0              // 0 = jamais brider
                };
                SetProcessInformation(p.Handle, 4 /* ProcessPowerThrottling */, ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            }
            catch (Exception ex) { Log.Write("Boost énergie : " + ex.Message); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int infoClass,
            ref PROCESS_POWER_THROTTLING_STATE info, uint size);
    }
}
