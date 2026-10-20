/*
 ============================================================
  WEB CHAT - Yerel Web Arayuzu                  (Sinif)
 ============================================================
  KONU : ASP.NET Core Minimal API, JSON, async/await
 ============================================================

  PROBLEM:
  Telegram yerine bilgisayarin tarayicisinda sohbet etmek
  istiyoruz. Avantajlari:
      - Hicbir hesap / token gerekmez
      - Ucretsiz, 3. parti servis yok
      - Telefondan ayni Wi-Fi'da PC'nin IP'siyle erisilir

  YAPILAR:
      GET  /            -> index.html (sohbet arayuzu)
      POST /api/chat    -> mesaj gonder, cevap al
      POST /api/reset   -> sohbeti sifirla
      GET  /api/health  -> calisiyor mu kontrolu

  ============================================================
  MINIMAL API NEDIR?
  ============================================================
  ASP.NET Core'da klasik yontem:
      -> Startup.cs / Controller / [HttpPost] attribute'leri
  Minimal API ile:
      app.MapPost("/api/chat", (veri) => { ... });
  Tek satirlik, dosya klasoru yok, anlasilmasi kolay.

  ============================================================
  IKI KISI ÖNEMLI DETAY
  ============================================================
  1) SIRALAMA (gate)
     Birden fazla sekme acarsan ayni anda 2 istek gelebilir.
     Model ayni anda 2 istegi kaldiramaz (VRAM) ve hafiza
     (Brain) thread-safe degil. Bu yuzden istekleri bir kapi gibi
     tek tek siraya koyuyoruz.

  2) TEK KULLANICI
     Simdilik tek sohbet (chatId = 0) var. Ilerde her tarayiciya
     ayri sohbet acilabilir.
  ============================================================
*/

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

// API'ye gonderilen veri:  { "message": "merhaba" }
public record ChatRequest(string? Message);

public static class WebChat
{
    public static void Start(Settings settings, string persona, Brain brain, LmStudio lmStudio, DesktopNotification notification, Stage stage)
    {
        // Tek seferde sadece bir istek islenir (aciklama yukarida).
        var gate = new SemaphoreSlim(1, 1);

        // Adres, port ve klasor ayarlari.
        // DIKKAT: ContentRoot "builder" olusturulduktan SONRA degistirilemez.
        // O yuzden WebApplicationOptions ile en basta veriyoruz.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Programin yanindaki dosyalari (config.json, persona.md) okusun
            ContentRootPath = AppContext.BaseDirectory,

            // wwwroot klasoru (arayuz dosyasi) icin
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        // Adres ve port
        builder.WebHost.UseUrls($"http://{settings.WebHost}:{settings.WebPort}");

        var app = builder.Build();

        // --------------------------------------------------------
        //  PROAKTIF MESAJLASMA (arka plan)
        // --------------------------------------------------------
        //  Nova bazi zamanlar kendi basina mesaj atar.
        //  (Endpoint'lerden ONCE olusturulmali, cunku /api/proactive
        //   onu kullanir.)
        var proactive = new ProactiveManager(
            settings,
            persona,
            brain,
            lmStudio,
            stage,
            mesaj => notification.Show(settings.NotificationTitle, mesaj));

        // Program kapandiginda arka plan dongusu de dursun
        var proactiveCts = new CancellationTokenSource();
        app.Lifetime.ApplicationStopping.Register(() => proactiveCts.Cancel());

        _ = proactive.RunAsync(proactiveCts.Token);

        // wwwroot klasorundeki index.html'i servis et.
        // DIKKAT: UseStaticFiles() tek basina "/" adresini "index.html"
        // dosyasina baglamaz. Bunun icin ONCESINDE UseDefaultFiles()
        // gelmeli. (Sira onemli: once Default, sonra Static.)
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // --------------------------------------------------------
        //  GET /api/debug  -  Klasor ayarlarini goster
        // --------------------------------------------------------
        app.MapGet("/api/debug", (IWebHostEnvironment env) => Results.Ok(new
        {
            contentRoot = env.ContentRootPath,
            webRoot = env.WebRootPath,
            webRootExists = Directory.Exists(env.WebRootPath),
            indexExists = File.Exists(Path.Combine(env.WebRootPath, "index.html"))
        }));

        // --------------------------------------------------------
        //  GET /api/prompt  -  Modele ne gonderiliyor? (kontrol)
        // --------------------------------------------------------
        //  "Turkce bir sey siziyor mu?" sorusunun cevabi burada.
        //  Burada gordugun metin BIREBIR modele gonderilen metindir.
        app.MapGet("/api/prompt", () => Results.Ok(new
        {
            length = persona.Length,
            lines = persona.Split('\n').Length,
            systemPrompt = persona
        }));

        // --------------------------------------------------------
        //  GET /api/health  -  Calisiyor mu?
        // --------------------------------------------------------
        app.MapGet("/api/health", () => Results.Ok(new
        {
            ok = true,
            model = settings.Model,
            reasoning = settings.ReasoningEffort
        }));

        // --------------------------------------------------------
        //  GET /api/stage  -  Nova su an nerede?
        // --------------------------------------------------------
        app.MapGet("/api/stage", async (CancellationToken ct) =>
        {
            string ad = await stage.GetDisplayNameAsync(ct);

            return Results.Ok(new
            {
                number = stage.Number,
                name = ad,
                file = stage.DosyaAdi
            });
        });

        // --------------------------------------------------------
        //  POST /api/reset  -  Sohbeti sifirla
        // --------------------------------------------------------
        app.MapPost("/api/reset", () =>
        {
            brain.Clear(0);
            return Results.Ok(new { ok = true });
        });

        // --------------------------------------------------------
        //  GET /api/proactive  -  Nova'nin kendi mesaji var mi?
        // --------------------------------------------------------
        //  Tarayici her 15 saniyede bir burayi sorar.
        app.MapGet("/api/proactive", () =>
        {
            if (proactive.TryDequeue(out var mesaj) && mesaj is not null)
            {
                return Results.Ok(new
                {
                    text = mesaj.Text,
                    stage = mesaj.Stage,
                    time = mesaj.Time
                });
            }

            // Bekleyen mesaj yok
            return Results.Ok(new { text = (string?)null, stage = 0 });
        });

        // --------------------------------------------------------
        //  POST /api/chat  -  Mesaj gonder, cevap al
        // --------------------------------------------------------
        //  NOT: Async lambda'nin donus tipi "async Task<IResult> (...) =>"
        //       seklinde olmali (IResult degil, Task<IResult>).
        app.MapPost("/api/chat", async Task<IResult> (ChatRequest? req, HttpContext ctx) =>
        {
            // --- 1) Girdi kontrolu ---
            if (req is null || string.IsNullOrWhiteSpace(req.Message))
            {
                return Results.BadRequest(new { error = "Mesaj bos gonderilemez." });
            }

            // --- 2) Sira kapisini bekle ---
            await gate.WaitAsync();

            try
            {
                var sw = Stopwatch.StartNew();

                // --- Kullanici konustu ---
                // Proaktif sayac sifirlansin, Nova beklemesin.
                proactive.KullaniciMesajiAldi();

                // --- Asama kontrolu ---
                // Her N mesajda bir sonraki asamaya gecilir.
                // (TOPLAM sayac kullanilir, kayan pencere degil)
                stage.AdvanceIfNeeded(
                    brain.GetTotalMessageCount(0), settings.StageAdvanceEveryMessages);

                // Sistem promptu = karakter + su anki asamanin ortami
                string systemPrompt = await BuildSystemPromptAsync(
                    persona, stage, ctx.RequestAborted);

                // Onceki mesajlar + yeni mesaj
                List<ChatMessage> history = brain.GetHistory(0);

                // Cevap beklerken Nova araya girip bolmesin
                proactive.Bekliyor = true;

                string reply = await lmStudio.ChatAsync(
                    systemPrompt, history, req.Message!, ctx.RequestAborted);

                sw.Stop();
                proactive.Bekliyor = false;

                // Bos cevap gelirse yedek mesaj
                if (string.IsNullOrWhiteSpace(reply))
                {
                    reply = "Hmm, I zipped out for a second there... say that again?";
                }

                // Hafizaya yaz
                brain.Add(0, "user", req.Message!);
                brain.Add(0, "assistant", reply);

                // --- Windows bildirimi (sag alt kose) ---
                // Tarayiciya bakmiyor olsak bile haberimiz olur.
                notification.Show(settings.NotificationTitle, reply);

                return Results.Ok(new
                {
                    reply = reply,
                    ms = sw.ElapsedMilliseconds,
                    stage = stage.Number
                });
            }
            catch (HttpRequestException ex)
            {
                // LM Studio kapali veya model yuklenemedi
                Console.WriteLine($"[HATA] Model hatasi: {ex.Message}");

                return Results.Json(
                    new { error = "Modele ulasilamadi. LM Studio acik mi? (" + ex.Message + ")" },
                    statusCode: 503);
            }
            catch (OperationCanceledException)
            {
                // Tarayici sekmeyi kapatti
                return Results.Json(new { error = "Iptal edildi." }, statusCode: 499);
            }
            finally
            {
                // Kapiyi bir sonraki istek icin ac
                gate.Release();

                // Bekleme bayragini her durumda kapat
                proactive.Bekliyor = false;
            }
        });

        // --------------------------------------------------------
        //  Baslat
        // --------------------------------------------------------
        string localUrl = $"http://localhost:{settings.WebPort}";
        var lanIps = GetLanIps();

        Console.WriteLine();
        Console.WriteLine("============================================");
        Console.WriteLine("   NOVA - Web Chat");
        Console.WriteLine("============================================");
        Console.WriteLine($"Bilgisayar : {localUrl}");

        foreach (string ip in lanIps)
        {
            Console.WriteLine($"Telefon    : http://{ip}:{settings.WebPort}");
        }

        Console.WriteLine();
        if (settings.ProactiveEnabled)
        {
            Console.WriteLine($"Proaktif   : acik (her {settings.ProactiveIntervalMinutes} dk," +
                              $" {settings.ProactiveQuietMinutes} dk sessizlikten sonra)");
        }
        else
        {
            Console.WriteLine("Proaktif   : kapali");
        }

        Console.WriteLine("Modele gonderilen karakter metni:  /api/prompt");
        Console.WriteLine("Durdurmak icin Ctrl+C");
        Console.WriteLine();

        // Sunucu baslayinca tarayiciyi otomatik ac
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                // UseShellExecute = true : tarayiciyi varsayilan uygulamayla ac
                Process.Start(new ProcessStartInfo(localUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Tarayici acilamadi, sorun degil. Adresi elle yazabilirsin.
            }
        });

        app.Run();
    }

    // ------------------------------------------------------------
    //  BuildSystemPromptAsync  -  Karakter + asama ortami
    // ------------------------------------------------------------
    //  Modele giden sistem promptu iki parca:
    //      1) persona.md  -> kim oldugu, nasil konusacagi
    //      2) atmosphere  -> su an hangi asamada, ortam nasil
    //  Ortam EN SONA eklenir, cunku model en taze bilgiye
    //  daha cok dikkat eder.
    private static async Task<string> BuildSystemPromptAsync(
        string persona, Stage stage, CancellationToken ct)
    {
        string atmos = await stage.GetAtmosphereAsync(ct);

        if (string.IsNullOrWhiteSpace(atmos))
        {
            return persona;
        }

        return persona + "\n\n" + atmos.Trim();
    }

    // ------------------------------------------------------------
    //  GetLanIps  -  Bu bilgisayarin ag adreslerini bul
    // ------------------------------------------------------------
    //  Telefondan baglanmak icen bu adresleri bilmemiz lazim.
    //  Ornegin: 192.168.1.106 (Wi-Fi)  ve  100.94.34.25 (Tailscale)
    private static List<string> GetLanIps()
    {
        var adresler = new List<string>();

        try
        {
            foreach (IPAddress adres in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                // Sadece IPv4, ve "loopback" (127.0.0.1) olmayanlar
                if (adres.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(adres))
                {
                    adresler.Add(adres.ToString());
                }
            }
        }
        catch (Exception)
        {
            // Ag bilgisi alinamadi, sorun degil.
        }

        if (adresler.Count == 0)
        {
            adresler.Add("BILINMIYOR");
        }

        return adresler;
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) "MapPost" nedir?

        app.MapPost("/api/chat", handler);

     Bir adresi bir metoda baglar. Yani:
        tarayici  POST /api/chat   ->  bu metot calisir
     "Get" ile okuma, "Post" ile veri gonderme yapar.

  2) Iki parametre nereden geliyor?

        async (ChatRequest? req, HttpContext ctx) -> IResult

     - "req" : gonderilen JSON, otomatik olarak ChatRequest'e cevrilir
       ({ "message": "..." } -> req.Message)
     - "ctx" : HTTP isteginin detaylari (IP, iptal tokeni vs.)
     - "-> IResult" : metodun ne dondurecegini belirtiyoruz
       (Results.Ok / Results.BadJson / Results.Json hepsi IResult)

  3) "await gate.WaitAsync()" nedir?

     SemaphoreSlim = ayni anda sadece 1 istek gecsin.
     Ilk istek girer, digerleri siraya bekler.
     "finally" icinde Release() ile kapi tekrar acilir.
     (finally = hata olsa bile calisir, kapi kilitli kalmaz.)

  4) Results.Ok / BadJson / Json farki

     Results.Ok(...)        -> 200 basarili
     Results.BadJson(...)   -> 400 hatali istek
     Results.Json(..., 503)  -> 503 servis kullanilamiyor
     Tarayici JS tarafinda "response.ok" ile bunlari ayirt ederiz.

  5) Neden "Microsoft.NET.Sdk" degil de "Web" degil?

     Web SDK ek olarak bir sifir ayar dosyasi (launchSettings) ve
     gelistirme sunucusu ekler. Bizim ihtiyacimiz sadece ASP.NET
     motoru. FrameworkReference ile onu elle ekledik, proje
     yine bir konsol uygulamasi olarak kaliyor (Telegram ve
     test modlari calismaya devam ediyor).
 ---------------------------------------------------------
*/
