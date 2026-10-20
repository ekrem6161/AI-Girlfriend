/*
 ============================================================
  LM STUDIO - Yerel Model Istemcisi              (Sinif)
 ============================================================
  KONU : HttpClient, JSON yazma, List<Dictionary>
 ============================================================

  PROBLEM:
  Model LM Studio'da calisiyor. Onunla konusmak icin
  OpenAI uyumlu bir API var:
      POST http://127.0.0.1:1234/v1/chat/completions

  ISTEK GOVDESI:
      {
        "model": "qwen3.5abl",
        "messages": [
          { "role": "system", "content": "..." },   <- karakter
          { "role": "user",   "content": "merhaba" },
          { "role": "assistant","content": "selam" }
        ],
        "max_tokens": 500,
        "temperature": 0.85,
        "reasoning_effort": "none",
        "stream": false
      }

  CEVAP:
      {
        "choices": [
          { "message": {
              "content": "merhaba! nasilsin?",
              "reasoning_content": ""
          }}
        ]
      }

  ============================================================
  "reasoning" NEDIR?
  ============================================================
  Qwen3.5 bir "dusunen" model. Cevap vermeden once icinde
  bir plan yaziyor (reasoning_content). Bu plan uzun olabilir
  ve yavaslatir.

  Ayardaki "reasoningEffort" degerine gore:
      "none"   -> hic dusunmez. HIZLI (~1-2 sn)
      "medium" -> dusunur. YAVAS (~30-60 sn)
      ""       -> parametre hiç gonderilmez (model karar verir)

  Onemli: cevap metni her zaman "content" alanindadir.
  "reasoning_content" sadece modelin ic notudur, kullaniciya
  gonderilmez.

  ============================================================
  ISTEK JSON'UNU NASIL KURARIZ?
  ============================================================
  C#'da anonymous type (isimsiz tip) ile kisa yol da var:
      new { model = "x", messages = ... }
  Ama biz Dictionary<string, object> kullaniyoruz cunku bazi
  alanlari sart degil (reasoningEffort bos ise hic gondermiyoruz).
  ============================================================
*/

using System.Text.Json;

public class LmStudio
{
    private readonly HttpClient _http;
    private readonly Settings _settings;

    public LmStudio(HttpClient http, Settings settings)
    {
        _http = http;
        _settings = settings;
    }

    // ------------------------------------------------------------
    //  ChatAsync  -  Modele mesaj gonder, cevabini al
    // ------------------------------------------------------------
    public async Task<string> ChatAsync(
        string systemPrompt,
        List<ChatMessage> history,
        string userMessage,
        CancellationToken ct)
    {
        // --- 1) Gonderilecek mesajlari sirala ---
        var messages = new List<Dictionary<string, string>>();

        // Sistem promptu her zaman en basa
        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "system",
            ["content"] = systemPrompt
        });

        // Onceki konusma (sistem promptundan SONRA)
        foreach (var m in history)
        {
            messages.Add(new Dictionary<string, string>
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            });
        }

        // En son yeni mesaj
        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "user",
            ["content"] = userMessage
        });

        // --- 2) Govdeyi kur ---
        var payload = new Dictionary<string, object>
        {
            ["model"] = _settings.Model,
            ["messages"] = messages,
            ["max_tokens"] = _settings.MaxTokens,
            ["temperature"] = _settings.Temperature,
            ["stream"] = false
        };

        // Bos degilse ekle (bos ise parametreyi hic gonderme)
        if (!string.IsNullOrWhiteSpace(_settings.ReasoningEffort))
        {
            payload["reasoning_effort"] = _settings.ReasoningEffort;
        }

        // --- 3) Gonder ---
        string json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        string url = $"{_settings.LmStudioBaseUrl}/v1/chat/completions";

        HttpResponseMessage response = await _http.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            string hataBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"LM Studio hatasi: {(int)response.StatusCode} - {hataBody}");
        }

        string body = await response.Content.ReadAsStringAsync(ct);

        // --- 4) Cevabi oku ---
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return "";

        var firstChoice = choices[0];

        if (!firstChoice.TryGetProperty("message", out var message))
            return "";

        // Cevap metni "content" alaninda
        string text = "";
        if (message.TryGetProperty("content", out var contentProp))
        {
            text = contentProp.GetString() ?? "";
        }

        return text.Trim();
    }
}

/*
 ---------------------------------------------------------
  IPUCLARI
 ---------------------------------------------------------
  1) "content" bos cikabilir, ne olur?

     Model tum token'i "dusunme" kismina harcadiginda cevap
     metni bos kalir ("finish_reason": "length").
     Program.cs bunu kontrol edip yedek mesaj gonderir.

  2) "role" degerleri:
        "system"     -> karakter talimlari
        "user"       -> kullanicinin mesaji
        "assistant"  -> botun onceki cevabi

  3) Liste sozlugu (List<Dictionary<...>>) nasil JSON'a doner?

        var messages = new List<Dictionary<string, string>>();
        messages.Add(new Dictionary<string, string> { ["role"] = "user", ["content"] = "selam" });

     JsonSerializer bunu otomatik olarak:
        [ { "role": "user", "content": "selam" } ]
     haline cevirir. Ekstra bir sey yazmana gerek yok.

  4) Neden System.Text.Json kullaniyoruz?

     .NET'in JSON kutuphanesi. NuGet paketi gerekmez, .NET ile
     birlikte gelir. Newtonsoft.Json (eski kutuphane) icin
     ayrica paket indirmek gerekir.

  5) await ve try/catch birlikte:

     Hata olursa (LM Studio kapali mesela) hata "telegram"
     uzerinden kullaniciya bildirilir. Program cokmez.
 ---------------------------------------------------------
*/
