# 🚀 Focus-Flow v5.3: Ultimate System Guard

Focus-Flow, ders çalışma veya yazılım geliştirme süreçlerinde dikkati dağıtan unsurları (sosyal medya, oyunlar, tarayıcı sekmeleri) sistem seviyesinde yönetmek için geliştirilmiş bir **C# Sistem Yardımcı Programıdır.**

Özellikle YKS hazırlık sürecinde disiplini artırmak ve Marmara Üniversitesi Bilgisayar Mühendisliği hedefine odaklanmak amacıyla tasarlanmıştır.

## ✨ Temel Özellikler

*   **🛡️ İki Farklı Koruma Modu:**
    *   **Kara Liste (Blacklist):** Sadece belirlenen yasaklı uygulamaları kapatır.
    *   **Beyaz Liste (Whitelist):** Belirlenen izinli uygulamalar ve kritik sistem süreçleri hariç her şeyi dondurur.
*   **📊 Veri Analitiği:** Her odaklanma seansı sonunda çalışma süresini ve engellenen uygulama sayısını `stats.json` dosyasına kaydeder.
*   **⚙️ Dinamik Hedef Belirleme:** Program her açıldığında o anki hedefinizi sorar ve bunu başlık çubuğunda (Console Title) canlı bir sayaçla takip etmenizi sağlar.
*   **🔒 Akıllı Korumalar:** Kendi sürecini (Process) ve Windows'un temel bileşenlerini (Explorer, VS Code vb.) yanlışlıkla kapatmamak için özel bir "Self-Shield" mekanizmasına sahiptir.

## 🛠️ Teknik Detaylar

*   **Dil:** C# (.NET 8.0)
*   **Teknolojiler:** `System.Diagnostics` (Process Management), `System.Text.Json` (Data Serialization), `System.Linq`.
*   **Platform:** Windows (64-bit)

## 🚀 Kurulum ve Kullanım

1.  [Releases](../../releases) kısmından en güncel `.exe` dosyasını indirin.
2.  Uygulamayı **Yönetici Olarak Çalıştırın** (Uygulama kapatma yetkisi için gereklidir).
3.  Odağınızı belirleyin, modunuzu seçin ve kilitlenin.

## 📈 İstatistik Takibi
Program çalıştıkça oluşan `stats.json` dosyası şu yapıda veri saklar:
```json
{
  "Date": "2026-05-14T22:30:00",
  "Goal": "Marmara Bilgisayar için Matematik",
  "DurationMinutes": 60,
  "BlockedCount": 12
}
```

## 📜 Lisans
* Bu proje **MIT** lisansı altında korunmaktadır.