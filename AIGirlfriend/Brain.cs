/*
 ============================================================
  BRAIN - Konusma Hafizasi                      (Sinif)
 ============================================================
  KONU : Dictionary, List, record, pencere (sliding window)
 ============================================================

  PROBLEM:
  Model her seferinde "sadece bu mesaja cevap ver" dersek
  sohbet cok sagirdir. "Dun nasildin?" diye sorulunca
  dunku konusmayi bilmez.

  Cozum: Sohbetin mesajlarini bir yerde saklamak (hafiza).
  Modele gonderirken: sistem promptu + son N mesaj + yeni mesaj.

  "N" kadar geriye bakacagimiz "maxHistoryMessages" ayarindan gelir.
  Bu pencereye "sliding window" (kayan pencere) denir:
  her yeni mesajda en eski mesaj duser, en yenisi eklenir.

  ============================================================
  YAPILAR
  ============================================================
    ChatMessage          -> tek bir mesaj (role + content)
    Brain                -> chat id -> mesaj listesi haritasi

  Yeni ogrendigin kavram: Dictionary<K,V>
  Anahtar (key) deger (value) tutar. Burada:
      anahtar = sohbetin oda numarasi (long)
      deger  = o odadaki mesajlarin listesi

  Liste kullanimi:
      List<ChatMessage> sohbet = new();

  ---------------------------------------------------------
  IPUCLARI
  ---------------------------------------------------------
  1) record nedir?

        public record ChatMessage(string Role, string Content);

     - "veri kutusu" gibi: iki deger tasir
     - Otomatik olarak constructor, Equals, ToString uretilir
     - Deger immutable (degistirilemez), yeni nesne olusturulur
     - "Role" ve "Content" degerlerine nokta ile erisilir:
            mesaj.Role, mesaj.Content

  2) Dictionary'de deger yoksa hata verir. Bu yuzden
     TryGetValue kullanilir:

        if (!_histories.TryGetValue(chatId, out var list))
        {
            list = new List<ChatMessage>();
            _histories[chatId] = list;
        }

     "TryGetValue" -> degeri bulursa true doner ve list degiskenine
     yazar. Bulamazsa false doner, list degiskeni null kalir.
     (Boylece "null referans" hatasi olmaz.)

  3) Son N mesaji almak icin LINQ:
        list.TakeLast(N)
     TakeLast -> sondan N tanesini alir.
  ---------------------------------------------------------
*/

public record ChatMessage(string Role, string Content, DateTime Time = default, bool IsProactive = false);

public class Brain
{
    // Sohbet id -> mesaj listesi
    private readonly Dictionary<long, List<ChatMessage>> _histories = new();

    // Sohbet id -> TOPLAM mesaj sayaci (kayan pencere kirpmasindan ETKILENMEZ)
    // Asama ilerletme bunu kullanir.
    private readonly Dictionary<long, int> _toplamSayac = new();

    // Kac mesaj geriye bakacagimiz
    private readonly int _maxMessages;

    // Cevapsiz kalan proaktif mesajlar kac dakika sonra silinsin
    private readonly int _proactiveExpiryMinutes;

    public Brain(int maxMessages, int proactiveExpiryMinutes = 5)
    {
        _maxMessages = maxMessages;
        _proactiveExpiryMinutes = proactiveExpiryMinutes;
    }

    // ------------------------------------------------------------
    //  Add  -  Sohbete yeni mesaj ekle (ve eskiyi dusur)
    // ------------------------------------------------------------
    public void Add(long chatId, string role, string content)
    {
        Add(chatId, role, content, false);
    }

    // ------------------------------------------------------------
    //  AddProactive  -  Nova'nin kendi baslattigi mesaj
    // ------------------------------------------------------------
    //  Isaretlenir, 5 dakika icinde cevap gelmezse silinir.
    public void AddProactive(long chatId, string content)
    {
        Add(chatId, "assistant", content, true);
    }

    private void Add(long chatId, string role, string content, bool isProactive)
    {
        // Bu sohbetin listesi var mi? Yoksa olustur.
        if (!_histories.TryGetValue(chatId, out var list))
        {
            list = new List<ChatMessage>();
            _histories[chatId] = list;
        }

        list.Add(new ChatMessage(role, content, DateTime.Now, isProactive));

        // Toplam sayaci artir (bu ASLA kirpilmaz)
        _toplamSayac[chatId] = _toplamSayac.TryGetValue(chatId, out var t) ? t + 1 : 1;

        // Pencereyi koru: en eskiyi at, yerine yenisini koy.
        while (list.Count > _maxMessages)
        {
            list.RemoveAt(0);
        }
    }

    // ------------------------------------------------------------
    //  GetHistory  -  Modele gonderilecek son N mesaji dondur
    // ------------------------------------------------------------
    public List<ChatMessage> GetHistory(long chatId)
    {
        // Sohbet hic baslamadiysa bos liste dondur.
        if (!_histories.TryGetValue(chatId, out var list))
        {
            return new List<ChatMessage>();
        }

        // Cevapsiz kalan proaktif mesajlari temizle
        PruneExpiredProactive(list);

        // Listenin kopyasi (asil listeyi bozmayalim)
        return list.TakeLast(_maxMessages).ToList();
    }

    // ------------------------------------------------------------
    //  PruneExpiredProactive  -  Eski proaktif mesajlari sil
    // ------------------------------------------------------------
    //  Nova mesaj atti, 5 dakika icinde cevap gelmedi.
    //  Mesaj history'de kalirsa model kafasi karisir
    //  ("kendine cevap veriyor" sanir), o yuzden siliyoruz.
    private void PruneExpiredProactive(List<ChatMessage> list)
    {
        if (_proactiveExpiryMinutes <= 0) return;

        DateTime sinir = DateTime.Now.AddMinutes(-_proactiveExpiryMinutes);

        list.RemoveAll(m => m.IsProactive && m.Time < sinir);
    }

    // ------------------------------------------------------------
    //  GetMessageCount  -  Sohbette biriken mesaj sayisi (sadece pencere)
    // ------------------------------------------------------------
    public int GetMessageCount(long chatId)
    {
        if (!_histories.TryGetValue(chatId, out var list))
        {
            return 0;
        }

        return list.Count;
    }

    // ------------------------------------------------------------
    //  GetTotalMessageCount  -  Soba konusulan TOPLAM mesaj sayisi
    // ------------------------------------------------------------
    //  DIKKAT: GetMessageCount kayan pencerenin sayisidir ve 20'de
    //  takilir. Asama ilerletme (Stage) TOPLAM sayaci kullanmali,
    //  yoksa "her 10 mesajda bir" kurali bozulur.
    public int GetTotalMessageCount(long chatId)
    {
        return _toplamSayac.TryGetValue(chatId, out var n) ? n : 0;
    }

    // ------------------------------------------------------------
    //  Clear  -  Sohbeti sifirla  (ilerde /reset komutu icin)
    // ------------------------------------------------------------
    public void Clear(long chatId)
    {
        _histories.Remove(chatId);
        _toplamSayac.Remove(chatId);
    }
}
