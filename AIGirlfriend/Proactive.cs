/*
 ============================================================
  PROACTIVE - Kendi Kendine Mesaj Atma           (Sinif)
 ============================================================
  KONU : async/await, ConcurrentQueue, PeriodicTimer benzeri
         dongu, CancellationToken
 ============================================================

  PROBLEM:
  Nova sadece cevap vermek zorunda degil. Personasi soyle diyor:
     "Whenever she feels particularly bored, Nova reaches out to her
      partner to share details about her daily life..."
  Yani o da bazen ilk adimi atabilir.

  COZUM:
  Sunucu acik oldugu surece, arka planda bir dongu calisir.
  Belirli araliklarla Nova kendi mesajini uretir.

  ============================================================
  ZAMANLAYICI KURALLARI
  ============================================================
  1) Kullanici SON 10 DAKIKADA konustuysa sayac bekle.
     (Siz sohbet ederken Nova sizi bolmez.)
  2) Kullanici sessiz kaldiysa 2 saatlik sayac baslar.
  3) Sayac dolunca: 1-10 arasinda rastgele bir asama sec,
     o asamanin ortamini + personayi + tetikleyiciyi modele gonder.
  4) Uretilen mesaj:
       - Brain'a yazilir (proaktif olarak isaretlenir)
       - Kuyruga konur (tarayiciya teslim icin)
       - Windows bildirimi gonderilir
  5) 5 dakika icinde cevap gelmezse mesaj Brain'dan SILINIR.
     (Bunu Brain yapar, GetHistory cagrilirken temizler.)

  ============================================================
  "5 DAKIKA" KURALI NEDEN VAR?
  ============================================================
  Cevapsiz kalan proaktif mesajlar history'de birikir:
      Nova: "merhaba"
      Nova: "bugun agirlik topladim"
      Nova: "hala yok musun"
  Model bunu gorunce kafasini karistirir.
  Bu yuzden 5 dakika cevap yoksa mesaj history'den cikar.
  ============================================================
*/

using System.Collections.Concurrent;

public record ProactiveMessage(string Text, int Stage, DateTime Time);

public class ProactiveManager
{
    // Tarayiciya teslim edilmesi bekleyen mesajlar
    private readonly ConcurrentQueue<ProactiveMessage> _kuyruk = new();

    private readonly Settings _settings;
    private readonly string _persona;
    private readonly Brain _brain;
    private readonly LmStudio _lmStudio;
    private readonly Stage _stage;

    // Windows bildirimi gonderme islevi
    private readonly Action<string> _bildirim;

    // Kullanicinin son mesajinin zamani
    public DateTime SonKullaniciMesaji { get; private set; } = DateTime.MinValue;

    // Sayac: ne zaman tetiklenecek (MinValue = baslamadi)
    private DateTime _sayac = DateTime.MinValue;

    // Kullanici su an cevap mi bekliyor? (bekliyorsa turu atla)
    public bool Bekliyor { get; set; } = false;

    public ProactiveManager(
        Settings settings,
        string persona,
        Brain brain,
        LmStudio lmStudio,
        Stage stage,
        Action<string> bildirim)
    {
        _settings = settings;
        _persona = persona;
        _brain = brain;
        _lmStudio = lmStudio;
        _stage = stage;
        _bildirim = bildirim;
    }

    // ------------------------------------------------------------
    //  KullaniciMesajiAldi  -  Kullanici mesaj gonderdi
    // ------------------------------------------------------------
    public void KullaniciMesajiAldi()
    {
        SonKullaniciMesaji = DateTime.Now;

        // Sayaci sifirla: kullanici konusuyorsa bekliyoruz.
        _sayac = DateTime.MinValue;
    }

    // ------------------------------------------------------------
    //  TryDequeue  -  Bekleyen mesaji al
    // ------------------------------------------------------------
    public bool TryDequeue(out ProactiveMessage? mesaj)
    {
        return _kuyruk.TryDequeue(out mesaj);
    }

    // ------------------------------------------------------------
    //  RunAsync  -  Arka plan dongusu
    // ------------------------------------------------------------
    public async Task RunAsync(CancellationToken ct)
    {
        // Kontrol sikligi (30 sn yeterli, 2 saatlik bir olcekte)
        using var bekleme = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await bekleme.WaitForNextTickAsync(ct))
        {
            if (!_settings.ProactiveEnabled) continue;

            // Kullanici bir cevap bekliyorsa bolme
            if (Bekliyor) continue;

            if (!ShouldFire(DateTime.Now)) continue;

            try
            {
                ProactiveMessage? mesaj = await GenerateAsync(ct);

                if (mesaj is null)
                {
                    Console.WriteLine("[PROAKTIF] Mesaj uretilemedi, tur atlandi.");
                    continue;
                }

                // Kuyruga koy (tarayici bir sonraki kontrolunde alir)
                _kuyruk.Enqueue(mesaj);

                // Windows bildirimi (sekme kapali olsa bile haberiniz olur)
                _bildirim(mesaj.Text);

                Console.WriteLine($"[PROAKTIF] Asama {mesaj.Stage}: {mesaj.Text}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                // LM Studio kapali olabilir, sonraki turda tekrar dener
                Console.WriteLine($"[PROAKTIF] Model hatasi: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PROAKTIF] Hata: {ex.Message}");
            }
        }
    }

    // ------------------------------------------------------------
    //  ShouldFire  -  Tetiklenmeli mi?
    // ------------------------------------------------------------
    private bool ShouldFire(DateTime now)
    {
        // 1) Kullanici son X dakikada konustuysa bekle
        //    (X = config'deki "proactiveQuietMinutes")
        int sessizlikDk = _settings.ProactiveQuietMinutes;

        if (sessizlikDk > 0 && now - SonKullaniciMesaji < TimeSpan.FromMinutes(sessizlikDk))
        {
            _sayac = DateTime.MinValue;
            return false;
        }

        // 2) Sayac baslamadiysa baslat (2 saat + kucuk bir rastgelelik)
        if (_sayac == DateTime.MinValue)
        {
            int dakika = _settings.ProactiveIntervalMinutes
                       + Random.Shared.Next(0, _settings.ProactiveJitterMinutes + 1);

            _sayac = now.AddMinutes(dakika);
            Console.WriteLine($"[PROAKTIF] Sayac basladi: {_sayac:HH:mm}");
            return false;
        }

        // 3) Sayac dolduysa tetikle
        if (now >= _sayac)
        {
            int dakika = _settings.ProactiveIntervalMinutes
                       + Random.Shared.Next(0, _settings.ProactiveJitterMinutes + 1);

            _sayac = now.AddMinutes(dakika);
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------
    //  GenerateAsync  -  Proaktif mesaji uret
    // ------------------------------------------------------------
    private async Task<ProactiveMessage?> GenerateAsync(CancellationToken ct)
    {
        // 1-10 arasinda rastgele asama
        int asama = Random.Shared.Next(1, _settings.StageCount + 1);

        // Konusmanin asamasini da buraya al (tutarlilik:
        // "yataktayim" dediyse, cevabin da gece modunda gelsin)
        _stage.SetTo(asama);

        // O asamanin ortam metni
        string atmos = await _stage.GetAtmosphereAsync(ct);

        if (string.IsNullOrWhiteSpace(atmos)) return null;

        // Sistem promptu = karakter + asama ortami
        string sistem = _persona + "\n\n" + atmos.Trim();

        // Tetikleyici (kullanicidan geliyormus gibi gorunmesin)
        // DIKKAT: "Write only what she sends" tek basina modeli
        // "mektup yazma" moduna sokuyor ve mesajlar uzuyor.
        // Kisa ve paragrafsiz olmasini da acikca soylemeliyiz.
        const string tetikleyici =
            "[Nova is bored and reaches out to him first, unprompted, " +
            "about something from her day. Write only what she sends. " +
            "One message. 1-3 short sentences. No line breaks, no paragraphs.]";

        // Modele sor
        string cevap = await _lmStudio.ChatAsync(
            sistem, new List<ChatMessage>(), tetikleyici, ct);

        if (string.IsNullOrWhiteSpace(cevap)) return null;

        // Brain'a yaz (proaktif olarak isaretli -> 5 dk sonra temizlenir)
        _brain.AddProactive(0, cevap);

        return new ProactiveMessage(cevap, asama, DateTime.Now);
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) ConcurrentQueue nedir?

     "Thread-safe" bir kuyruk. Birden fazla is parcacigi
     (zamanlayici + web istekleri) ayni anda ekleme/cikarma
     yapabilir ve veri bozulmaz.

     Enqueue -> kuyruga ekle
     TryDequeue -> kuyruktan bir tane al (yoksa false doner)

  2) PeriodicTimer nedir?

     using var bekleme = new PeriodicTimer(TimeSpan.FromSeconds(30));

     Belirtilen surede bir bir WaitForNextTickAsync() cagrilir.
     Her cagri false donerse dongu biter.
     "using" ile otomatik temizlenir.

  3) Action<string> nedir?

     Bir metotu parametre olarak tasimak. Biz burada
     Windows bildirimini veriyoruz:

         bildirim: mesaj => notification.Show("Nova", mesaj)

     Boylece ProactiveManager, bildirimin NASIL gonderilecegini
     bilmek zorunda kalmaz.

  4) Neden 10 dakika kurali var?

     Cunku kullanici seninle konusurken Nova arada bir mesaj
     atip sohbeti bolmesin. Once sessizlik olsun, sonra Nova
     konusmaya baslasin.

  5) DateTime.MinValue ne demek?

     "Zamanin baslangici", yani "hic olmadi" anlamina gelir.
     Bir tarihi karsilastirmada kullanilirsa fark cok buyuk
     cikar -> "cok eski" anlamina gelir.
 ---------------------------------------------------------
*/
