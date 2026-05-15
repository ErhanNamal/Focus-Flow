# 🚀 Focus-Flow v7.1.1

Windows odaklanma asistanı. Dikkat dağıtıcı uygulamaları otomatik dondurur, Pomodoro tekniğiyle çalışmanı destekler ve detaylı istatistik tutar.

---

## 📋 Özellikler

| Özellik | Açıklama |
|---------|----------|
| 🧊 **Uygulama Dondurma** | Blacklist'teki uygulamaları (Discord, Spotify, Steam vb.) otomatik askıya alır |
| 🍅 **Pomodoro Modu** | 4 tur odaklanma + mola döngüsü (ayarlanabilir) |
| ⏸️ **Duraklatma** | `[P]` tuşuyla seansı duraklat, devam et |
| 🛡️ **ESC Koruması** | Ayarlardan ESC ile çıkışı engelleyebilirsin |
| 📊 **İstatistikler** | JSON tabanlı seans kaydı, HTML dashboard ile görsel rapor |
| 🎵 **Ses Bildirimi** | Seans bitiminde bip sesi (Windows) |
| 📝 **Log Rotation** | 5 MB üzerinde otomatik log yedekleme |

---

## 🚀 Hızlı Başlangıç

### Gereksinimler
- Windows 10/11 (tam işlevsellik için)
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download) veya üstü
- **Yönetici yetkisi** (uygulama dondurma için zorunlu)

### Derleme
```bash
dotnet build -c Release
```

### Çalıştırma
```bash
# Yönetici olarak çalıştır (önerilen)
Run as Administrator -> FocusFlow.exe

# Normal kullanıcı (dondurma devre dışı)
FocusFlow.exe
```

---

## 🎮 Kullanım

### Ana Menü
```
[ENTER] Başlat  |  [S] Ayarlar  |  [H] İstatistikler  |  [Q] Çıkış
```

### Seans Ekranı
```
╔══════════════════════════════════════════════╗
║  🎯 TYT-AYT Odaklanma                         ║
╠══════════════════════════════════════════════╣
║                                              ║
║   ⏰  38:42                                  ║
║   [████████████░░░░░░░░░░░░░░░░░░░░░░░░░░]  ║
║                                              ║
╚══════════════════════════════════════════════╝

[ESC] Bitir  |  [P] Duraklat
```

### Kısayollar
| Tuş | İşlem |
|-----|-------|
| `ENTER` | Seans başlat |
| `P` | Duraklat / Devam et |
| `ESC` | Seansı sonlandır (Ayarlar'dan kapatılabilir) |
| `S` | Ayarlar menüsü |
| `H` | İstatistikler |
| `Q` | Çıkış |

---

## ⚙️ Ayarlar (`settings.json`)

```json
{
  "Blacklist": ["discord", "spotify", "steam"],
  "DefaultDuration": 40,
  "BreakDuration": 5,
  "DefaultGoal": "TYT-AYT Odaklanma",
  "AllowEscape": true,
  "ShowSeconds": true,
  "PomodoroMode": false,
  "PomodoroRounds": 4
}
```

| Alan | Varsayılan | Aralık | Açıklama |
|------|-----------|--------|----------|
| `Blacklist` | `discord, spotify, steam` | — | Dondurulacak uygulamalar |
| `DefaultDuration` | 40 | 1-180 dk | Odaklanma süresi |
| `BreakDuration` | 5 | 1-60 dk | Mola süresi |
| `PomodoroMode` | false | — | Pomodoro döngüsü aç/kapat |
| `PomodoroRounds` | 4 | 1-20 | Toplam tur sayısı |
| `AllowEscape` | true | — | ESC ile çıkışa izin ver |
| `ShowSeconds` | true | — | Saniye gösterimi |

---

## 🛡️ Güvenlik

### Korunan Sistem Uygulamaları
Aşağıdaki process'ler **asla** dondurulmaz:
```
explorer, lsass, csrss, services, svchost, winlogon,
System, Registry, dwm, cmd, powershell, ...
```

### XSS Koruması
Dashboard'da JSON verisi `JavaScriptEncoder` ile güvenli şekilde HTML'e aktarılır.

---

## 📁 Dosya Yapısı

```
FocusFlow/
├── Program.cs              # Ana kod
├── settings.json           # Kullanıcı ayarları
├── stats.json              # Seans kayıtları
├── focus_flow.log          # Uygulama logları
├── dashboard.html          # Otomatik oluşturulan rapor
└── focus_flow_*.log        # Yedek loglar (5MB+)
```

---

## 🐞 Hata Giderme

| Sorun | Çözüm |
|-------|-------|
| "Yönetici yetkisi yok" | Uygulamayı sağ tıkla → "Yönetici olarak çalıştır" |
| Uygulamalar donmuyor | Admin yetkisi gerekli, ayrıca process adı doğru mu kontrol et |
| Dashboard açılmıyor | `stats.json` boş olabilir, bir seans tamamla |
| `ArgumentOutOfRangeException` | Konsol penceresini büyüt (v7.1.1+'da düzeltildi) |

---

## 📝 Sürüm Geçmişi

| Versiyon | Değişiklikler |
|----------|--------------|
| **7.1.1** | Konsol buffer taşması düzeltmesi, `SafeSetCursor` |
| **7.1.0** | Pomodoro mola düzeltmesi, XSS koruması, log rotation, ayar iptali |
| **7.0.0** | İlk stabil sürüm |

---

## 📄 Lisans

MIT License — Özgürce kullan, değiştir, dağıt.

---

> 💡 **İpucu:** Blacklist'e eklemeden önce hedef uygulamanın process adını Görev Yöneticisi'nden kontrol et. Örn: "Discord" için `discord`, "Google Chrome" için `chrome`.
