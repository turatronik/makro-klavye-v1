import os
import time

import board
import digitalio
import rotaryio
import neopixel
import supervisor

from macro_keys import MacroKeys, load_settings, split_macro
from led_animations import LedAnimator, hex_to_rgb
from settings_server import SettingsServer

# =========================================================
# PIN TANIMLARI
# =========================================================

BUTTON_PINS = [
    board.GP1,   # BUTTON0
    board.GP2,   # BUTTON1
    board.GP3,   # BUTTON2
    board.GP4,   # BUTTON3
    board.GP5,   # BUTTON4
    board.GP6,   # BUTTON5
    board.GP7,   # BUTTON6
    board.GP8,   # BUTTON7
    board.GP29,  # BUTTON8
    board.GP28,  # BUTTON9
]

ENCODER_A = board.GP27
ENCODER_B = board.GP26

NEOPIXEL_PIN = board.GP15
NEOPIXEL_COUNT = 13

# =========================================================
# VARSAYILAN AYARLAR (settings.txt ile ezilebilir)
# =========================================================

SURUM = "2.8"          # kart yazılımı sürümü (PC uygulaması güncelleme için okur)

SETTINGS_PATH = "/settings.txt"

LONG_PRESS = 0.6       # saniye: bu kadar basılı tutulursa "uzun basış" (settings.txt: LONG_PRESS_MS)
DOUBLE_WINDOW = 0.3    # saniye: ikinci basış bu süre içinde gelirse "çift basış" (DOUBLE_PRESS_MS)
MACRO_MAX_WAIT = 5000  # ms: tek bir WAIT adımı en fazla bu kadar bekler
MACRO_MAX_REPEAT = 100 # REPEAT adımı bir bloğu en fazla bu kadar kez çalıştırır
MACRO_MAX_STEPS = 2000 # bir makroda (tekrarlar dahil) çalışacak en fazla adım: kart kilitlenmesin
LED_SAVE_DELAY = 3.0   # saniye: LED tuşuyla seçilen mod, son basıştan bu kadar sonra kaydedilir
ENC_TERS = False       # settings.txt: ENCODER_TERS
ENC_ADIM = 1           # settings.txt: ENCODER_ADIM
ENC_TEKRAR = 1         # settings.txt: ENCODER_TEKRAR (bir adımda komut kaç kez çalışır)
TITRESIM = 0.0         # sn: tuş titreşim filtresi (settings.txt: TITRESIM_MS, 0 = kapalı)
enc_birikim = 0        # henüz komuta dönüşmemiş encoder adımları
HATA_DOSYASI = "/son_hata.txt"
DENEME_DOSYASI = "/yedek/deneme"   # güncellemeden sonra yeni yazılım henüz onaylanmadı
GERI_DONUS_DOSYASI = "/geri_donus.txt"
DENEME_SURESI = 20     # sn: yeni yazılım bu kadar sorunsuz çalışınca onaylanır
DENEME_HATA = 5        # deneme sırasında bu kadar hata olursa eski yazılıma dönülür
UYKU_HAREKET = 1       # piksel: uyku engelleyicinin fareyi kıpırdatma miktarı (gidip geri gelir)

DEFAULT_SETTINGS = {
    "BUTTON0": "WINDOWS+D",
    "BUTTON1": "MEDIA_PREVIOUS",
    "BUTTON2": "MEDIA_PLAY_PAUSE",
    "BUTTON3": "MEDIA_NEXT",
    "BUTTON4": "ALT+TAB",
    "BUTTON5": "CTRL+C",      # BUTON 6: kopyala
    "BUTTON6": "CTRL+V",      # BUTON 7: yapıştır
    "BUTTON7": "DISPLAY",
    "BUTTON8": "PIXEL_MODE",
    "BUTTON9": "MUTE",
    "ENCODER_CW": "VOLUME_UP",     # encoder sağa çevrilince
    "ENCODER_CCW": "VOLUME_DOWN",  # encoder sola çevrilince
    "LED_MODE": "rainbow",
    "LED_COLOR": "0066FF",    # renkli efektlerin rengi (color, breathe, comet ...)
    "LED_BRIGHTNESS": "100",  # 0-100
    "LED_SPEED": "100",       # animasyon hızı (%): 100 normal, 25-300
    "LED_FLOW": "FF0000,8C00FF,FFFFFF",   # 3 renk akışının renkleri (red_purple_white modu)
    "LONG_PRESS_MS": "600",   # uzun basış için basılı tutma süresi (200-2000 ms)
    "DOUBLE_PRESS_MS": "300", # çift basışta iki basış arası en fazla (100-1000 ms)
    "KLAVYE_DUZENI": "US",    # metinler hangi Windows düzenine göre yazılsın: US ya da TR (Türkçe Q)
    "TEXT_DELAY_MS": "0",     # metin yazarken harfler arası bekleme (0-100 ms); harf kaçıran programlar için
    "ENCODER_TERS": "0",      # 1: encoder yönü ters (sağa çevirince ENCODER_CCW çalışır)
    "ENCODER_ADIM": "1",      # kaç encoder adımında bir komut (1, 2 ya da 4)
    "ENCODER_TEKRAR": "1",    # encoder bir adımda komutu kaç kez çalıştırsın (1-5)
    "TITRESIM_MS": "0",       # tuş titreşim filtresi (0-30 ms): tek basış iki kez algılanıyorsa artır
    "UYKU_ENGEL": "0",        # 1: bilgisayar uyumasın, fare arada bir 1 piksel kıpırdar (NOSLEEP tuşu aç/kapa)
    "UYKU_ARALIK": "60",      # uyku engelleyici kaç saniyede bir kıpırdatsın (10-600)
    "LED_AKIM_SINIR": "400",  # LED'lerin çekebileceği en fazla akım (mA, 0 = sınırsız); aşılınca parlaklık kısılır
    # BUTTONn_CW / BUTTONn_CCW: n tuşu (9 = encoder'a basma) basılıyken encoder sağa / sola çevrilince
}

def ayarlari_yukle():
    return load_settings(SETTINGS_PATH, DEFAULT_SETTINGS)

settings = ayarlari_yukle()

# Kod dosyaya yazınca kart kendiliğinden yeniden başlamasın
supervisor.runtime.autoreload = False

# =========================================================
# DONANIM KURULUMU
# =========================================================

macros = MacroKeys()

pix = neopixel.NeoPixel(
    NEOPIXEL_PIN, NEOPIXEL_COUNT, brightness=1, auto_write=False
)
leds = LedAnimator(pix, mode="rainbow")

buttons = []
for pin in BUTTON_PINS:
    btn = digitalio.DigitalInOut(pin)
    btn.direction = digitalio.Direction.INPUT
    btn.pull = digitalio.Pull.UP
    buttons.append(btn)

encoder = rotaryio.IncrementalEncoder(ENCODER_A, ENCODER_B)
last_encoder_pos = encoder.position

was_pressed = [False] * len(buttons)
press_time = [0.0] * len(buttons)       # tuşa basılma anı
long_fired = [False] * len(buttons)     # uzun basış bu basışta çalıştı mı
pending_click = [None] * len(buttons)   # çift basış için bekleyen ilk tıklamanın bırakılma anı
ham_durum = [False] * len(buttons)      # titreşim filtresi: pinin son okunan hâli
ham_zaman = [0.0] * len(buttons)        # ... ve o hâle geçtiği an
kararli = [False] * len(buttons)        # TITRESIM süresince değişmeden kalan hâl


def tus_basili(i, now):
    """Tuş basılı mı? TITRESIM > 0 ise pin o kadar süre aynı kalmadan değişim sayılmaz."""
    ham = not buttons[i].value
    if TITRESIM <= 0:
        ham_durum[i] = kararli[i] = ham
        return ham
    if ham != ham_durum[i]:
        ham_durum[i] = ham
        ham_zaman[i] = now
    elif now - ham_zaman[i] >= TITRESIM:
        kararli[i] = ham
    return kararli[i]

# =========================================================
# KOMUT / LED İŞLEME
# =========================================================

def run_command(command):
    """Bir komutu çalıştırır (tuş, makro adımı ve encoder aynı yolu kullanır)."""
    if command in ("", "NONE"):
        return                            # "Boş" tuş: hiçbir şey yapmaz
    if command.startswith("MACRO:"):
        run_macro(command[6:])
    elif command == "PIXEL_MODE":
        led_modu_degistir(leds.next_mode())
    elif command == "NOSLEEP":
        uyku_degistir(not uyku_acik)
    elif not macros.run(command):
        print("Bilinmeyen komut:", command)

def run_macro(body):
    """
    Adımlar sırayla çalışır: kısayol, medya, TEXT:, PIXEL_MODE, WAIT:ms (bekleme),
    SCROLL_UP:n / SCROLL_DOWN:n (fare tekerleği), MOUSE_MOVE:x:y (imleci taşı), REPEAT:k:n,
    PRESS:x / RELEASE:x (tuşu ya da fare tuşunu basılı tut / bırak; makro bitince hepsi bırakılır)
    ve NOT:yazı (yalnızca açıklama, çalışmaz)
    (hemen üstündeki k adımı toplam n kez çalıştırır; iç içe kullanılabilir).
    """
    steps = split_macro(body)
    tutuldu = False   # PRESS adımı çalıştı mı (sonunda her şey bırakılır)
    kalan = {}        # REPEAT adımının sırası -> kalan tekrar sayısı
    i = 0
    calisan = 0

    while i < len(steps) and calisan < MACRO_MAX_STEPS:
        step = steps[i]

        if step.startswith("REPEAT:"):
            try:
                k, n = (int(x) for x in step[7:].split(":"))
            except ValueError:
                k, n = 0, 1
            k = max(0, min(k, i))
            n = max(1, min(n, MACRO_MAX_REPEAT))

            sayi = kalan.get(i, n - 1)
            if k > 0 and sayi > 0:
                kalan[i] = sayi - 1
                i -= k                    # bloğun başına dön
            else:
                kalan.pop(i, None)        # dış bir tekrar yeniden çalıştırırsa baştan saysın
                i += 1
            continue

        if step.startswith("NOT:"):
            i += 1                        # kullanıcının notu: hiçbir şey yapmaz, adım sayılmaz
            continue

        calisan += 1
        i += 1
        if step.startswith("MACRO:"):
            continue                      # iç içe makro yok
        if step.startswith("WAIT:"):
            try:
                ms = int(step[5:])
            except ValueError:
                ms = 0
            time.sleep(max(0, min(ms, MACRO_MAX_WAIT)) / 1000)
        elif step.startswith("PRESS:"):
            tutuldu = macros.basili_tut(step[6:], True) or tutuldu
        elif step.startswith("RELEASE:"):
            macros.basili_tut(step[8:], False)
        else:
            run_command(step)

    if tutuldu:
        macros.hepsini_birak()            # bırakılmadan kalan tuş olmasın

def fire(index, kind, command):
    """kind: SHORT, LONG ya da DOUBLE"""
    print("TUS", index, kind)             # PC canlı izleme için
    print("BUTTON", index, kind, "->", command)
    run_command(command)

def tanimli(komut):
    return bool(komut) and komut != "NONE"

def cevirme_var(i):
    """i tuşu (9 = encoder'a basma) basılıyken encoder çevirme komutu var mı?"""
    return tanimli(settings.get("BUTTON%d_CW" % i, "")) or tanimli(settings.get("BUTTON%d_CCW" % i, ""))

def basili_cevirme_tusu():
    """Encoder çevrilirken basılı tutulan ve çevirme komutu olan tuş (yoksa -1)."""
    for i in range(len(buttons)):
        if was_pressed[i] and cevirme_var(i):
            return i
    return -1

def process_button(i, now):
    """
    Tuş başına: kısa / uzun / çift basış.
    Uzun ya da çift basışı olmayan tuş basar basmaz çalışır (gecikme yok).
    """
    pressed = tus_basili(i, now)
    short = settings["BUTTON" + str(i)]
    long_cmd = settings.get("BUTTON" + str(i) + "_LONG", "")
    double_cmd = settings.get("BUTTON" + str(i) + "_DOUBLE", "")
    if long_cmd == "NONE":
        long_cmd = ""                     # boş uzun/çift basış hiç tanımlanmamış sayılır
    if double_cmd == "NONE":
        double_cmd = ""

    # Basılıyken encoder çevirme tanımlıysa tuşun kendi komutu bırakınca çalışır
    # (çevirdiysen hiç çalışmaz)
    if not long_cmd and not double_cmd and not cevirme_var(i):
        if pressed and not was_pressed[i]:
            fire(i, "SHORT", short)
    else:
        if pressed and not was_pressed[i]:
            press_time[i] = now
            long_fired[i] = False
            if pending_click[i] is not None:
                if now - pending_click[i] <= DOUBLE_WINDOW:
                    # ikinci basış süre içinde başladı: çift basış (bırakmayı beklemeden)
                    pending_click[i] = None
                    long_fired[i] = True          # bu basışın bırakılması / uzun tutulması sayılmaz
                    fire(i, "DOUBLE", double_cmd)
                else:
                    pending_click[i] = None       # bekleyen tıklama zaten kısa basıştı
                    fire(i, "SHORT", short)
        elif pressed:
            # basılı tutuluyor: süre dolunca uzun basış
            if long_cmd and not long_fired[i] and now - press_time[i] >= LONG_PRESS:
                long_fired[i] = True
                pending_click[i] = None
                fire(i, "LONG", long_cmd)
        elif was_pressed[i]:
            # bırakıldı
            if long_fired[i]:
                pass                              # uzun ya da çift basış zaten çalıştı
            elif double_cmd:
                pending_click[i] = now            # ikinci basışı bekle
            else:
                fire(i, "SHORT", short)

        # Çift basış penceresi doldu: bekleyen tek tıklama kısa basış sayılır
        if pending_click[i] is not None and not pressed and now - pending_click[i] > DOUBLE_WINDOW:
            pending_click[i] = None
            fire(i, "SHORT", short)

    was_pressed[i] = pressed

# =========================================================
# TUŞLA DEĞİŞEN AYARLARI KALICI YAP (LED modu, uyku engelleyici)
# =========================================================
# Tuşla seçilen mod settings.txt'ye yazılır; kart yeniden başlayınca aynı modla açılır.
# Modlar arasında hızlı geçerken her basışta yazıp flash belleği yormamak için
# son basıştan LED_SAVE_DELAY saniye sonra tek seferde kaydedilir.

led_kayit_zamani = None


def led_modu_degistir(mod):
    global led_kayit_zamani
    settings["LED_MODE"] = mod
    led_kayit_zamani = time.monotonic()
    print("NeoPixel modu:", mod)
    print("LEDMOD", mod)                  # PC uygulaması da güncel modu bilsin

# ---------------------------------------------------------
# UYKU ENGELLEYİCİ: açıkken fare UYKU_ARALIK saniyede bir 1 piksel gidip gelir,
# böylece bilgisayar uykuya geçmez / ekran kilitlenmez. Uygulama kapalıyken de çalışır.
# ---------------------------------------------------------

uyku_acik = False
uyku_aralik = 60.0
uyku_son = 0.0


def uyku_degistir(acik):
    global uyku_acik, uyku_son, led_kayit_zamani
    uyku_acik = acik
    uyku_son = time.monotonic()
    settings["UYKU_ENGEL"] = "1" if acik else "0"
    led_kayit_zamani = time.monotonic()   # LED moduyla aynı gecikmeli kayıt
    print("Uyku engelleyici:", "açık" if acik else "kapalı")
    print("UYKU", 1 if acik else 0)       # PC uygulaması da bilsin


def uyku_kontrol(now):
    global uyku_son
    if not uyku_acik or now - uyku_son < uyku_aralik:
        return
    uyku_son = now
    fare = macros.mouse
    if fare is not None:
        fare.move(x=UYKU_HAREKET)
        fare.move(x=-UYKU_HAREKET)


def ayarlari_dosyaya_yaz():
    try:
        with open(SETTINGS_PATH, "w") as dosya:
            for anahtar in sorted(settings):
                dosya.write(anahtar + "=" + settings[anahtar] + "\n")
        return True
    except OSError as e:
        # boot.py sürücüyü PC'ye yazılabilir bıraktıysa kart dosyaya yazamaz
        print("Ayarlar kaydedilemedi:", e)
        return False


def bekleyen_led_kaydi(now):
    global led_kayit_zamani
    if led_kayit_zamani is not None and now - led_kayit_zamani >= LED_SAVE_DELAY:
        led_kayit_zamani = None
        if ayarlari_dosyaya_yaz():
            print("Ayarlar kaydedildi (LED:", settings["LED_MODE"], " uyku:", settings.get("UYKU_ENGEL", "0") + ")")


def ms_ayari(anahtar, varsayilan, en_az, en_cok):
    """settings içindeki milisaniye değerini sınırlar içinde saniyeye çevirir."""
    try:
        ms = int(settings.get(anahtar, varsayilan))
    except ValueError:
        ms = varsayilan
    return max(en_az, min(en_cok, ms)) / 1000

def sureleri_uygula():
    global LONG_PRESS, DOUBLE_WINDOW
    LONG_PRESS = ms_ayari("LONG_PRESS_MS", 600, 200, 2000)
    DOUBLE_WINDOW = ms_ayari("DOUBLE_PRESS_MS", 300, 100, 1000)
    duzen = settings.get("KLAVYE_DUZENI", "US").upper()
    macros.duzen = duzen if duzen in ("TR", "TRF") else "US"
    macros.harf_bekleme = ms_ayari("TEXT_DELAY_MS", 0, 0, 100)

    global ENC_TERS, ENC_ADIM, ENC_TEKRAR, TITRESIM, enc_birikim
    ENC_TERS = settings.get("ENCODER_TERS", "0") == "1"
    try:
        ENC_ADIM = max(1, min(8, int(settings.get("ENCODER_ADIM", "1"))))
    except ValueError:
        ENC_ADIM = 1
    try:
        ENC_TEKRAR = max(1, min(5, int(settings.get("ENCODER_TEKRAR", "1"))))
    except ValueError:
        ENC_TEKRAR = 1
    TITRESIM = ms_ayari("TITRESIM_MS", 0, 0, 30)
    enc_birikim = 0

    global uyku_acik, uyku_aralik, uyku_son
    uyku_acik = settings.get("UYKU_ENGEL", "0") == "1"
    uyku_aralik = ms_ayari("UYKU_ARALIK", 60, 10, 600) * 1000   # saniye olarak saklanır
    uyku_son = time.monotonic()

def kayit_sonrasi():
    """PC yeni ayarları kaydedince: basış süreleri ve LED yeniden uygulanır."""
    sureleri_uygula()
    led_ayarlarini_uygula()

def renk_listesi(metin):
    """'FF0000,8C00FF,FFFFFF' -> ((255,0,0), ...); hatalıysa None (varsayılan renkler kalır)"""
    try:
        renkler = tuple(hex_to_rgb(p) for p in metin.split(","))
        return renkler if len(renkler) >= 2 else None
    except Exception:
        return None

def led_uygula(mod, renk_hex, parlaklik, hiz=None, akis=None):
    """
    mod: 'rainbow'..., renk_hex: '0066FF', parlaklik: 0-100,
    hiz: animasyon hızı (%), akis: 3 renk akışının renkleri ('FF0000,8C00FF,FFFFFF').
    hiz / akis verilmezse settings.txt'dekiler kullanılır.
    """
    if hiz is None:
        hiz = settings.get("LED_SPEED", "100")
    if akis is None:
        akis = settings.get("LED_FLOW", "")
    try:
        leds.akim_siniri = max(0, int(settings.get("LED_AKIM_SINIR", "400")))
    except ValueError:
        leds.akim_siniri = 400
    leds.ayarla(mod, hex_to_rgb(renk_hex), int(parlaklik) / 100, int(hiz), renk_listesi(akis))

def led_ayarlarini_uygula():
    """settings içindeki LED_* değerlerini LED'e uygular."""
    try:
        led_uygula(
            settings["LED_MODE"],
            settings["LED_COLOR"],
            settings["LED_BRIGHTNESS"],
            settings.get("LED_SPEED", "100"),
            settings.get("LED_FLOW", ""),
        )
    except Exception as e:
        print("LED ayari uygulanamadi:", e)

# =========================================================
# HATA KORUMASI
# =========================================================
# Ana döngüde bir hata olursa kart durmaz: hata kaydedilir, döngü devam eder.
# Son hata settings_server'ın TANI cevabında görünür (PC uygulaması Tanılama'da gösterir).

hata_sayisi = 0
son_hata = ""
son_hata_zamani = 0.0
hata_dosyasi_yazildi = -999.0

try:
    with open(HATA_DOSYASI, "r") as _f:
        onceki_hata = _f.read().strip()[:200]   # önceki çalışmadan kalan
except OSError:
    onceki_hata = ""


def hata_kaydet(e):
    global hata_sayisi, son_hata, son_hata_zamani, hata_dosyasi_yazildi
    hata_sayisi += 1
    son_hata = (type(e).__name__ + ": " + str(e))[:200]
    son_hata_zamani = time.monotonic()
    print("HATA", son_hata)
    # Flash belleği yormamak için dosyaya en fazla dakikada bir yazılır
    if son_hata_zamani - hata_dosyasi_yazildi > 60:
        hata_dosyasi_yazildi = son_hata_zamani
        try:
            with open(HATA_DOSYASI, "w") as f:
                f.write(son_hata)
        except OSError:
            pass


def _dosya_oku(yol):
    try:
        with open(yol, "r") as d:
            return d.read().strip()
    except OSError:
        return None


# Güvenli güncelleme: yeni yazılım deneniyor mu, önceki güncellemede geri dönüldü mü
deneme_baslangic = time.monotonic() if _dosya_oku(DENEME_DOSYASI) is not None else None
deneme_yukleyiciyle = __name__ != "__main__"   # settings_server.py yükleyici olarak çalıştırdıysa
geri_donus = _dosya_oku(GERI_DONUS_DOSYASI) or ""
if geri_donus:
    try:
        os.remove(GERI_DONUS_DOSYASI)          # bir kez bildirilir (bu çalışma boyunca TANI'da görünür)
    except OSError:
        pass


def deneme_kontrol(now):
    """Yeni yazılım DENEME_SURESI boyunca sorunsuz çalıştı: onayla (geri dönüş gerekmez)."""
    global deneme_baslangic
    if deneme_baslangic is not None and now - deneme_baslangic >= DENEME_SURESI:
        deneme_baslangic = None
        try:
            os.remove(DENEME_DOSYASI)
            print("Yeni kart yazılımı onaylandı:", SURUM)
        except OSError:
            pass


def tani_ek():
    bilgi = {"HATA_SAYISI": hata_sayisi}
    if deneme_baslangic is not None:
        bilgi["DENEME"] = 1
    if geri_donus:
        bilgi["GERI_DONUS"] = geri_donus     # "eski sürüm|neden"
    bilgi["UYKU"] = 1 if uyku_acik else 0
    if son_hata:
        bilgi["SON_HATA"] = son_hata
        bilgi["SON_HATA_ONCE"] = int(time.monotonic() - son_hata_zamani)
    if onceki_hata:
        bilgi["ONCEKI_HATA"] = onceki_hata
    return bilgi

# PC uygulaması USB seri üzerinden ayarları okuyup yazabilsin ve LED'i değiştirebilsin
server = SettingsServer(
    SETTINGS_PATH,
    settings,
    ayarlari_yukle,
    on_led=led_uygula,
    on_saved=kayit_sonrasi,
    surum=SURUM,
    tani_ek=tani_ek,
)

sureleri_uygula()
led_ayarlarini_uygula()

# =========================================================
# BAŞLANGIÇ
# =========================================================

print("================================")
print("Makro klavye çalışıyor... sürüm", SURUM)
for i in range(len(BUTTON_PINS)):
    print("BUTTON" + str(i), "->", settings["BUTTON" + str(i)])
print("ENCODER:", settings["ENCODER_CCW"], "/", settings["ENCODER_CW"])
print("Uzun basış:", LONG_PRESS, "sn  Çift basış:", DOUBLE_WINDOW, "sn  Düzen:", macros.duzen)
print("================================")

# =========================================================
# ANA DÖNGÜ
# =========================================================

def dongu():
    global last_encoder_pos, enc_birikim

    # Encoder: sağa / sola çevirme komutları ayarlanabilir (varsayılan ses).
    # ENC_ADIM adımda bir komut; ENC_TERS ise yön ters.
    pos = encoder.position
    if pos != last_encoder_pos:
        enc_birikim += pos - last_encoder_pos
        last_encoder_pos = pos
        while enc_birikim >= ENC_ADIM or enc_birikim <= -ENC_ADIM:
            direction = 1 if enc_birikim > 0 else -1
            enc_birikim -= direction * ENC_ADIM
            if ENC_TERS:
                direction = -direction
            tus = basili_cevirme_tusu()
            if tus >= 0:
                # Tuş basılıyken çevirme: tuşun kendi komutu (kısa / uzun / çift) bu basışta çalışmaz
                long_fired[tus] = True
                pending_click[tus] = None
                print("TUSENC", tus, direction)
                komut = settings.get("BUTTON%d_%s" % (tus, "CW" if direction > 0 else "CCW"), "")
                if not tanimli(komut):
                    continue
            else:
                print("ENC", direction)
                komut = settings["ENCODER_CW"] if direction > 0 else settings["ENCODER_CCW"]
            for _ in range(ENC_TEKRAR):
                run_command(komut)

    # Butonlar (animasyon takılmaz)
    now = time.monotonic()
    for i in range(len(buttons)):
        process_button(i, now)

    # Arka plan işleri
    server.update()   # PC'den gelen komutlar
    bekleyen_led_kaydi(now)
    uyku_kontrol(now)
    deneme_kontrol(now)
    macros.update()   # Windows+P menüsü zaman aşımı
    leds.update()     # LED animasyonu


while True:
    try:
        dongu()
    except Exception as e:
        hata_kaydet(e)
        try:
            macros.keyboard.release_all()   # yarıda kalan basılı tuş kalmasın
        except Exception:
            pass
        # Güncellemeden sonraki denemede art arda hata: yükleyici eski yazılıma döner
        if deneme_baslangic is not None and deneme_yukleyiciyle and hata_sayisi >= DENEME_HATA:
            raise
        time.sleep(0.2)                     # aynı hata art arda dönmesin
