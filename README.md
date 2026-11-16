# Turatronik Makro Klavye v1

9 tuş ve 1 enkoder (döner düğme) olan, **RP2040 + CircuitPython** ile çalışan makro klavye ve onu yöneten
Windows uygulaması. Tuşlara kısayol, metin, uygulama, makro ya da medya komutları atanır. Ayarlar klavyenin
içinde saklanır; uygulama kapalıyken ve başka bilgisayarda da çalışır.

> Bu projenin devamı: [makro-klavye-v2](https://github.com/turatronik/makro-klavye-v2) — 6 tuş, 2 enkoder,
> 240×240 ekran, C++ kart yazılımı.

## Depo düzeni

| Klasör | İçerik |
|---|---|
| `MakroKeyboard/` | Windows uygulaması (C#, WinForms, .NET Framework) |
| `Kart/` | Kart yazılımı (CircuitPython 9.x) — `code.py`, `macro_keys.py`, `led_animations.py`, `settings_server.py`, `boot.py` |
| `MakroKeyboard.slnx` | Visual Studio çözüm dosyası |

Uygulama sürümü **3.6**, kart yazılımı sürümü **2.8**.

## Donanım

| Parça | Bağlantı |
|---|---|
| Tuşlar 1–8 | GP1 … GP8 |
| Tuş 9 (enkoder altı) | GP29 |
| Tuş 10 | GP28 |
| Enkoder A / B | GP27 / GP26 |
| NeoPixel (WS2812B) | GP15 |

Kart: RP2040 (CircuitPython 9.x). `lib` klasöründe `adafruit_hid` ve `neopixel.mpy` gerekir.
LED akımı 400 mA ile sınırlıdır; aşılırsa kart parlaklığı kendiliğinden kısar.

## Derleme

```
MakroKeyboard.slnx dosyasını Visual Studio ile aç → Build
```

Kart yazılımı derlenmez: `Kart/` içindeki `.py` dosyaları `CIRCUITPY` sürücüsünün köküne kopyalanır.
Yazılabilmesi için klavye **9 numaralı tuşa basılı tutularak** takılır (normal takıldığında sürücü görünmez,
bu kasıtlıdır).

---

# Kullanım kılavuzu

## 1. Kurulum

### Uygulama
1. `MakroKlavye-x.y.exe` dosyasını çalıştır.
2. İlk açılışta "bilgisayara kurulsun mu?" diye sorar. **Evet** dersen kendini
   `%LOCALAPPDATA%\Programs\MakroKlavye` klasörüne kurar, masaüstüne ve Başlat menüsüne kısayol koyar.
3. Kaldırmak için: Ayarlar > Uygulama > **KALDIR…** ya da Windows'ta *Uygulamalar ve özellikler*.

### Kart (ilk kez)
1. Klavyeyi **9 numaralı tuşa (enkoderin altındaki) basılı tutarak** USB'ye tak.
2. `Kart/` içindeki `.py` dosyalarını `CIRCUITPY` sürücüsünün köküne kopyala.
3. `CIRCUITPY\lib` klasöründe `adafruit_hid` klasörü ve `neopixel.mpy` olmalı.
4. Klavyeyi çıkarıp **normal** tak.

Bundan sonra kart yazılımı da uygulamadan güncellenir, dosya kopyalamak gerekmez.

## 2. Bağlanma
Uygulama açılınca klavyeyi kendiliğinden bulur (daha önce bağlandığı portu önce dener). Bulamazsa **BAĞLAN**'a bas.
Üst satırda klavyenin adı yazar; yanındaki **kalem** simgesiyle ad verebilirsin.

Bağlanmıyorsa: Mu, PuTTY, Arduino IDE gibi seri porta bağlanan bir program açıksa kapat; USB kablosunu ve girişini değiştir.

## 3. Tuş atama
Pencerenin üstünde üç sekme var: **Tuşlar**, **Işık**, **Ayarlar** (Ctrl+1 / 2 / 3).

1. Klavyenin resminde bir tuşa tıkla (ya da klavyenden **1-9**, enkoder için **0**).
2. **Bütün basışlar** listesinden basış türünü seç: **Kısa**, **Uzun**, **Çift** basış.
   Enkoderde ayrıca **Sağa** ve **Sola** çevirme var.
   **Tut + çevir**: bir tuşu basılı tutup enkoderi çevirince ayrı komut çalışır (yakınlaştırma, fırça boyutu,
   sekme değiştirme…). Kart yazılımı 2.8 gerekir.
3. Komut kutusuna tıkla ve ne yapacağını seç.
4. **AYARI KAYDET** (Ctrl+S).

Yanlış olursa **Geri al** (Ctrl+Z). İki tuşu sürükleyip bırakarak yerlerini değiştirebilirsin; sağ tıkla
kopyala / yapıştır / boşalt. Tuşa sağ tıklayıp **ad, renk ve simge** verilebilir.

### Komut menüsü
- Üstte **arama**: "ses", "kopyala", "sekme", "uyku"… yaz, Enter'la ilk sonucu seç.
- Beş tür: **Kısayol**, **Metin**, **Uygulama** (program, klasör, site aç), **Makro** (çok adımlı), **Boş**.
- Kategoriler: Genel, Düzenleme, Medya ve Ses, Tarayıcı, Pencere ve Sistem, Özel Tuşlar, Fare.
- **F13–F24**: gerçek klavyede olmayan fonksiyon tuşları. OBS, Discord, AutoHotkey kısayollarında hiçbir
  şeyle çakışmaz.

### Metin
Türkçe harfler için Ayarlar > Kart > **KLAVYE AYARLARI**'ndan bilgisayarındaki düzeni seç
(**Türkçe Q**, **Türkçe F** ya da İngilizce). é, ä, ñ, ß gibi harfler de yazılır. Metnin içine `{ENTER}`,
`{TAB}`, `{CTRL+A}` gibi tuşlar yazılabilir: `sifre{ENTER}`. Emoji yazılamaz.

## 4. Makro
Bir tuşa en fazla 20 adımlık dizi atanır:

| Adım | Ne yapar |
|---|---|
| Kısayol | Tuş kombinasyonu (Alt+Tab, Ctrl+Shift+T…) |
| Metin | Yazı yazdırır |
| Bekle | Sonraki adımdan önce bekler (en fazla 5000 ms) |
| Komut | Hazır komut (ses, medya, sol tık…) |
| Fare | Tekerleği çevirir ya da imleci taşır |
| Tekrar | Üstündeki adımları birkaç kez çalıştırır |
| Tut | Tuşu ya da fare tuşunu basılı tutar veya bırakır |

- **Kayıt al**: bastığın tuşlar, yazdığın metin, fare tıklamaları adım olarak eklenir.
- Adıma **çift tıkla** düzenle, **sürükle** sırala, **Ctrl+D** çoğalt, **Delete** sil.
- **Makrolarım**: makroyu ad vererek sakla, başka tuşta kullan.

## 5. Işık
**Işık** sekmesi: efekt, renk, parlaklık, hız ve akış renkleri. Solda klavyenin canlı demosu efekti oynatır.
Altta tahmini LED akımı görünür; **Sınır** (varsayılan 400 mA) aşılınca kart parlaklığı kendisi kısar.
Klavyedeki LED tuşu (varsayılan 9) efektler arasında geçer.

## 6. Profiller, geçmiş, yedek
- **Profiller**: farklı işler için ayrı tuş düzenleri. Hazır profiller: Ofis, Müzik ve medya, Tarayıcı,
  Toplantı (Teams), Oyun ve yayın, Video düzenleme, Sayı tuş takımı, OBS Studio, Visual Studio, Sunum.
- **Geçmiş**: klavyeye her yazışta o anki hâl saklanır (son 20), istenen ana dönülebilir.
- **Tam yedek**: profiller, makrolar, geçmiş ve klavyedeki ayarlar tek zip'e konur.

## 7. Klavye ayarları
Ayarlar > Kart > **KLAVYE AYARLARI…**

| Ayar | Açıklama |
|---|---|
| Metin yazma düzeni | Türkçe Q / Türkçe F / İngilizce |
| Harfler arası bekleme | Bazı programlar harf kaçırırsa artır |
| Uyku engelleyici | Fare arada bir 1 piksel kıpırdar |
| Uzun basış / çift basış | Süreler |
| Tuş titreşim filtresi | Bir basış iki kez algılanıyorsa artır |
| Enkoder | Yön, hassasiyet, tık başına kaç kez |

## 8. Güncellemeler
- **Uygulama**: yeni sürüm çıkınca sorar, kurunca ayarlar korunur.
- **Kart yazılımı**: bağlanınca sorar ya da Ayarlar > Kart > **GÜNCELLE**.
  - **Güvenli güncelleme**: yeni yazılım açılamazsa kart kendiliğinden eski sürüme döner.
  - **ÖNCEKİ SÜRÜME DÖN…** ile bir önceki sürüme dönülür.

## 9. Birden çok klavye
Her klavye kendini tanıtır; her klavyenin geçmişi ve seçili profili ayrıdır.
Ayarlar > Geçmiş > **DİĞER KLAVYEDEN AL…** bir klavyenin ayarlarını ötekine kopyalar.

## 10. Sorun giderme

| Sorun | Çözüm |
|---|---|
| Klavye bulunamıyor | Seri porta bağlanan programları kapat; başka kablo / USB girişi dene |
| "Salt okunur, kaydedilemedi" | Klavyeyi 9 numaralı tuşa basmadan yeniden tak |
| Metinde harfler yanlış | KLAVYE AYARLARI'nda düzeni bilgisayarındakiyle aynı yap |
| Bazı programlarda harf kaçıyor | Harfler arası beklemeyi artır |
| Tek basış iki kez çalışıyor | Tuş titreşim filtresini artır |
| Kartta ne olduğunu görmek | Ayarlar > Kart > **TANILAMA…** |

Hata ayrıntıları `%APPDATA%\MakroKeyboard\hatalar.txt` dosyasına yazılır.

## Lisans

Bu depodaki her şey (yazılım, 3D modeller ve görseller) [CC BY-NC-SA 4.0](LICENSE)
lisanslıdır: yapabilir, değiştirebilir ve paylaşabilirsin; Turatronik'i kaynak
göstermen ve değiştirdiğin sürümü aynı lisansla paylaşman gerekir. Satmak ve ticari
amaçla kullanmak yasaktır.

Türkçe Q klavye düzeni tablosu Neradoc/Circuitpython_Keyboard_Layouts projesinden alınmıştır (MIT).
