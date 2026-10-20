/*
 ============================================================
  TELEGRAM - Bot API Istemcisi                   (Sinif)
 ============================================================
  KONU : HttpClient, async/await, Task, JSON okuma
 ============================================================

  PROBLEM:
  Telegram'da bot olmak icin 3 istek yeterli:
      1) getUpdates      -> yeni mesaj var mi?
      2) sendMessage     -> mesaj gonder
      3) sendChatAction  -> "yaziyor..." efekti

  Bunlarin hepsi ayni adrese gider:
      https://api.telegram.org/bot<TOKEN>/<komut>

  Kodda ek paket (NuGet) kullanmiyoruz. Her sey HttpClient ile.

  ============================================================
  ISTEK / CEVAP YAPISI
  ============================================================
  Telegram "getUpdates" cevabi:

      {
        "ok": true,
        "result": [
          {
            "update_id": 12345,
            "message": {
              "message_id": 7,
              "chat": { "id": 999888, "type": "private" },
              "text": "merhaba"
            }
          }
        ]
      }

  Cevapta "message" olmak zorunda degil (ornegin biri botu
  /start'a cevap verirse "message" yerine baska alanlar gelir).
  Bu yuzden once JsonDocument ile "message" var mi diye bakip
  sonra icinden okuyoruz.

  ============================================================
  LONG POLLING (uzun yoklama)
  ============================================================
  Telegram'da iki yontem var:
      - Webhook : bir sunucu olmali, URL'ye POST atilir (zor)
      - getUpdates : biz sorariz "yeni mesaj var mi?" (kolay)

  getUpdates adresine "timeout=30" yazarsan, Telegram 30 saniye
  bekler. Yeni mesaj yoksa 30 sn sonra bos liste doner.
  Boylece her 30 saniyede bir "polling" yapmaz oluruz.

  >>> DIKKAT: getUpdates asla ayni anda 2 kez cagrilmamali.
  Biz tek bir dongude sirayla isledigimiz icin sorun yok.
  ============================================================
*/

using System.Text.Json;

public record TelegramMessage(int MessageId, long ChatId, string Text);
public record TelegramUpdate(long UpdateId, TelegramMessage? Message);

public class Telegram
{
    // Her istek icin tam adres: https://api.telegram.org/bot<TOKEN>
    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public Telegram(HttpClient http, string token)
    {
        _http = http;
        _baseUrl = $"https://api.telegram.org/bot{token}";
    }

    // ------------------------------------------------------------
    //  GetMeAsync  -  Botun kim oldugunu sor (baglanti testi)
    // ------------------------------------------------------------
    public async Task<string> GetBotUsernameAsync(CancellationToken ct)
    {
        string body = await _http.GetStringAsync($"{_baseUrl}/getMe", ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var result))
            return "bilinmiyor";

        if (!result.TryGetProperty("username", out var username))
            return "bilinmiyor";

        return username.GetString() ?? "bilinmiyor";
    }

    // ------------------------------------------------------------
    //  GetLatestUpdateIdAsync  -  Program acildiginda eski mesajlari atla
    // ------------------------------------------------------------
    public async Task<int> GetLatestUpdateIdAsync(CancellationToken ct)
    {
        // offset=-1 : Telegram'a "sadece en son mesaji ver" demek.
        string url = $"{_baseUrl}/getUpdates?offset=-1&timeout=0";
        string body = await _http.GetStringAsync(url, ct);

        int latest = 0;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var result))
            return 0;

        foreach (var item in result.EnumerateArray())
        {
            if (item.TryGetProperty("update_id", out var id))
            {
                latest = Math.Max(latest, id.GetInt32());
            }
        }

        return latest;
    }

    // ------------------------------------------------------------
    //  GetUpdatesAsync  -  Yeni mesajlari al
    // ------------------------------------------------------------
    public async Task<List<TelegramUpdate>> GetUpdatesAsync(int offset, int timeoutSeconds, CancellationToken ct)
    {
        string url = $"{_baseUrl}/getUpdates?offset={offset}&timeout={timeoutSeconds}";
        string body = await _http.GetStringAsync(url, ct);

        var updates = new List<TelegramUpdate>();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var result))
            return updates;

        foreach (var item in result.EnumerateArray())
        {
            long updateId = 0;
            if (item.TryGetProperty("update_id", out var id))
                updateId = id.GetInt64();

            // "message" alani her zaman olmaz. Varsa oku, yoksa bos gec.
            TelegramMessage? message = null;

            if (item.TryGetProperty("message", out var msg))
            {
                string text = "";
                if (msg.TryGetProperty("text", out var t))
                    text = t.GetString() ?? "";

                long chatId = 0;
                if (msg.TryGetProperty("chat", out var chat)
                    && chat.TryGetProperty("id", out var cid))
                {
                    chatId = cid.GetInt64();
                }

                int messageId = 0;
                if (msg.TryGetProperty("message_id", out var mid))
                    messageId = mid.GetInt32();

                // Sadece yazili mesajlari ilgilendir. (Fotograf, ses kaydi
                // gibi mesajlarda "text" alani olmaz -> atlanir.)
                if (chatId != 0 && !string.IsNullOrWhiteSpace(text))
                {
                    message = new TelegramMessage(messageId, chatId, text);
                }
            }

            updates.Add(new TelegramUpdate(updateId, message));
        }

        return updates;
    }

    // ------------------------------------------------------------
    //  SendChatActionAsync  -  "Yaziyor..." efekti
    // ------------------------------------------------------------
    public async Task SendChatActionAsync(long chatId, string action, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["action"] = action
        };

        await PostJsonAsync("sendChatAction", payload, ct);
    }

    // ------------------------------------------------------------
    //  SendMessageAsync  -  Mesaj gonder (uzun metni parcalayarak)
    // ------------------------------------------------------------
    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        // Telegram tek mesajda en fazla 4096 karakter kabul eder.
        const int maxLength = 4096;

        foreach (var chunk in SplitText(text, maxLength))
        {
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = chatId,
                ["text"] = chunk
            };

            await PostJsonAsync("sendMessage", payload, ct);
        }
    }

    // ------------------------------------------------------------
    //  Yardimcilar  (private)
    // ------------------------------------------------------------

    // JSON gonderip cevabi okumadan sadece "basarili mi?" kontrol eder.
    private async Task PostJsonAsync(string method, Dictionary<string, object> payload, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(payload);

        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _http.PostAsync($"{_baseUrl}/{method}", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            string hataBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Telegram {method} hatasi: {(int)response.StatusCode} - {hataBody}");
        }
    }

    // Metni maxLength karakterlik parcalara boler.
    // Once bosluktan kesmeyi dener, olmazsa karakterden keser.
    private static IEnumerable<string> SplitText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return "";
            yield break;
        }

        while (text.Length > maxLength)
        {
            // Son boslugu bul (bulamazsa tam maxLength noktasinda kes)
            int cut = text.LastIndexOf(' ', maxLength);
            if (cut <= 0) cut = maxLength;

            yield return text[..cut];
            text = text[cut..].TrimStart();
        }

        yield return text;
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) async / await nedir?

        public async Task<List<TelegramUpdate>> GetUpdatesAsync(...)
        {
            string body = await _http.GetStringAsync(url, ct);
            ...
        }

     - "async" = "bu metot bekleme yapabilir, dondugu yerde
       program dursun, dondugu zaman devam et"
     - "await" = "sonucu bekle, gelince buradan devam et"
     - "Task<T>" = "sonunda T turunden bir sonuc donecek"
     - "Task" (T'siz) = "sonuc yok, sadece bir sey yapmis ol"

     Neden gerekli? Cift taraflı (Telegram + LM Studio) iki yere
     istek atiyoruz. await ile biri beklerken digeri calisabilir.

  2) CancellationToken nedir?

     Programi Ctrl+C ile kapatmak istiyoruz. Ama getUpdates
     30 sn boyunca Telegram'da bekliyor. O esnade iptal
     edebilmek icin "ct" (cancellation token) parametresi
     her cagriya verilir.

     Program.cs'te Ctrl+C tusuna basinca bu token iptal edilir
     ve bekleyen istek hemen geri doner.

  3) StringContent nedir?

        new StringContent(json, Encoding.UTF8, "application/json")

     Yaziyi HTTP govdesine (body) cevirir ve "bu JSON" bilgisini
     basliga (header) ekler. Encoding.UTF8 -> Turkce/emoji karakterler
     bozulmaz.

  4) JsonDocument nedir?

     JSON metnini okunabilir bir agaca cevirir.

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("result", out var result)

     "TryGetProperty" -> "bu alan var mi?" diye sorar, varsa degeri
     verir. GetProperty yerine bunu kullaniyoruz cunku alan
     bazen hiç olmayabilir (o zaman program coktu).

  5) Neden "using var" yaziyoruz?

     JsonDocument bir "bellek kaynagi" (IDisposable).
     "using var" = "kullan, sonra otomatik serbest birak".
 ---------------------------------------------------------
*/
