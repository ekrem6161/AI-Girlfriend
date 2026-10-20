/*
 ============================================================
  DESKTOP NOTIFICATION - Windows Bildirimi      (Sinif)
 ============================================================
  KONU : WinRT (Windows.UI.Notifications), Registry
 ============================================================

  PROBLEM:
  Nova cevap verdiginde, tarayiciya bakmiyor olabiliriz.
  Windows'un sag alt kosesinden kayan bildirim istiyoruz.

  ============================================================
  NASIL CALISIYOR?
  ============================================================
  1) Windows her programa bir "AUMID" (AppUserModelId) ile
     taninir. Bu kimlik, bildirimde gosterilen isim ve ikonu
     belirler.

  2) Biz kendi kimligimizi kaydediyoruz:
         HKCU\Software\Classes\AppUserModelId\Nova.Chat
             DisplayName = Nova
     (Program ilk acildiginda bir kez yazilir.)

  3) Bildirim gonderirken:
         ToastNotificationManager
             .CreateToastNotifier("Nova.Chat")
             .Show(toast)

  "ToastGeneric" = modern goruntu (Windows 10 2004+).
  Eski "ToastText02" yerine bunu kullaniyoruz.

  ============================================================
  NEDEN NuGet PAKETI YOK?
  ============================================================
  Proje hedefi "net8.0-windows10.0.19041.0" oldugu icin
  Windows SDK projeksiyonlari (.NET ile birlikte gelir).
  "using Windows.UI.Notifications;" yazmak yeterli.

  ============================================================
  DIKKAT
  ============================================================
  - Bildirim sadece PROGRAM ACIKKEN gider (sunucu durursa gelmez)
  - Windows ayarlarinda "Nova" gorunur, ilk seferde izin
    gerekebilir
  - Uzun metinler kisaltilir (Windows ~3 satir gosterir)
  ============================================================
*/

using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

public class DesktopNotification
{
    // Uygulamanin Windows'taki kimligi
    private const string AppId = "Nova.Chat";

    // Bildirimde gorunecek isim
    private const string DisplayName = "Nova";

    // Bildirim acilisinda gosterilecek azami karakter
    private const int MaxBodyLength = 240;

    private readonly bool _enabled;

    public DesktopNotification(bool enabled)
    {
        _enabled = enabled;

        if (_enabled)
        {
            EnsureAppRegistered();
        }
    }

    // ------------------------------------------------------------
    //  Show  -  Bildirim gonder
    // ------------------------------------------------------------
    public void Show(string title, string body)
    {
        if (!_enabled) return;

        try
        {
            // Cevabi kisalt (Windows 3 satirdan fazlasini gostermez)
            string kisa = body.Length > MaxBodyLength
                ? body[..MaxBodyLength].TrimEnd() + "..."
                : body;

            // XML metni hazirla.
            //   ToastGeneric = baslik + govde + opsiyonel ikon
            string xml = $@"<toast>
  <visual>
    <binding template=""ToastGeneric"">
      <text>{EscapeXml(title)}</text>
      <text>{EscapeXml(kisa)}</text>
    </binding>
  </visual>
</toast>";

            var belge = new XmlDocument();
            belge.LoadXml(xml);

            var toast = new ToastNotification(belge);

            // Bildirimi gonder
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }
        catch (Exception ex)
        {
            // Bildirim gonderilemezse program calismaya devam et.
            // (En sik neden: Windows bildirimleri kapali.)
            Console.WriteLine($"[UYARI] Bildirim gonderilemedi: {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    //  EnsureAppRegistered  -  Windows'ta kendimizi tanit
    // ------------------------------------------------------------
    private static void EnsureAppRegistered()
    {
        try
        {
            string yol = $@"Software\Classes\AppUserModelId\{AppId}";

            using RegistryKey? anahtar = Registry.CurrentUser.CreateSubKey(yol);

            if (anahtar is null) return;

            anahtar.SetValue("", AppId);
            anahtar.SetValue("DisplayName", DisplayName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UYARI] AUMID kaydi yapilamadi: {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    //  EscapeXml  -  Ozel karakterleri XML'e uygun hale getir
    // ------------------------------------------------------------
    //  Cevap icinde " & < > olabilir. Bunlar XML'de bozulur.
    private static string EscapeXml(string metin)
    {
        return metin
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) WinRT nedir?

     Windows Runtime = Windows'un kendi API dili.
     .NET 8, "net8.0-windows10.0.x" hedefiyle bu API'lara
     direktik erisebilir. Ek paket gerekmez.

  2) Registry nedir?

     Windows ayarlarinin saklandigi yeri duzenlemek icin
     kullanilir. Burada "bu programin kimligi su" bilgisini
     yaziyoruz. HKCU = "Current User" (bu kullanicinin ayarlari),
     yani yonetici yetkisi gerektirmez.

     "using Microsoft.Win32;" ile erisilir.

  3) Neden XML yaziyoruz?

     Bildirim icerigi XML ile tarif edilir:
         <toast>
           <visual>
             <binding template="ToastGeneric">
               <text>Baslik</text>
               <text>Govde</text>
             </binding>
           </visual>
         </toast>

  4) Hata yakalama neden onemli?

     Bildirim ozelligi kritik degil. Olursa program cokmemeli,
     sadece konsola uyari yazmali. Boylece asil sohbet calisir.
 ---------------------------------------------------------
*/
