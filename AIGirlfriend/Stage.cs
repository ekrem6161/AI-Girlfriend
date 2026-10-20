/*
 ============================================================
  STAGE - Gunun Asamalari ve Prompt Yukleme      (Sinif)
 ============================================================
  KONU : class, dosya okuma, Dictionary (onbellek)
 ============================================================

  PROBLEM:
  Nova'nin gunu 10 asamadan olusuyor. Sabah kalktiginda
  baska, okuldayken baska, gece yatakta baska davranir.
  Her asamada modele o asamanin ORTAMINI gondermemiz lazim.

  COZUM:
  "atmosphere" klasorunde 10 dosya var:
      stage-01-sabah.md
      stage-02-durak.md
      ...
      stage-10-gece.md

  Bu sinif:
      1) Hangi asamada oldugunu tutar (bir tam sayi)
      2) O asamaya ait dosyayi okur (bir kez, onbellekten)
      3) Icerigi dondurur, ki persona'ya eklenip modele gonderilsin

  ============================================================
  SAYAÇ NASIL ISLIER?
  ============================================================
      0 -> 1 -> 2 -> ... -> 9 -> 0 -> 1 -> ...

  Yani 10 dosya tam tur yapiyor.
  Sayac 10'a ulasirsa sifirlanir.

  Dosya eslesmesi:
      sayaç 0  -> stage-01
      sayaç 1  -> stage-02
      ...
      sayaç 9  -> stage-10

  ============================================================
  NE ZAMAN ILERLER?
  ============================================================
  Her "stageAdvanceEveryMessages" mesajda bir ilerler.
  (config.json'dan ayarlanir, varsayilan 10)
  ============================================================
*/

public class Stage
{
    // En fazla 10 asama
    public const int MaxStage = 10;

    // atmosphere klasorunun tam yolu
    private readonly string _atmosphereDir;

    // Okunan dosyalari bir kez okuyup burada saklariz.
    // (Her mesajda dosyayi tekrar okumaya gerek kalmasin)
    private readonly Dictionary<int, string> _cache = new();

    // 0-9 arasindaki asama sayaci
    public int Current { get; private set; } = 0;

    public Stage(string atmosphereDir)
    {
        _atmosphereDir = atmosphereDir;
    }

    // ------------------------------------------------------------
    //  Next  -  Sonraki asamaya gec
    // ------------------------------------------------------------
    //  0 -> 1 -> 2 -> ... -> 9 -> 0
    public void Next()
    {
        Current++;

        // 10'a ulastik, sifirla
        if (Current >= MaxStage)
        {
            Current = 0;
        }

        Console.WriteLine($"[SAHNE] Yeni asama: {Number} ({DosyaAdi})");
    }

    // ------------------------------------------------------------
    //  SetTo  -  Belirli bir asamaya atla
    // ------------------------------------------------------------
    //  Proaktif mesaj uretildiginde kullanilir:
    //  Nova "yataktayim" dediyse, sohbetin asamasini da
    //  oraya aliyoruz ki cevaplar tutarli olsun.
    public void SetTo(int number)
    {
        if (number < 1) number = 1;
        if (number > MaxStage) number = MaxStage;

        Current = number - 1;

        Console.WriteLine($"[SAHNE] Atlandi: {Number} ({DosyaAdi})");
    }

    // ------------------------------------------------------------
    //  Number  -  Su anki asamanin numarasi (1-10)
    // ------------------------------------------------------------
    public int Number => Current + 1;

    // ------------------------------------------------------------
    //  DosyaAdi  -  Asamanin dosya adi (ornek: stage-03-otobus)
    // ------------------------------------------------------------
    public string DosyaAdi => $"stage-{Number:D2}";

    // ------------------------------------------------------------
    //  GetAtmosphereAsync  -  Asamanin ortam metnini getir
    // ------------------------------------------------------------
    //  Donen deger sistem promptunun SONUNA eklenir.
    public async Task<string> GetAtmosphereAsync(CancellationToken ct)
    {
        int n = Number;

        // Onbellekte varsa hemen don
        if (_cache.TryGetValue(n, out var onbellek))
        {
            return onbellek;
        }

        string icerik = "";

        if (Directory.Exists(_atmosphereDir))
        {
            // "stage-03-*.md" -> stage-03-otobus.md gibi
            string[] eslesen = Directory.GetFiles(
                _atmosphereDir, $"stage-{n:D2}-*.md");

            if (eslesen.Length > 0)
            {
                icerik = await File.ReadAllTextAsync(eslesen[0], ct);
            }
            else
            {
                Console.WriteLine($"[UYARI] {DosyaAdi}*.md bulunamadi.");
            }
        }
        else
        {
            Console.WriteLine($"[UYARI] atmosphere klasoru yok: {_atmosphereDir}");
        }

        _cache[n] = icerik;
        return icerik;
    }

    // ------------------------------------------------------------
    //  GetDisplayNameAsync  -  Asamanin okunabilir adi
    // ------------------------------------------------------------
    //  Atmosfer dosyasinin ilk satirindan alinir:
    //      "### STAGE 5 - Lunch break"  ->  "Lunch break"
    //  (Ayrı bir isim tablosu tutmaya gerek yok, dosyada zaten var.)
    public async Task<string> GetDisplayNameAsync(CancellationToken ct)
    {
        string icerik = await GetAtmosphereAsync(ct);

        if (string.IsNullOrWhiteSpace(icerik))
        {
            return $"Stage {Number}";
        }

        string ilkSatir = icerik.Split('\n')[0].Trim();

        // "### STAGE 5 - Lunch break" -> tire karakterinden sonrasi
        int tire = ilkSatir.IndexOf('-');

        if (tire >= 0 && tire + 1 < ilkSatir.Length)
        {
            string ad = ilkSatir[(tire + 1)..].Trim();
            return ad.Length > 0 ? ad : $"Stage {Number}";
        }

        return $"Stage {Number}";
    }

    // ------------------------------------------------------------
    //  AdvanceIfNeeded  -  Mesaj sayisina gore ilerle
    // ------------------------------------------------------------
    //  Her "herNMesaj" mesajda bir asama ilerletir.
    public void AdvanceIfNeeded(int mesajSayisi, int herNMesaj)
    {
        if (herNMesaj <= 0) return;
        if (mesajSayisi <= 0) return;

        // 10 mesajda bir: 10, 20, 30...
        if (mesajSayisi % herNMesaj == 0)
        {
            Next();
        }
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) private set ne demek?

        public int Current { get; private set; } = 0;

     - "get" herkes okuyabilir
     - "private set" sadece BU SINIF icinde degistirilebilir
       (disaridan "stage.Current = 5" yazilamaz)
     - Disaridan sadece Next() cagrilir, kurallar bozulmaz.

  2) Dictionary onbellegi nedir?

        _cache[n] = icerik;

     Anahtar (n) -> deger (icerik).
     Ayni dosyayi ikinci kez istedigimizde diskten okumuyoruz.
     Onbellek = "sonra tekrar kullanmak uzere saklamak".

  3) string interpolation

        $"stage-{Number:D2}-*.md"

     - {Number} -> sayinin degeri
     - :D2 -> sayiyi 2 haneli yaz (1 -> "01", 10 -> "10")
     - Yani: "stage-03-*.md"

  4) async / await

        await File.ReadAllTextAsync(dosya, ct);

     Dosyayi okuma islemi programi bloklamaz, o sirada
     baska isler de yapilabilir. ct (cancellation token) ile
     iptal edilebilir.
 ---------------------------------------------------------
*/
