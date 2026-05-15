using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace FocusFlow
{
    public class AppSettings
    {
        public List<string> Blacklist     { get; set; } = new();
        public int    DefaultDuration     { get; set; } = 25;
        public int    BreakDuration       { get; set; } = 5;
        public string DefaultGoal         { get; set; } = "TYT-AYT Odaklanma";
        public bool   AllowEscape         { get; set; } = true;
        public bool   ShowSeconds         { get; set; } = true;
        public bool   PomodoroMode        { get; set; } = false;
        public int    PomodoroRounds      { get; set; } = 4;
        public bool   OpenDashboard       { get; set; } = true;
    }

    public class FocusSession
    {
        public DateTime Date              { get; set; }
        public string   Goal              { get; set; } = "";
        public int      DurationMinutes   { get; set; }
        public int      BlockedCount      { get; set; }
        public bool     Completed         { get; set; }
    }

    class Program
    {
        private const string AppVersion      = "7.5.0";
        private const string LogFile         = "focus_flow.log";
        private const string SettingsFile    = "settings.json";
        private const string StatsFile       = "stats.json";
        private const string StatsTempFile   = "stats.json.tmp"; // FIX #1: atomik yazma için
        private const string DashboardFile   = "dashboard.html";
        private const long   MaxLogSize      = 5 * 1024 * 1024;
        private const int    MinTerminalWidth = 50; // FIX #7: minimum pencere genişliği

        private static readonly object _logLock = new object();
        // Log boyutu cache: -2 = henüz initialize edilmedi (ilk Log() çağrısında okunur)
        private static long _logSizeCache    = -2;
        private static int  _logCheckCounter =  0;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtResumeProcess(IntPtr processHandle);

        private static readonly HashSet<string> _protectedProcesses =
            new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer","lsass","csrss","services","smss","wininit","winlogon",
            "svchost","System","Registry","Memory Compression","dwm","fontdrvhost",
            "sihost","taskhostw","runtimebroker","searchindexer","ctfmon",
            "conhost","cmd","powershell","pwsh","devenv","code","FocusFlow"
        };

        private static readonly bool _isWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static bool _isAdmin = false;

        private static volatile bool _paused      = false;
        private static volatile bool _exitSession = false;

        // Satır koordinatları
        private const int FrameTop    = 0;
        private const int GoalLine    = 1;
        private const int DivLine_NP  = 2;
        private const int DivLine_P   = 3;
        private const int TimerLine_NP = 4;
        private const int TimerLine_P  = 5;
        private const int BarLine_NP   = 5;
        private const int BarLine_P    = 6;
        private const int CloseLine_NP = 7;
        private const int CloseLine_P  = 8;
        private const int StatusLine_NP= 9;
        private const int StatusLine_P = 10;
        private const int MsgLine_NP   = 11;
        private const int MsgLine_P    = 12;

        // FIX #2: Emoji genişliği sabitleri
        private const int EmojiWidth = 2;   // Terminal'de çift genişlik emoji
        private const int FrameInner = 44;  // ╔ ve ╗ arasındaki içerik genişliği

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Clear();

            if (_isWindows)
            {
                _isAdmin = CheckAdministrator();
                if (!_isAdmin)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("⚠️  YÖNETİCİ YETKİSİ YOK");
                    Console.WriteLine("   Uygulamaları dondurma özelliği devre dışı.");
                    Console.WriteLine("   Tam işlevsellik için 'Yönetici olarak çalıştır'.");
                    Console.ResetColor();
                    Thread.Sleep(2500);
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️  Bu platform tam desteklenmez (yalnızca Windows).");
                Console.WriteLine("   Dondurma ve ses özellikleri devre dışı.");
                Console.ResetColor();
                Thread.Sleep(2000);
            }

            AppSettings settings = LoadSettings();

            bool exit = false;
            while (!exit)
            {
                ShowBanner();
                DisplayMainMenu(settings);

                // FIX #8: Hızlı tuş basımlarından biriken buffer'ı temizle
                // Aksi halde seans/ayar bitiminden kalan tuşlar istem dışı işlenir
                while (Console.KeyAvailable) Console.ReadKey(true);

                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.Enter: RunFocusSession(settings); break;
                    case ConsoleKey.G:     QuickGoalChange(settings); break;
                    case ConsoleKey.S:     UpdateSettings(settings);  break;
                    case ConsoleKey.H:     ShowStats();                break;
                    case ConsoleKey.Q:     exit = true;                break;
                }
            }

            Console.WriteLine("\n👋 Görüşürüz!");
        }

#pragma warning disable CA1416
        static bool CheckAdministrator()
        {
            if (!_isWindows) return false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
#pragma warning restore CA1416

        static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(SettingsFile, Encoding.UTF8));
                    if (loaded != null)
                    {
                        // FIX #3: String alanları null olabilir (JSON'da null yazılmışsa)
                        if (string.IsNullOrWhiteSpace(loaded.DefaultGoal))
                            loaded.DefaultGoal = "TYT-AYT Odaklanma";

                        if (loaded.BreakDuration  <= 0) loaded.BreakDuration  = 5;
                        if (loaded.PomodoroRounds <= 0) loaded.PomodoroRounds = 4;
                        if (loaded.Blacklist == null)   loaded.Blacklist = new List<string>();

                        // Null entry'leri blacklist'ten temizle
                        loaded.Blacklist.RemoveAll(s => string.IsNullOrWhiteSpace(s));

                        loaded.DefaultDuration = Math.Clamp(loaded.DefaultDuration, 1, 180);
                        loaded.BreakDuration   = Math.Clamp(loaded.BreakDuration,   1,  60);
                        loaded.PomodoroRounds  = Math.Clamp(loaded.PomodoroRounds,  1,  20);
                        return loaded;
                    }
                }
            }
            catch (Exception ex) { Log($"Ayar yükleme hatası: {ex.Message}"); }

            // Bozuk/eksik settings durumunu kullanıcıya bildir
            var defaults = CreateDefaultSettings();
            bool saved = SaveSettingsToFile(defaults);
            if (!saved)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️  Ayar dosyası okunamadı/yazılamadı.");
                Console.WriteLine("   Varsayılan ayarlarla devam ediliyor.");
                Console.ResetColor();
                Thread.Sleep(2000);
            }
            return defaults;
        }

        static AppSettings CreateDefaultSettings() => new AppSettings
        {
            Blacklist       = new List<string> { "discord", "spotify", "steam" },
            DefaultDuration = 40,
            DefaultGoal     = "TYT-AYT Odaklanma",
            AllowEscape     = true,
            ShowSeconds     = true,
            BreakDuration   = 5,
            PomodoroMode    = false,
            PomodoroRounds  = 4,
            OpenDashboard   = true
        };

        // FIX #9: bool dönüş değeri — kaydetme başarısı bildiriliyor
        static bool SaveSettingsToFile(AppSettings settings)
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(SettingsFile,
                    JsonSerializer.Serialize(settings, opts), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Ayar kaydetme hatası: {ex.Message}");
                return false;
            }
        }

        static void DisplayMainMenu(AppSettings settings)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n" + new string('═', 54));
            Console.WriteLine($"   FOCUS-FLOW v{AppVersion} | {settings.DefaultGoal.ToUpper()}");
            Console.WriteLine(new string('═', 54));
            Console.ResetColor();

            Console.WriteLine($"\n🎯 Hedef    : {settings.DefaultGoal}");
            Console.WriteLine($"⏰ Süre     : {settings.DefaultDuration} dk  |  Mola: {settings.BreakDuration} dk");
            Console.WriteLine($"🔄 Pomodoro : {(settings.PomodoroMode
                               ? $"Açık ({settings.PomodoroRounds} tur)" : "Kapalı")}");
            Console.WriteLine($"🛡️  ESC      : {(settings.AllowEscape ? "Açık" : "KAPALI")}");
            Console.WriteLine($"📊 Dashboard: {(settings.OpenDashboard ? "Otomatik aç" : "Manuel")}");
            Console.WriteLine($"🚫 Yasaklı  : {(settings.Blacklist.Count > 0
                               ? string.Join(", ", settings.Blacklist) : "(boş)")}");

            if (_isWindows && !_isAdmin)
                Console.WriteLine("\n⚠️  Yönetici yetkisi yok — dondurma devre dışı");

            Console.WriteLine("\n[ENTER] Başlat | [G] Hızlı Hedef | [S] Ayarlar | [H] İstatistik | [Q] Çıkış");
        }

        // FIX #7 + Çift çizim düzeltmesi:
        // Return sonrası ana while döngüsü ShowBanner()+DisplayMainMenu() çağırıyor.
        // Burada tekrar çizmek gereksiz flash üretiyor — sadece onay mesajı gösterip dönüyoruz.
        static void QuickGoalChange(AppSettings settings)
        {
            Console.Write($"\n🎯 Yeni hedef ({settings.DefaultGoal}): ");
            string? g = ReadLineWithEscape();
            if (g != null && !string.IsNullOrWhiteSpace(g))
            {
                settings.DefaultGoal = g.Trim();
                SaveSettingsToFile(settings);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Hedef güncellendi: {settings.DefaultGoal}");
                Console.ResetColor();
                Thread.Sleep(900);
                // Ana döngü dönünce ShowBanner()+DisplayMainMenu() yeni hedefle çizilecek.
                // Burada tekrar çizmiyoruz — çift flash yok.
            }
        }

        static void UpdateSettings(AppSettings settings)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n══════ AYAR GÜNCELLEME ══════");
            Console.WriteLine("(Boş bırakırsanız mevcut değer korunur)");
            Console.WriteLine("[ESC] Değişiklikleri iptal et\n");
            Console.ResetColor();

            // Orijinal ayarları temiz kopya olarak sakla
            var opts = new JsonSerializerOptions();
            var originalSettings = JsonSerializer.Deserialize<AppSettings>(
                JsonSerializer.Serialize(settings, opts))!;
            bool cancelled = false;

            Console.Write($"📝 Hedef ({settings.DefaultGoal}): ");
            string? g = ReadLineWithEscape();
            if (g == null) { cancelled = true; }
            else if (!string.IsNullOrWhiteSpace(g)) settings.DefaultGoal = g.Trim();

            if (!cancelled)
            {
                Console.Write($"⏱️  Odak süresi dk ({settings.DefaultDuration}): ");
                string? durStr = ReadLineWithEscape();
                if (durStr == null) { cancelled = true; }
                else if (int.TryParse(durStr, out int dur) && dur > 0 && dur <= 180)
                    settings.DefaultDuration = dur;
            }

            if (!cancelled)
            {
                Console.Write($"☕ Mola süresi dk ({settings.BreakDuration}): ");
                string? brkStr = ReadLineWithEscape();
                if (brkStr == null) { cancelled = true; }
                else if (int.TryParse(brkStr, out int brk) && brk > 0 && brk <= 60)
                    settings.BreakDuration = brk;
            }

            if (!cancelled)
            {
                Console.Write($"🔄 Pomodoro modu (e/h) ({(settings.PomodoroMode ? "e" : "h")}): ");
                string? pom = ReadLineWithEscape()?.ToLower();
                if (pom == null) { cancelled = true; }
                else if (pom == "e" || pom == "h") settings.PomodoroMode = pom == "e";
            }

            if (!cancelled && settings.PomodoroMode)
            {
                Console.Write($"🔁 Tur sayısı ({settings.PomodoroRounds}): ");
                string? rndStr = ReadLineWithEscape();
                if (rndStr == null) { cancelled = true; }
                else if (int.TryParse(rndStr, out int r) && r > 0 && r <= 20)
                    settings.PomodoroRounds = r;
            }

            if (!cancelled)
            {
                Console.Write($"🛡️  ESC aktif (e/h) ({(settings.AllowEscape ? "e" : "h")}): ");
                string? esc = ReadLineWithEscape()?.ToLower();
                if (esc == null) { cancelled = true; }
                else if (esc == "e" || esc == "h") settings.AllowEscape = esc == "e";
            }

            if (!cancelled)
            {
                Console.Write($"⏱️  Saniye göster (e/h) ({(settings.ShowSeconds ? "e" : "h")}): ");
                string? sec = ReadLineWithEscape()?.ToLower();
                if (sec == null) { cancelled = true; }
                else if (sec == "e" || sec == "h") settings.ShowSeconds = sec == "e";
            }

            if (!cancelled)
            {
                Console.Write($"📊 Seans sonrası dashboard aç (e/h) ({(settings.OpenDashboard ? "e" : "h")}): ");
                string? dash = ReadLineWithEscape()?.ToLower();
                if (dash == null) { cancelled = true; }
                else if (dash == "e" || dash == "h") settings.OpenDashboard = dash == "e";
            }

            if (!cancelled)
            {
                Console.WriteLine($"\n🚫 Mevcut yasaklı: {string.Join(", ", settings.Blacklist)}");
                Console.WriteLine("   [A] Ekle  |  [D] Çıkar  |  [Enter] Geç  |  [ESC] İptal");
                var blKey = Console.ReadKey(true).Key;

                if (blKey == ConsoleKey.Escape)
                {
                    cancelled = true;
                }
                else if (blKey == ConsoleKey.A)
                {
                    Console.Write("Eklenecek uygulama (örn: chrome): ");
                    string? app = ReadLineWithEscape()?.ToLower().Trim();
                    if (app == null) { cancelled = true; }
                    else if (!string.IsNullOrWhiteSpace(app) && !settings.Blacklist.Contains(app))
                    {
                        if (_protectedProcesses.Contains(app))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ '{app}' kritik sistem uygulaması, eklenemez.");
                            Console.ResetColor();
                            Thread.Sleep(1500);
                        }
                        else
                        {
                            settings.Blacklist.Add(app);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"✅ '{app}' eklendi.");
                            Console.ResetColor();
                        }
                    }
                    else Console.WriteLine("Zaten listede veya geçersiz giriş.");
                }
                else if (blKey == ConsoleKey.D)
                {
                    Console.Write("Çıkarılacak uygulama: ");
                    string? app = ReadLineWithEscape()?.ToLower().Trim();
                    if (app == null) { cancelled = true; }
                    else if (!string.IsNullOrWhiteSpace(app) && settings.Blacklist.Remove(app))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ '{app}' çıkarıldı.");
                        Console.ResetColor();
                    }
                    else Console.WriteLine("Listede bulunamadı.");
                }
            }

            if (cancelled)
            {
                settings.DefaultGoal     = originalSettings.DefaultGoal;
                settings.DefaultDuration = originalSettings.DefaultDuration;
                settings.BreakDuration   = originalSettings.BreakDuration;
                settings.PomodoroMode    = originalSettings.PomodoroMode;
                settings.PomodoroRounds  = originalSettings.PomodoroRounds;
                settings.AllowEscape     = originalSettings.AllowEscape;
                settings.ShowSeconds     = originalSettings.ShowSeconds;
                settings.OpenDashboard   = originalSettings.OpenDashboard;
                settings.Blacklist       = originalSettings.Blacklist;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n❌ Değişiklikler iptal edildi.");
                Console.ResetColor();
                Thread.Sleep(1200);
                return;
            }

            SaveSettingsToFile(settings);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ Ayarlar kaydedildi.");
            Console.ResetColor();
            Thread.Sleep(1200);
        }

        static string? ReadLineWithEscape()
        {
            var input = new StringBuilder();
            // Prompt'un başladığı sütunu hatırlayarak satırı yeniden çizmek için
            int promptLeft = Console.CursorLeft;
            int promptTop  = Console.CursorTop;

            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return input.ToString();
                }
                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine(" [İPTAL]");
                    return null;
                }
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);

                    // FIX #5: \b \b yerine satırı baştan yeniden çiz
                    // Türkçe karakterler (ğ, ş, ı) bazı terminallerde çift glyph bırakabilir
                    try
                    {
                        Console.SetCursorPosition(promptLeft, promptTop);
                        string redrawn = input.ToString();
                        Console.Write(redrawn + " "); // son karakterin üstüne boşluk bas
                        Console.SetCursorPosition(promptLeft + redrawn.Length, promptTop);
                    }
                    catch
                    {
                        // Cursor pozisyonu alınamazsa eski yönteme dön
                        Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
        }

        static void RunFocusSession(AppSettings settings)
        {
            int rounds = settings.PomodoroMode ? settings.PomodoroRounds : 1;
            var allSuspendedIds = new HashSet<int>();

            for (int round = 1; round <= rounds; round++)
            {
                if (settings.PomodoroMode)
                {
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n🍅 TUR {round}/{rounds} başlıyor...");
                    Console.ResetColor();
                    Thread.Sleep(1400);
                }

                bool completed = ExecuteSession(settings, round, rounds, allSuspendedIds);
                if (!completed) break;

                if (settings.PomodoroMode && round < rounds)
                {
                    ResumeAll(allSuspendedIds);

                    // FIX #3: Mola öncesi _paused sıfırlanıyor
                    _paused = false;

                    ExecuteBreak(settings, round);
                    allSuspendedIds.Clear();

                    if (_exitSession) break;
                }
            }

            // FIX #8: Pomodoro kapalıyken allSuspendedIds zaten boş, gereksiz çağrı
            // korunuyor ama önce boş mu kontrolü yapılıyor
            if (allSuspendedIds.Count > 0)
                ResumeAll(allSuspendedIds);

            Console.WriteLine("\n📄 Detaylar: focus_flow.log  |  dashboard.html");
            Console.WriteLine("Ana menüye dönmek için bir tuşa basın...");
            Console.ReadKey();
        }

        static bool ExecuteSession(
            AppSettings settings, int round, int totalRounds, HashSet<int> allSuspendedIds)
        {
            DateTime startTime    = DateTime.Now;
            DateTime endTime      = startTime.AddMinutes(settings.DefaultDuration);
            var suspendedIds      = new HashSet<int>();
            int newlyBlockedCount = 0;
            int myId              = Process.GetCurrentProcess().Id;
            _paused      = false;
            _exitSession = false;

            bool pomodoroOn = totalRounds > 1;
            int timerLine   = pomodoroOn ? TimerLine_P  : TimerLine_NP;
            int barLine     = pomodoroOn ? BarLine_P    : BarLine_NP;
            int statusLine  = pomodoroOn ? StatusLine_P : StatusLine_NP;
            int msgLine     = pomodoroOn ? MsgLine_P    : MsgLine_NP;

            Log($"Seans Başladı [{round}/{totalRounds}]: {settings.DefaultGoal}");
            Console.Clear();
            DrawSessionFrame(settings.DefaultGoal, round, totalRounds);

            while (DateTime.Now < endTime && !_exitSession)
            {
                if (_paused)
                {
                    Console.Title = "⏸ DURAKLATILDI — [P] ile devam";
                    if (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(true).Key;
                        if (k == ConsoleKey.P)
                        {
                            _paused = false;
                            Log("Seans devam ediyor.");
                        }
                        if (k == ConsoleKey.Escape && settings.AllowEscape)
                            _exitSession = true;
                    }
                    Thread.Sleep(200);
                    continue;
                }

                TimeSpan rem = endTime - DateTime.Now;
                string timeStr = settings.ShowSeconds
                    ? $"{(int)rem.TotalMinutes:D2}:{rem.Seconds:D2}"
                    : $"{(int)rem.TotalMinutes:D2} dk";

                Console.Title = $"⌛ {timeStr} | {settings.DefaultGoal}";

                SafeSetCursor(0, timerLine);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"║   ⏰  {timeStr,-8}                             ║");

                SafeSetCursor(0, barLine);
                Console.ForegroundColor = ConsoleColor.Green;
                double pct = 1.0 - rem.TotalSeconds / (settings.DefaultDuration * 60.0);
                pct        = Math.Max(0, Math.Min(1, pct));
                int barW   = 40;
                int filled = (int)(pct * barW);
                string bar = new string('█', filled) + new string('░', barW - filled);
                Console.Write($"║   [{bar}]  ║");
                Console.ResetColor();

                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true).Key;
                    if (k == ConsoleKey.Escape && settings.AllowEscape)
                    {
                        _exitSession = true;
                        Log("Kullanıcı ESC ile seansı sonlandırdı.");
                    }
                    else if (k == ConsoleKey.P)
                    {
                        _paused = true;
                        SafeSetCursor(0, statusLine);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("⏸  DURAKLATILDI — devam etmek için [P]    ");
                        Console.ResetColor();
                        Log("Seans duraklatıldı.");
                    }
                }

                if (_isWindows && _isAdmin)
                {
                    int newCount = SuspendBlacklistedApps(
                        settings.Blacklist, myId, suspendedIds, allSuspendedIds, msgLine);
                    newlyBlockedCount += newCount;
                }

                Thread.Sleep(500);
            }

            bool sessionCompleted = !_exitSession;

            FinalizeFocusSession(
                startTime, suspendedIds, settings.DefaultGoal,
                sessionCompleted, newlyBlockedCount, settings.OpenDashboard);

            return sessionCompleted;
        }

        // FIX #5: SafeSetCursor — WindowHeight kullanıyor
        static void SafeSetCursor(int left, int top)
        {
            try
            {
                if (top  >= 0 && top  < Console.WindowHeight &&
                    left >= 0 && left < Console.WindowWidth)
                {
                    Console.SetCursorPosition(left, top);
                }
            }
            catch { /* Resize race condition — görmezden gel */ }
        }

        static void ExecuteBreak(AppSettings settings, int round)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            // Satır 0: boş (\n)
            // Satır 1: ☕ MOLA ...
            // Satır 2: süre bilgisi
            // Satır 3: tuş kılavuzu
            // Satır 4: ResetColor + boş (WriteLine sonrası imleci bir satır ilerletir)
            // Satır 5: zamanlayıcı → SafeSetCursor(0, 5)
            Console.WriteLine($"\n☕ MOLA — Tur {round} tamamlandı!");
            Console.WriteLine($"   {settings.BreakDuration} dakika dinlen.");
            Console.WriteLine("   [ENTER] atla  |  [ESC] ana menüye dön");
            Console.ResetColor();
            Console.WriteLine(); // Satır 4 — boş tampon satırı, cursor satır 5'e geçer

            DateTime breakEnd = DateTime.Now.AddMinutes(settings.BreakDuration);
            bool skipBreak    = false;

            while (DateTime.Now < breakEnd && !skipBreak)
            {
                TimeSpan rem = breakEnd - DateTime.Now;
                // FIX #2: \n ile açılan boş satır nedeniyle zamanlayıcı satırı 5'te
                SafeSetCursor(0, 5);
                Console.Write($"   ⏰ Kalan mola: {(int)rem.TotalMinutes:D2}:{rem.Seconds:D2}   ");

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Enter)
                        skipBreak = true;
                    else if (key == ConsoleKey.Escape)
                    {
                        _exitSession = true;
                        skipBreak    = true;
                    }
                }
                Thread.Sleep(500);
            }

            PlayBeep(800, 300);
        }

        // Emoji genişliği gözetilerek padding hesaplandı.
        // Çerçeve: ╔ + 46 karakter + ╗ = 48 toplam; iç alan = 46.
        // Hedef satırı: "║  🎯 " → ║(1) + 2 boşluk(2) + emoji(2 terminal col) + 1 boşluk(1) = 6
        // Sağ kenar: ║(1) → kullanılabilir hedef alanı = 46 - 6 - 1 = 39... HAYIR:
        //
        // Gerçek çerçeve genişliği ölçümü (DrawSessionFrame satırı):
        //   "╔══════════════════════════════════════════════╗"
        //    ^                 44 ═ işareti                ^
        // Toplam görünür genişlik = 1 + 44 + 1 = 46 karakter.
        // İç içerik alanı (║ ile ║ arası, kenarsız) = 44 karakter.
        //
        // Hedef satırı prefix görünür genişliği:
        //   ║(1) + 2×boşluk(2) + 🎯 emoji(2 terminal col) + 1×boşluk(1) = 6
        // Sağ ║ = 1
        // Kullanılabilir hedef alanı = 44 - 6 - 1 = 37
        static void DrawSessionFrame(string goal, int round, int totalRounds)
        {
            // FIX #7: Terminal dar açılmışsa uyar — SafeSetCursor koordinatları kayar
            if (Console.WindowWidth < MinTerminalWidth)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"⚠️  Pencere çok dar ({Console.WindowWidth} sütun).");
                Console.WriteLine($"   En az {MinTerminalWidth} sütun olmalı. Lütfen genişletin.");
                Console.ResetColor();
            }

            bool pomodoroOn = totalRounds > 1;
            Console.ForegroundColor = ConsoleColor.Cyan;

            // goalAvail doğru hesap = 37
            const int goalAvail = 37;
            string goalDisplay = goal.Length > goalAvail
                ? goal[..goalAvail]
                : goal.PadRight(goalAvail);

            // Satır 0
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            // Satır 1
            Console.WriteLine($"║  \U0001F3AF {goalDisplay}║");

            if (pomodoroOn)
            {
                // Tur satırı prefix: ║(1) + 2×boşluk(2) + 🍅(2) + 1×boşluk(1) = 6
                // "Tur X/Y" metni: kalan = 44 - 6 - 1 = 37... ama "Tur " sabiti 4 char ekliyor:
                // Prefix zaten "║  🍅 " = 6; sağ ║ = 1; kalan metin alanı = 44 - 6 - 1 = 37
                const int roundAvail = 37;
                string roundStr = $"Tur {round}/{totalRounds}".PadRight(roundAvail);
                // Satır 2
                Console.WriteLine($"║  \U0001F345 {roundStr}║");
                // Satır 3
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                // Satır 4
                Console.WriteLine("║                                              ║");
                // Satır 5 — TimerLine_P
                Console.WriteLine("║   ⏰  --:--                                  ║");
                // Satır 6 — BarLine_P
                Console.WriteLine("║   [░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]  ║");
                // Satır 7
                Console.WriteLine("║                                              ║");
                // Satır 8 — CloseLine_P
                Console.WriteLine("╚══════════════════════════════════════════════╝");
            }
            else
            {
                // Satır 2
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                // Satır 3
                Console.WriteLine("║                                              ║");
                // Satır 4 — TimerLine_NP
                Console.WriteLine("║   ⏰  --:--                                  ║");
                // Satır 5 — BarLine_NP
                Console.WriteLine("║   [░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]  ║");
                // Satır 6
                Console.WriteLine("║                                              ║");
                // Satır 7 — CloseLine_NP
                Console.WriteLine("╚══════════════════════════════════════════════╝");
            }

            Console.ResetColor();
            Console.WriteLine("\n[ESC] Bitir  |  [P] Duraklat");
        }

        // FIX #6: Blacklist araması büyük/küçük harf duyarsız yapıldı.
        // GetProcessesByName zaten case-insensitive ama ek güvenlik için
        // blacklist entry'leri normalize (lowercase) olarak saklanıyor.
        static int SuspendBlacklistedApps(
            List<string> blacklist, int myId,
            HashSet<int> suspendedIds, HashSet<int> allSuspendedIds,
            int msgLine)
        {
            int newCount = 0;

            foreach (string target in blacklist)
            {
                // FIX #6: target'ı normalize et — büyük harf entry'leri de eşleşsin
                string normalizedTarget = target.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(normalizedTarget)) continue;

                Process[] procs;
                try { procs = Process.GetProcessesByName(normalizedTarget); }
                catch { continue; }

                foreach (Process p in procs)
                {
                    try
                    {
                        if (p.Id == myId) continue;
                        if (suspendedIds.Contains(p.Id)) continue;

                        // FIX: ProcessName erişimi handle'dan önce exception fırlatabilir
                        // (proses kapandıysa Win32Exception). try bloğu zaten kapsıyor ama
                        // açıklık için önce Id kontrolü, sonra ProcessName kontrolü yapıyoruz.
                        string procName;
                        try { procName = p.ProcessName; }
                        catch { continue; } // Proses kapanmış, atla

                        if (_protectedProcesses.Contains(procName)) continue;

                        IntPtr handle;
                        try { handle = p.Handle; }
                        catch (Exception ex)
                        {
                            Log($"Handle erişim hatası [{target}]: {ex.Message}");
                            continue;
                        }

                        NtSuspendProcess(handle);
                        suspendedIds.Add(p.Id);
                        allSuspendedIds.Add(p.Id);
                        newCount++;

                        SafeSetCursor(0, msgLine);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[❄️ DONDURULDU] {procName,-20} {DateTime.Now:HH:mm:ss}");
                        Console.ResetColor();

                        Log($"Donduruldu: {procName} (PID:{p.Id})");
                    }
                    catch (Exception ex)
                    {
                        Log($"Dondurma hatası [{target}]: {ex.Message}");
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }

            return newCount;
        }

        static void ResumeAll(HashSet<int> suspendedIds)
        {
            var failedIds = new List<int>(); // FIX #5: başarısız resume takibi

            foreach (int id in suspendedIds)
            {
                try
                {
                    using var p = Process.GetProcessById(id);
                    try { NtResumeProcess(p.Handle); }
                    catch (Exception ex)
                    {
                        Log($"Resume handle hatası (PID:{id}): {ex.Message}");
                        failedIds.Add(id);
                    }
                }
                catch { /* Proses zaten kapanmış — sorun değil */ }
            }
            suspendedIds.Clear();

            // FIX #5: Donuk kalabilecek prosesler varsa kullanıcıya bildir
            if (failedIds.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"⚠️  {failedIds.Count} uygulama uyandırılamadı (PID: {string.Join(", ", failedIds)}).");
                Console.WriteLine("   Görev yöneticisinden manuel olarak kapatmanız gerekebilir.");
                Console.ResetColor();
            }
        }

        static void FinalizeFocusSession(
            DateTime startTime, HashSet<int> suspendedIds, string goal,
            bool completed, int blockedCount, bool openDashboard)
        {
            Console.Clear();
            Console.WriteLine("🔓 Seans bitti. Uygulamalar uyandırılıyor...");

            ResumeAll(suspendedIds);

            int mins = (int)(DateTime.Now - startTime).TotalMinutes;

            if (completed) { PlayBeep(1000, 200); Thread.Sleep(120); PlayBeep(1300, 350); }
            else            { PlayBeep(600,  400); }

            SaveSession(goal, mins, blockedCount, completed);
            GenerateDashboard(openDashboard);
            Log($"Bitti. Süre: {mins} dk | Tamamlandı: {completed} | Engellenen: {blockedCount}");

            Console.ForegroundColor = completed ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(completed
                ? $"\n🏆 Tebrikler! {mins} dakika tam odaklandın."
                : $"\n⚡ Seans {mins} dakikada sonlandırıldı.");
            Console.ResetColor();
        }

        static void ShowStats()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n══════ İSTATİSTİKLER ══════\n");
            Console.ResetColor();

            try
            {
                if (!File.Exists(StatsFile))
                {
                    Console.WriteLine("Henüz kayıtlı seans yok.");
                }
                else
                {
                    var sessions = JsonSerializer.Deserialize<List<FocusSession>>(
                        File.ReadAllText(StatsFile, Encoding.UTF8)) ?? new List<FocusSession>();

                    int totalMin  = sessions.Sum(s => s.DurationMinutes);
                    int completed = sessions.Count(s => s.Completed);

                    Console.WriteLine($"📊 Toplam seans  : {sessions.Count}");
                    Console.WriteLine($"✅ Tamamlanan    : {completed}");
                    Console.WriteLine($"⏱️  Toplam süre   : {totalMin} dk  ({totalMin / 60} sa {totalMin % 60} dk)");
                    Console.WriteLine($"🚫 Toplam engel  : {sessions.Sum(s => s.BlockedCount)}");
                    Console.WriteLine($"\n📅 Son 5 seans:\n{new string('─', 50)}");

                    // FIX #5: TakeLast(5).Reverse() yerine doğrudan index döngüsü
                    int start = Math.Max(0, sessions.Count - 5);
                    for (int i = sessions.Count - 1; i >= start; i--)
                    {
                        var s    = sessions[i];
                        string icon = s.Completed ? "✅" : "⚡";
                        Console.WriteLine($"  {icon} {s.Date:dd.MM HH:mm} | {s.DurationMinutes,3} dk | {s.Goal}");
                    }
                }
            }
            catch (JsonException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ '{StatsFile}' dosyası bozulmuş ve okunamıyor.");
                Console.WriteLine("   Dosyayı silip uygulamayı yeniden başlatabilirsiniz.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ İstatistik yükleme hatası: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nAna menüye dönmek için bir tuşa basın...");
            Console.ReadKey();
        }

        static void ShowBanner()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine(@"
    ███████╗ ██████╗  ██████╗██╗   ██╗███████╗
    ██╔════╝██╔═══██╗██╔════╝██║   ██║██╔════╝
    █████╗  ██║   ██║██║     ██║   ██║███████╗
    ██╔══╝  ██║   ██║██║     ██║   ██║╚════██║
    ██║     ╚██████╔╝╚██████╗╚██████╔╝███████║
    ╚═╝      ╚═════╝  ╚═════╝ ╚═════╝ ╚══════╝");
            Console.WriteLine($"            >>> FOCUS-FLOW v{AppVersion} <<<");
            Console.ResetColor();
        }

#pragma warning disable CA1416
        static void PlayBeep(int freq, int ms)
        {
            if (!_isWindows) return;
            try { Console.Beep(freq, ms); } catch { }
        }
#pragma warning restore CA1416

        // Log boyutu optimizasyonu:
        // _logSizeCache = -2 → henüz initialize edilmedi, ilk çağrıda mevcut boyut okunur
        // _logSizeCache = -1 → dosya yok (normal durum)
        // Her 100 çağrıda bir boyut güncellenir; araya rotate girmişse 0'a sıfırlanır
        static void Log(string message)
        {
            lock (_logLock)
            {
                try
                {
                    // FIX #3: İlk çağrıda mevcut log boyutunu oku — ilk 100 çağrıda da rotasyon çalışır
                    if (_logSizeCache == -2)
                    {
                        _logSizeCache = File.Exists(LogFile)
                            ? new FileInfo(LogFile).Length
                            : -1;
                    }

                    // Her 100 çağrıda bir dosya boyutunu güncelle
                    if (++_logCheckCounter >= 100)
                    {
                        _logCheckCounter = 0;
                        _logSizeCache = File.Exists(LogFile)
                            ? new FileInfo(LogFile).Length
                            : -1;
                    }

                    if (_logSizeCache > MaxLogSize)
                    {
                        string backup = $"focus_flow_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                        File.Move(LogFile, backup, overwrite: true);
                        _logSizeCache = 0;
                    }

                    File.AppendAllText(
                        LogFile,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        // FIX #1: Atomik yazma — WriteAllText önce truncate eder, yarıda kesilirse veri kaybolur.
        // Çözüm: önce temp dosyaya yaz, başarılıysa File.Move ile yerini al (overwrite).
        static void SaveSession(string goal, int duration, int blocked, bool completed)
        {
            try
            {
                var sessions = new List<FocusSession>();

                if (File.Exists(StatsFile))
                {
                    try
                    {
                        sessions = JsonSerializer.Deserialize<List<FocusSession>>(
                            File.ReadAllText(StatsFile, Encoding.UTF8)) ?? new List<FocusSession>();
                    }
                    catch (JsonException ex)
                    {
                        // Stats bozuksa sıfırdan başla, mevcut dosyayı yedekle
                        Log($"Stats bozulmuş, yedekleniyor: {ex.Message}");
                        string backup = $"stats_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                        try { File.Copy(StatsFile, backup, overwrite: true); } catch { }
                        sessions = new List<FocusSession>();
                    }
                }

                sessions.Add(new FocusSession
                {
                    Date            = DateTime.Now,
                    Goal            = goal,
                    DurationMinutes = duration,
                    BlockedCount    = blocked,
                    Completed       = completed
                });

                var opts    = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(sessions, opts);

                // Önce temp dosyaya yaz
                File.WriteAllText(StatsTempFile, json, Encoding.UTF8);
                // Atomik yer değiştir — bu adım başarısız olsa bile stats.json sağlam kalır
                File.Move(StatsTempFile, StatsFile, overwrite: true);
            }
            catch (Exception ex) { Log($"Seans kaydetme hatası: {ex.Message}"); }
        }

        // FIX #7: using var doc kullanılmıyordu — gereksiz nesne kaldırıldı,
        // JSON geçerliliği parse exception'ı ile zaten kontrol ediliyor
        static void GenerateDashboard(bool openDashboard = true)
        {
            try
            {
                if (!File.Exists(StatsFile)) return;

                string jsonData = File.ReadAllText(StatsFile, Encoding.UTF8);

                // Geçerlilik kontrolü — parse başarısız olursa exception fırlatır, catch yakalar
                JsonDocument.Parse(jsonData).Dispose();

                // FIX #1: </script> tag'i JSON içindeyse HTML'i bozmasın
                string safeJson = jsonData.Replace("</script>", "<\\/script>", StringComparison.OrdinalIgnoreCase);

                string htmlContent = $@"<!DOCTYPE html>
<html lang='tr'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Focus-Flow Raporu</title>
<script src='https://cdn.jsdelivr.net/npm/chart.js'></script>
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ font-family:system-ui,sans-serif; background:#0f172a; color:#fff; padding:20px; }}
  .wrap {{ max-width:1000px; margin:auto; }}
  h1 {{ text-align:center; color:#38bdf8; margin-bottom:2rem; font-size:2rem; }}
  .grid {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:16px; margin-bottom:2rem; }}
  .card {{ background:#1e293b; padding:20px; border-radius:12px; text-align:center; border-left:4px solid #38bdf8; }}
  .num {{ font-size:2.5rem; font-weight:700; color:#38bdf8; }}
  .lbl {{ font-size:0.85rem; color:#94a3b8; margin-top:8px; }}
  .chartbox {{ background:#1e293b; padding:24px; border-radius:12px; }}
  .error {{ color:#f43f5e; padding:20px; background:#1e293b; border-radius:12px; text-align:center; }}
</style>
</head>
<body>
<div class='wrap'>
  <h1>🚀 Focus-Flow Raporu</h1>
  <div id='grid' class='grid'></div>
  <div class='chartbox'><canvas id='chart'></canvas></div>
</div>
<!-- FIX #1: safeJson — </script> kaçırılmış, XSS imkansız -->
<script type='application/json' id='session-data'>
{safeJson}
</script>
<script>
document.addEventListener('DOMContentLoaded', function() {{
  let data;
  try {{
    const raw = document.getElementById('session-data').textContent;
    data = JSON.parse(raw);
  }} catch(e) {{
    document.getElementById('grid').innerHTML = '<div class=""error"">❌ Veri ayrıştırma hatası: ' + e.message + '</div>';
    return;
  }}

  if (!Array.isArray(data) || data.length === 0) {{
    document.getElementById('grid').innerHTML = '<div class=""error"">📊 Henüz veri yok</div>';
    return;
  }}

  const totalMin  = data.reduce((a,s) => a + (s.durationMinutes || 0), 0);
  const completed = data.filter(s => s.completed).length;
  const blocked   = data.reduce((a,s) => a + (s.blockedCount   || 0), 0);

  const grid = document.getElementById('grid');
  [
    {{value: data.length,          label: 'Toplam Seans'}},
    {{value: completed,             label: 'Tamamlanan'}},
    {{value: totalMin + ' dk',     label: 'Toplam Süre'}},
    {{value: blocked,              label: 'Engellenen'}}
  ].forEach(s => {{
    const card = document.createElement('div');
    card.className = 'card';
    card.innerHTML = '<div class=""num"">' + s.value + '</div><div class=""lbl"">' + s.label + '</div>';
    grid.appendChild(card);
  }});

  const labels    = data.map(s => {{
    const d = new Date(s.date);
    return (d.getDate()+'').padStart(2,'0') + '.' + ((d.getMonth()+1)+'').padStart(2,'0');
  }});
  const durations = data.map(s => s.durationMinutes || 0);
  const blockedArr= data.map(s => s.blockedCount    || 0);

  try {{
    new Chart(document.getElementById('chart').getContext('2d'), {{
      type: 'line',
      data: {{
        labels,
        datasets: [
          {{
            label: 'Odak Süresi (dk)',
            data: durations,
            borderColor: '#38bdf8',
            backgroundColor: 'rgba(56,189,248,0.1)',
            borderWidth: 2, tension: 0.4, fill: true
          }},
          {{
            label: 'Engellenen Sayı',
            data: blockedArr,
            borderColor: '#f43f5e',
            backgroundColor: 'rgba(244,63,94,0.1)',
            borderWidth: 2, tension: 0.4, fill: true
          }}
        ]
      }},
      options: {{
        responsive: true,
        plugins: {{ legend: {{ labels: {{ color: '#fff', font: {{ size: 12 }} }} }} }},
        scales: {{
          y: {{ ticks: {{ color: '#94a3b8' }}, grid: {{ color: 'rgba(255,255,255,0.08)' }}, beginAtZero: true }},
          x: {{ ticks: {{ color: '#94a3b8' }}, grid: {{ color: 'rgba(255,255,255,0.08)' }} }}
        }}
      }}
    }});
  }} catch(e) {{ console.error('Chart hatası:', e); }}
}});
</script>
</body>
</html>";

                File.WriteAllText(DashboardFile, htmlContent, Encoding.UTF8);
                Log("Dashboard oluşturuldu.");

                if (openDashboard)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(DashboardFile)
                            { UseShellExecute = true });
                        Log("Dashboard açıldı.");
                    }
                    catch (Exception ex) { Log($"Dashboard açma hatası: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Log($"Dashboard oluşturma hatası: {ex.Message}"); }
        }
    }
}