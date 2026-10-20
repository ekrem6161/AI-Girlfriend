/*
 ============================================================
  SETTINGS - Ayarlar Dosyasi                    (Sinif)
 ============================================================
  KONU : class, JSON okuma (System.Text.Json), Property
 ============================================================

  PROBLEM:
  Programin ayarlari (token, model adi, sifir sayisi gibi)
  koda gomulmesin. Ayarlar "config.json" adli ayri bir
  dosyada tutulsun. Boylece kodu degistirmeden ayar
  degistirebilirsin.

  DOSYA YAPISI (config.json):
      {
        "telegramBotToken": "123:ABC...",
        "model": "qwen3.5abl",
        "maxTokens": 500
      }

  ============================================================
  NOT: JSON'daki isimler "camelCase", C#'taki isimler
  "PascalCase". PropertyNameCaseInsensitive = true
  ayari sayesinde iki farkli yazim birbirini tanir.
  ============================================================
*/

using System.Text.Json;

public class Settings
{
    // --- Telegram ---
    // BotFather'dan alinan token. Bos ise program hata verip cikar.
    public string TelegramBotToken { get; set; } = "";

    // Botun "yaziyor..." efekti gonderip gondermeyecegi
    public bool TypingIndicator { get; set; } = true;

    // Program acildiginda eski (okunmamis) mesajlarin
    // tekrar islenip islenmedigi. true = eskileri atla.
    public bool SkipOldMessagesOnStart { get; set; } = true;

    // --- LM Studio (yerel model) ---
    public string LmStudioBaseUrl { get; set; } = "http://127.0.0.1:1234";

    // Kullanilacak modelin LM Studio'daki adi
    public string Model { get; set; } = "qwen3.5abl";

    // Dusunme modu:
    //   "none"   -> hizli (cevap ~1-2 sn), model dusunmez
    //   "medium" -> yavas (cevap ~30-60 sn), model dusunur
    //   ""       -> parametreyi hiç gonderme (modelin varsayilani)
    public string ReasoningEffort { get; set; } = "none";

    // Cevaplarin cesnisi. 0 = her zaman ayni, 1 = cok cesitli.
    public double Temperature { get; set; } = 0.85;

    // Tek cevapta uretilebilecek en fazla token sayisi
    public int MaxTokens { get; set; } = 500;

    // --- Sohbet ---
    // Modele gonderilen gecmis mesaj sayisi (hafiza)
    public int MaxHistoryMessages { get; set; } = 20;

    // Karakter/sistem promptunun dosya adi (proje kokune gore)
    public string PersonaFile { get; set; } = "prompts/persona.md";

    // --- Asamalar ---
    // Gunun kac asamasi oldugu (atmosphere klasorundeki dosya sayisi)
    public int StageCount { get; set; } = 10;

    // Her kac mesajda bir sonraki asamaya gecilsin
    public int StageAdvanceEveryMessages { get; set; } = 10;

    // --- Proaktif mesajlasma ---
    // Nova kendi kendine mesaj atsin mi?
    public bool ProactiveEnabled { get; set; } = true;

    // Kullanici sessiz kaldiktan sonra kac dakika sonra sayac baslasin
    public int ProactiveQuietMinutes { get; set; } = 10;

    // Proaktif mesajlar arasi bekleme (dakika)
    public int ProactiveIntervalMinutes { get; set; } = 120;

    // Buna kadar rastgele ekleme (dakika) - robotik hissettirmesin
    public int ProactiveJitterMinutes { get; set; } = 15;

    // Cevapsiz proaktif mesaj kac dakika sonra history'den silinsin
    public int ProactiveExpiryMinutes { get; set; } = 5;

    // --- Masaustu bildirimi (Windows bildirimi) ---
    // Nova cevap verdiginde sag altta bildirim cikar mi?
    public bool DesktopNotifications { get; set; } = true;

    // Bildirimde gorunecek isim
    public string NotificationTitle { get; set; } = "Nova";

    // --- Web Chat ---
    // Web sunucusunun adresi.
    // "0.0.0.0" = tum aglar (telefonun ayni Wi-Fi'dan baglanabilmesi icin)
    // "localhost" = sadece bu bilgisayar
    public string WebHost { get; set; } = "0.0.0.0";

    // Web sunucusunun portu
    public int WebPort { get; set; } = 5050;

    // ------------------------------------------------------------
    //  Load  -  config.json dosyasini okuyup Settings nesnesi dondurur
    // ------------------------------------------------------------
    public static Settings Load(string path)
    {
        // Dosya yoksa varsayilan ayarlarla devam et (token zaten bos olur,
        // Program.cs bunu kontrol edip kullaniciya bildirecek).
        if (!File.Exists(path))
            return new Settings();

        string json = File.ReadAllText(path);

        var options = new JsonSerializerOptions
        {
            // JSON'daki "telegramBotToken" ile C#'taki "TelegramBotToken"
            // ayni sayilir. Boylece ayar adlarini iki sekilde yazabilirsin.
            PropertyNameCaseInsensitive = true
        };

        // JSON bozuk olursa program cokmesin, varsayilana don.
        try
        {
            return JsonSerializer.Deserialize<Settings>(json, options) ?? new Settings();
        }
        catch (JsonException)
        {
            Console.WriteLine($"[HATA] {path} okunamadi, varsayilan ayarlar kullanilacak.");
            return new Settings();
        }
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) Auto-ozellik (property) nedir?

        public string Model { get; set; } = "qwen3.5abl";

     - "get" = degeri oku, "set" = degeri degistir
     - "= ..." = baslangic degeri (dosya okunamazsa bu kullanilir)

  2) "config.json" dosyasini bu klasore koyman lazim.
     Dosya yoksa program "config.json bulunamadi" der.
     Ayrica .csproj icine su iki satir eklendi, boylece
     dosya derlenen klasore de kopyalansin:

        <None Update="config.json" CopyToOutputDirectory="PreserveNewest" />
        <None Update="persona.md"  CopyToOutputDirectory="PreserveNewest" />

  3) Token'i bu dosyaya elle yazacaksin. Chat'e yapistirma.
 ---------------------------------------------------------
*/
