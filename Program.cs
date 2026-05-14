using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FocusFlow
{
    public class FocusSession
    {
        public DateTime Date { get; set; }
        public string Goal { get; set; }
        public int DurationMinutes { get; set; }
        public int BlockedCount { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("====================================================");
            Console.WriteLine("🚀 FOCUS-FLOW v5.3 | SHIELDED EDITION");
            Console.WriteLine("====================================================");
            Console.ResetColor();

            // 1. Mod Seçimi
            Console.WriteLine("\n[1] KARA LİSTE");
            Console.WriteLine("[2] BEYAZ LİSTE (Sert Mod)");
            Console.Write("\nSeçimin (1/2): ");
            string mode = Console.ReadLine();

            // 2. Hedef ve Uygulamalar
            Console.Write("\n🎯 Hedefin: ");
            string userGoal = Console.ReadLine() ?? "Odaklanma";

            Console.WriteLine("\n🚫 İzin verilecek/Yasaklanacak uygulamalar (Virgül ile):");
            string inputApps = Console.ReadLine() ?? "";
            List<string> appList = inputApps.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrEmpty(p)).ToList();

            // 3. Süre
            Console.Write("\n⏰ Dakika: ");
            if (!int.TryParse(Console.ReadLine(), out int focusMinutes)) focusMinutes = 25;

            DateTime endTime = DateTime.Now.AddMinutes(focusMinutes);
            int blockedCount = 0;

            // --- KRİTİK GÜVENLİK AYARLARI ---
            int myProcessId = Process.GetCurrentProcess().Id;
            
            // Koruma listesi (Sistem ve Geliştirme araçları)
            HashSet<string> protectionList = new HashSet<string> 
            { 
                "explorer", "taskmgr", "svchost", "runtimebroker", 
                "conhost", "cmd", "powershell", "code", "devenv", 
                "focusflow", "dotnet"
            };

            Console.Clear();
            Console.WriteLine("🛡️ KORUMA KALKANI AKTİF...");

            while (DateTime.Now < endTime)
            {
                TimeSpan remaining = endTime - DateTime.Now;
                Console.Title = $"⌛ {remaining.Minutes:D2}:{remaining.Seconds:D2} | 🎯 {userGoal}";

                Process[] allProcesses = Process.GetProcesses();

                foreach (Process p in allProcesses)
                {
                    try
                    {
                        // 1. Kural: Eğer işlem BEN isem dokunma.
                        if (p.Id == myProcessId) continue;

                        // 2. Kural: Eğer işlemi BEN başlattıysam dokunma.
                        // (Bazı .NET sürümleri alt süreç açabilir)
                        
                        string pName = p.ProcessName.ToLower();
                        bool shouldKill = false;

                        if (mode == "1") // Kara Liste
                        {
                            if (appList.Contains(pName)) shouldKill = true;
                        }
                        else // Beyaz Liste
                        {
                            // Eğer uygulama appList veya protectionList içinde DEĞİLSE
                            // VE görünür bir penceresi VARSA kapat.
                            bool isAllowed = appList.Contains(pName) || protectionList.Contains(pName);
                            
                            if (!isAllowed && !string.IsNullOrEmpty(p.MainWindowTitle))
                            {
                                shouldKill = true;
                            }
                        }

                        if (shouldKill)
                        {
                            p.Kill();
                            blockedCount++;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[!] Engellendi: {pName}");
                            Console.ResetColor();
                        }
                    }
                    catch { /* Yetki hataları */ }
                }
                Thread.Sleep(2500); // Kontrol aralığını biraz açtık (stabilite için)
            }

            SaveSession(userGoal, focusMinutes, blockedCount);
            Console.WriteLine("\n🏆 Tebrikler Erhan! Başardın.");
            Console.ReadKey();
        }

        static void SaveSession(string goal, int duration, int blocked)
        {
            try {
                var session = new FocusSession { Date = DateTime.Now, Goal = goal, DurationMinutes = duration, BlockedCount = blocked };
                string fileName = "stats.json";
                List<FocusSession> allSessions = File.Exists(fileName) 
                    ? JsonSerializer.Deserialize<List<FocusSession>>(File.ReadAllText(fileName)) 
                    : new List<FocusSession>();
                
                allSessions.Add(session);
                File.WriteAllText(fileName, JsonSerializer.Serialize(allSessions, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }
    }
}