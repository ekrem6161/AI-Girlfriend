/*
 ============================================================
  AI GIRLFRIEND - Ana Program                   (Ana)
 ============================================================
  KONU : Top-level statements, async/await dongu, hata yonetimi
 ============================================================

  PROBLEM:
  Telegram'dan mesaj al -> modele gonder -> cevabi geri gonder.
  Bunu sonsuza kadar tekrarla.

  AKIS:
      1) Ayalari oku (config.json), karakteri oku (persona.md)
      2) Botun baglantisini test et (getMe)
      3) Eski mesajlari atla
      4) Sonsuz dongu:
           getUpdates -> mesaj varsa isle -> cevabi gonder

  ============================================================
  DOSYALAR
  ============================================================
      config.json   ayarlar (token, model, ...)
      persona.md    karakter / sistem promptu
      Settings.cs   ayarlari okuyan sinif
      Brain.cs      konusma hafizasi
      Telegram.cs   Telegram API istemcisi
      LmStudio.cs   model API istemcisi
  ============================================================
*/

using System.Text;

// Windows konsol varsayilan olarak ASCII karakter kullaniyor.
// Bu ayar olmadan emoji'ler "?" olarak gorunur.
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// ============================================================
//  BOLUM 1 - HAZIRLIK
// ============================================================

// Test modu:   dotnet run -- test
// Telegram'a girmeden, konsoldan yazarak botu denemene izin verir.
// (Token gerekmez. Konusma mantigi ayni calisir.)
bool testMode = args.Any(a => a.Equals("test", StringComparison.OrdinalIgnoreCase));

// Web modu:    dotnet run -- web
// Tarayicida sohbet edersin. Token gerekmez.
// Bilgisayar/ada ayni Wi-Fi'dan telefondan da acabilirsin.
bool webMode = args.Any(a => a.Equals("web", StringComparison.OrdinalIgnoreCase));

Console.WriteLine("============================================");
Console.WriteLine("   AI GIRLFRIEND  -  Nova");
Console.WriteLine("============================================");

if (testMode)
{
    Console.WriteLine("   MOD: TEST (konsol) - Telegram kullanilmiyor");
    Console.WriteLine("============================================");
}
else if (webMode)
{
    Console.WriteLine("   MOD: WEB (tarayici)");
    Console.WriteLine("============================================");
}

// Dosyalar programin yaninda (derlenen klasorde) aranir.
// Boylece hangi klasorden calistirirsan calistir ayni dosyalar bulunur.
string baseDir = AppContext.BaseDirectory;

// --- Ayarlari oku ---
string configPath = Path.Combine(baseDir, "config.json");
Settings settings = Settings.Load(configPath);

if (!File.Exists(configPath))
{
    Console.WriteLine($"[HATA] config.json bulunamadi: {configPath}");
    return 1;
}

// --- Token var mi? (test ve web modunda gerekmez) ---
if (!testMode && !webMode && string.IsNullOrWhiteSpace(settings.TelegramBotToken))
{
    Console.WriteLine("[HATA] config.json icinde telegramBotToken bos!");
    Console.WriteLine("        @BotFather'dan aldigin tokeni oraya yaz.");
    Console.WriteLine("        (Token olmadan denemek icin: dotnet run -- test)");
    Console.WriteLine("        (Tarayicida denemek icin: dotnet run -- web)");
    return 1;
}

// --- Karakter dosyasini oku ---
string personaPath = Path.Combine(baseDir, settings.PersonaFile);

if (!File.Exists(personaPath))
{
    Console.WriteLine($"[HATA] Karakter dosyasi bulunamadi: {personaPath}");
    return 1;
}

// persona.md icinde "---" sonrasi olan kisim sistem promptudur.
// (Dosyanin ustundeki Turkce aciklama modele gonderilmez.)
string personaRaw = await File.ReadAllTextAsync(personaPath);
string persona = ExtractSystemPrompt(personaRaw);

Console.WriteLine($"Karakter   : {settings.PersonaFile} ({persona.Length} karakter)");
Console.WriteLine($"Model      : {settings.Model}");
Console.WriteLine($"Reasoning  : {(string.IsNullOrEmpty(settings.ReasoningEffort) ? "model varsayilani" : settings.ReasoningEffort)}");
Console.WriteLine($"Gecis      : {settings.MaxHistoryMessages} mesaj");

// ============================================================
//  BOLUM 2 - ISTEMCI OLUSTUR
// ============================================================

// Tek bir HttpClient tum istekler icin kullanilir.
// (Her istekte yeni HttpClient yaratmak performansi bozar.)
var http = new HttpClient
{
    // Telegram getUpdates 30 sn bekleyebilir, LM Studio da yavas
    // olabilir. 120 sn verelim ki asagida zaman asimi olmasi.
    Timeout = TimeSpan.FromSeconds(120)
};

var telegram = new Telegram(http, settings.TelegramBotToken);
var lmStudio = new LmStudio(http, settings);
var brain = new Brain(settings.MaxHistoryMessages, settings.ProactiveExpiryMinutes);

// Windows masaustu bildirimi (sag alt kose)
var notification = new DesktopNotification(settings.DesktopNotifications);

// Gunun asamalari (atmosphere klasorundeki 10 dosya)
var stage = new Stage(Path.Combine(baseDir, "atmosphere"));

// ============================================================
//  BOLUM 2b - WEB MODU
// ============================================================
//  Tarayicida sohbet etmek icin.
//  Burada app.Run() programi bloklar, geri donmez.
//  (Telegram ve test modu bu bloga hic girmez.)

if (webMode)
{
    WebChat.Start(settings, persona, brain, lmStudio, notification, stage);
    return 0;
}
// ============================================================
//  BOLUM 3 - BAGLANTI TESTI
// ============================================================

if (!testMode)
{
    try
    {
        string username = await telegram.GetBotUsernameAsync(ct: CancellationToken.None);
        Console.WriteLine($"Bot        : @{username}");
        Console.WriteLine();
        Console.WriteLine("Baglanti tamam. Mesajlar bekleniyor...");
        Console.WriteLine("(Durdurmak icin Ctrl+C)");
        Console.WriteLine();
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"[HATA] Telegram'a ulasilamadi: {ex.Message}");
        Console.WriteLine("        Token'i kontrol et (config.json).");
        return 1;
    }
}
else
{
    Console.WriteLine("Test modu. Mesajini yaz ve Enter'a bas. (cikmak icin: exit)");
    Console.WriteLine();
}

// ============================================================
//  BOLUM 4 - ESKI MESAJLARI ATLA
// ============================================================

int offset = 0;

if (!testMode && settings.SkipOldMessagesOnStart)
{
    try
    {
        int latest = await telegram.GetLatestUpdateIdAsync(CancellationToken.None);
        offset = latest + 1;
        Console.WriteLine($"Eski mesajlar atlandi. Offset = {offset}");
    }
    catch (HttpRequestException)
    {
        // Atlayamazsak 0'dan devam ederiz, sorun degil.
    }
}

// ============================================================
//  BOLUM 5 - CTRL+C ILE KAPATMA
// ============================================================

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    // e.Cancel = true  ->  program normal sekilde kapanir (zorla degil)
    // Test modunda Console.ReadLine() bekliyor oldugu icin
    // iptal edemeyiz; orada varsayilan davranis (kapat) daha iyi.
    if (testMode) return;

    e.Cancel = true;
    cts.Cancel();
};

// ============================================================
//  BOLUM 6 - ANA DONGU
// ============================================================

Console.WriteLine();
Console.WriteLine("--- beklemede ---");
Console.WriteLine();

while (!cts.IsCancellationRequested)
{
    // --------------------------------------------------------
    //  TEST MODU: mesaji konsoldan al
    // --------------------------------------------------------
    if (testMode)
    {
        Console.Write("> ");
        string? giris = Console.ReadLine();

        if (giris is null) break;                       // konsol kapandi
        if (string.IsNullOrWhiteSpace(giris)) continue; // bos satir
        if (giris.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        // chatId = 0  ->  test kanalindaki tek "sohbet"
        await HandleMessageAsync(new TelegramMessage(0, 0, giris));
        continue;
    }

    try
    {
        // 30 sn bekle, yeni mesaj gelirse don
        List<TelegramUpdate> updates =
            await telegram.GetUpdatesAsync(offset, timeoutSeconds: 30, ct: cts.Token);

        foreach (TelegramUpdate update in updates)
        {
            // offset her zaman son islenen update_id + 1 olmali.
            // (Aksi halde Telegram ayni mesaji tekrar gonderir.)
            offset = (int)update.UpdateId + 1;

            if (update.Message is null)
                continue;

            await HandleMessageAsync(update.Message);
        }
    }
    catch (TaskCanceledException)
    {
        // Bu iki durum olabilir:
        //   1) Ag yavas, istek zaman asimi   -> tekrar dene
        //   2) Ctrl+C basildi, istek iptal   -> programdan cik
        // (TaskCanceledException, OperationCanceledException'dan
        //  turedigi icin ONCE bu yazilir.)
        if (cts.IsCancellationRequested)
            break;

        Console.WriteLine("[UYARI] Istek zaman asimi, tekrar deneniyor...");
        await DelayAsync(3);
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C basildi, programdan cik.
        break;
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"[HATA] Telegram hatasi: {ex.Message}");
        await DelayAsync(5);
    }
}

Console.WriteLine();
Console.WriteLine("Gorusmek uzere!");

return 0;


// ============================================================
//  YARDIMCI: Mesaji isle
// ============================================================
async Task HandleMessageAsync(TelegramMessage msg)
{
    string kim = testMode ? "SEN" : $"[{msg.ChatId}]";
    Console.WriteLine($"{kim} Geldi  : {msg.Text}");

    // --- Ilk mesaj: karsilama ---
    if (msg.Text == "/start")
    {
        const string hosgeldin =
            "Hey you! 👋 I've been waiting for you. " +
            "How's your day going so far? Tell me everything.";

        if (testMode)
        {
            Console.WriteLine($"NOVA       : {hosgeldin}");
        }
        else
        {
            await telegram.SendMessageAsync(msg.ChatId, hosgeldin, cts.Token);
        }

        return;
    }

    // "Yaziyor..." efekti (sadece Telegram'da)
    if (settings.TypingIndicator && !testMode)
    {
        try
        {
            await telegram.SendChatActionAsync(msg.ChatId, "typing", cts.Token);
        }
        catch (HttpRequestException)
        {
            // Efekt gonderilemese de devam et.
        }
    }

    // --- Modele sor ---
    string reply;

    var sw = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        // Onceki mesajlar (bu mesaj henuz eklenmedi) + yeni mesaj
        List<ChatMessage> history = brain.GetHistory(msg.ChatId);

        reply = await lmStudio.ChatAsync(persona, history, msg.Text, cts.Token);
    }
    catch (HttpRequestException ex)
    {
        // LM Studio kapali ya da model yuklenemedi
        Console.WriteLine($"[HATA] Model hatasi: {ex.Message}");

        reply =
            "Hmm, my brain just went offline for a second 😵 " +
            "Give me a moment and try again?";

        if (testMode)
        {
            Console.WriteLine($"NOVA       : {reply}");
            Console.WriteLine();
        }
        else
        {
            try
            {
                await telegram.SendMessageAsync(msg.ChatId, reply, cts.Token);
            }
            catch (HttpRequestException)
            {
                // Telegram da kapali, yapacak bir sey yok.
            }
        }

        return;
    }
    catch (OperationCanceledException)
    {
        return;
    }

    sw.Stop();

    // Model bazen bos cevap doner (dusunme butcesi bitmis olabilir)
    if (string.IsNullOrWhiteSpace(reply))
    {
        reply = "Hmm, I zipped out for a second there... say that again? 😶";
    }

    // --- Cevabi gonder ---
    if (testMode)
    {
        Console.WriteLine($"NOVA       : {reply}   ({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine();
    }
    else
    {
        try
        {
            await telegram.SendMessageAsync(msg.ChatId, reply, cts.Token);
            Console.WriteLine($"{kim} Gonder : {reply}   ({sw.ElapsedMilliseconds} ms)");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[HATA] Mesaj gonderilemedi: {ex.Message}");
            return;
        }
    }

    // --- Hafizaya yaz ---
    brain.Add(msg.ChatId, "user", msg.Text);
    brain.Add(msg.ChatId, "assistant", reply);
}


// ============================================================
//  YARDIMCI: Guvenli bekleme
// ============================================================
//  Program iptal edildiginde (Ctrl+C) Task.Delay hata firlatir.
//  Bu yardimci onu yutar, boylece hata ekrani cikmaz.
async Task DelayAsync(int seconds)
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Iptal edildi. Sorun yok, dongu bir sonra kontrol edip cikar.
    }
}


// ============================================================
//  YARDIMCI: persona.md icinden sistem promptunu ayikla
// ============================================================
static string ExtractSystemPrompt(string raw)
{
    // "---" ayiracini SATIR BASINDA arayiz.
    // (Icinde gecen bir metinde "---" yaziyor olsa bile
    //  yanlis yer kesilmez.)
    string[] satirlar = raw.Split('\n');

    for (int i = 0; i < satirlar.Length; i++)
    {
        if (satirlar[i].Trim() == "---")
        {
            // Ayiractan sonraki her sey sistem promptudur.
            // Ustteki Turkce yorum kismi modele GONDERILMEZ.
            return string.Join('\n', satirlar[(i + 1)..]).Trim();
        }
    }

    // Ayirac yoksa dosyanin tamamini kullan.
    return raw.Trim();
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) Top-level statements nedir?

     Program.cs'in en ustunde dogrudan kod yazabilirsin.
     Klasik C#'da "static void Main()" vardi, artik gerekmiyor.
     Ayrica "using" satirlari da en uste yazilir.

  2) "return 1;" ne demek?

     Top-level statements'ta "return 1" = "programi 1 kodu ile
     bitir" (hata kodu). Basi bir hata mesajiyla donuluyorsa
     boyle yapilir. "return 0" = normal bitis.

  3) await ... ct: cts.Token  yazmamiz gerekiyor

     Her await edilen istege iptal tokeni veriyoruz.
     Boylece Ctrl+C basildiginda bekleyen istek hemen kesiliyor.

  4) Neden while (true) degil de while (!cts.IsCancellationRequested)?

     C#'ta asla "while (true)" yazmaz, "for (;;)" yazarsin
     (derleyici uyarisi verir: "unreachable code"). Burada
     zaten cikis kosulumuz var, o yuzden sorun yok.

  5) Ayirici olarak "---" kullanimi

     persona.md'nin ust kismi senin icin (Turkce yorum),
     alt kismi model icin (sistem promptu).
     Ayirac sayesinde ikisini tek dosyada tutuyoruz.
 ---------------------------------------------------------
*/
