"""
settings_server.py - PC'den USB seri (konsol) üzerinden ayar okuma/yazma ve LED kontrolü

Komutlar (satır satır, "\n" ile biter):
    PING                  -> MK1  (kart kimliği)
    OKU                   -> KEY=değer satırları (tüm ayarlar), en sonda SON
    KAYDET                -> ardından gelen TEK satırı settings.txt'ye yazar,
    <kodlu satır>            "OK" (hata olursa "HATA: ...") döner.
                             Kodlama: "\\n" = yeni satır, "\\\\" = ters bölü
    LED <mod> <RRGGBB> <parlaklık 0-100> [hız %] [akış renkleri RRGGBB,RRGGBB,RRGGBB]
                          -> LED'i anında ayarlar (kaydetmeden önizleme) -> OK / HATA
    SURUM                 -> SURUM <sürüm>   (code.py'deki SURUM)
    KIMLIK                -> KIMLIK <işlemcinin seri numarası, hex>  (2.4+: birden çok klavyeyi ayırmak için)
    YEDEK                 -> YEDEK <sürüm>  ya da  YEDEK YOK   (son güncellemeden önceki yazılım, 2.4+)
    GERI_AL               -> OK, ardından yedekteki (önceki) yazılım geri konur ve kart yeniden başlar
                             (yedek yoksa HATA)
    TANI                  -> kart sağlığı: ANAHTAR=değer satırları, en sonda SON
                             (YAZILIM, CP, KART, BOS, TOPLAM, AYAR_BOYUT, AYAR_SAYI, BELLEK,
                              ACIK, CALISMA, SIFIRLAMA, BASLAMA, SICAKLIK, YAZILABILIR, KAYIT_SAYISI, KIMLIK)

  Kart yazılımı güncelleme (dosyalar base64 parçalar halinde gelir):
    DOSYA <ad.py>         -> <ad.py>.tmp açılır                     -> OK / HATA
    PARCA <base64>        -> parça .tmp dosyasına eklenir            -> OK / HATA
    DOSYA_BITIR <bayt>    -> boyut doğruysa .tmp, <ad.py> yerine geçer -> OK / HATA
    DOSYA_IPTAL           -> .tmp silinir                           -> OK
    YENIDEN               -> OK, ardından kart yazılımı yeniden başlar

  Güvenli güncelleme (2.3+):
    Bir dosyanın yerine yenisi geçmeden önce eskisi /yedek/ klasörüne kopyalanır.
    Dosya gönderildikten sonra YENIDEN gelirse kart yeni yazılımı "deneme" olarak açar:
    bu dosya (settings_server.py) ana program olarak çalışır ve code.py'yi içeri aktarır.
    Yeni yazılım açılamazsa ya da ilk saniyelerde art arda hata verirse /yedek'teki dosyalar
    geri konur, /geri_donus.txt'ye nedeni yazılır ve kart eski yazılımla yeniden başlar.
    code.py yeni yazılım bir süre sorunsuz çalışınca /yedek/deneme işaretini siler (onay).

Kart kendiliğinden şu satırları da yazar (PC canlı izleme için):
    TUS <n>               -> n numaralı tuşa basıldı
    ENC <+1|-1>           -> encoder döndü

Kullanım (code.py):
    server = SettingsServer("/settings.txt", settings, loader,
                            on_led=..., on_saved=...)
    while True:
        server.update()      # bloklamaz
"""

import os
import sys
import gc
import time
import binascii
import supervisor

try:
    import microcontroller
except ImportError:
    microcontroller = None
try:
    import storage
except ImportError:
    storage = None

MAX_SATIR = 12288
KIMLIK = "MK1"   # PING cevabı; PC uygulaması kartı bununla tanır

# PC'den güncellenebilecek dosyalar: kökteki .py dosyaları (settings.txt ve klasörler dışarıda)
GUNCELLENEBILIR = ("code.py", "macro_keys.py", "led_animations.py", "settings_server.py", "boot.py")

YEDEK = "/yedek"                       # güncellemeden önceki dosyaların kopyası
DENEME = YEDEK + "/deneme"             # varsa yeni yazılım henüz onaylanmadı (içinde eski sürüm)
YENI_DOSYALAR = YEDEK + "/yeni.txt"    # güncellemeden önce kartta olmayan dosyalar (geri dönüşte silinir)
GERI_DONUS = "/geri_donus.txt"         # geri dönüldüyse nedeni (code.py okuyup TANI'da gösterir)


def kart_kimligi():
    """İşlemcinin değişmeyen seri numarası (her kartta farklı). Okunamazsa '?'."""
    try:
        return binascii.hexlify(microcontroller.cpu.uid).decode().upper()
    except Exception:
        return "?"


def guncellenebilir(ad):
    return (ad in GUNCELLENEBILIR or ad.endswith(".py")) and "/" not in ad and "\\" not in ad \
        and not ad.startswith(".") and len(ad) <= 40


def _var(yol):
    try:
        os.stat(yol)
        return True
    except OSError:
        return False


def _kopyala(kaynak, hedef):
    with open(kaynak, "rb") as k:
        with open(hedef, "wb") as h:
            while True:
                parca = k.read(512)
                if not parca:
                    break
                h.write(parca)


def _sil(yol):
    try:
        os.remove(yol)
    except OSError:
        pass

def _coz(metin):
    """PC'nin kodladığı satırı çözer: \\n -> yeni satır, \\\\ -> \\, \\u015f -> ş (Türkçe harfler)"""
    sonuc = ""
    i = 0
    while i < len(metin):
        c = metin[i]
        if c == "\\" and i + 1 < len(metin):
            sonraki = metin[i + 1]
            if sonraki == "u" and i + 6 <= len(metin):
                try:
                    sonuc += chr(int(metin[i + 2:i + 6], 16))
                    i += 6
                    continue
                except ValueError:
                    pass
            sonuc += "\n" if sonraki == "n" else sonraki
            i += 2
        else:
            sonuc += c
            i += 1
    return sonuc

class SettingsServer:
    def __init__(self, path, settings, loader, on_led=None, on_saved=None, surum="?", tani_ek=None):
        """
        path     : yazılacak dosya ("/settings.txt")
        settings : çalışan koddaki ayar sözlüğü (yerinde güncellenir)
        loader   : dosyadan ayarları yeniden okuyup sözlük döndüren fonksiyon
        on_led   : on_led(mod, renk_hex, parlaklik)  - LED önizlemesi
        on_saved : on_saved()                  - kayıttan sonra (ör. LED'i yeniden uygula)
        surum    : kart yazılımının sürümü (PC güncelleme için karşılaştırır)
        """
        self.path = path
        self.settings = settings
        self.loader = loader
        self.on_led = on_led
        self.on_saved = on_saved
        self._tampon = ""
        self._kayit_bekleniyor = False   # KAYDET geldi: sıradaki satır dosya içeriği
        self.surum = surum
        self._dosya = None               # güncelleme sırasında açık .tmp dosyası
        self._dosya_adi = None
        self._baslangic = time.monotonic()   # kart yazılımının bu çalışmaya başladığı an
        self._kayit_sayisi = 0               # bu çalışmada PC'den gelen başarılı kayıtlar
        self.tani_ek = tani_ek               # code.py'den ek tanılama bilgisi: {ANAHTAR: değer}
        self._yedeklenen = None              # bu güncellemede yedeği alınan dosyalar (None: güncelleme yok)

    def update(self):
        """Ana döngüde sürekli çağır."""
        while supervisor.runtime.serial_bytes_available:
            ch = sys.stdin.read(1)

            if ch == "\r":
                continue

            if ch == "\n":
                satir, self._tampon = self._tampon, ""
                self._isle(satir)
            elif len(self._tampon) < MAX_SATIR:
                self._tampon += ch

    # -----------------------------------------------------

    def _isle(self, satir):
        if self._kayit_bekleniyor:
            self._kayit_bekleniyor = False
            self._kaydet(_coz(satir))
            return

        komut = satir.strip()

        if komut == "PING":
            print(KIMLIK)
        elif komut == "OKU":
            self._gonder()
        elif komut == "KAYDET":
            self._kayit_bekleniyor = True
        elif komut.startswith("LED "):
            self._led(komut)
        elif komut == "SURUM":
            print("SURUM " + self.surum)
        elif komut == "KIMLIK":
            print("KIMLIK " + kart_kimligi())
        elif komut == "YEDEK":
            print("YEDEK " + (yedek_surumu() or "YOK"))
        elif komut == "GERI_AL":
            if yedek_surumu() is None:
                print("HATA: yedek yok")
            else:
                print("OK")
                time.sleep(0.3)
                yedege_don()
                supervisor.reload()
        elif komut == "TANI":
            self._tani()
        elif komut.startswith("DOSYA "):
            self._dosya_baslat(komut[6:].strip())
        elif komut.startswith("PARCA "):
            self._dosya_parca(komut[6:].strip())
        elif komut.startswith("DOSYA_BITIR "):
            self._dosya_bitir(komut[12:].strip())
        elif komut == "DOSYA_IPTAL":
            self._dosya_iptal()
            print("OK")
        elif komut == "YENIDEN":
            self._denemeye_hazirla()
            print("OK")
            time.sleep(0.3)              # cevap PC'ye ulaşsın
            supervisor.reload()

    def _gonder(self):
        for anahtar in self.settings:
            print(anahtar + "=" + self.settings[anahtar])
        print("SON")

    def _kaydet(self, metin):
        try:
            with open(self.path, "w") as dosya:
                dosya.write(metin)

            # Çalışan koddaki ayarları anında güncelle (yeniden başlatma yok)
            yeni = self.loader()
            self.settings.clear()
            self.settings.update(yeni)

            if self.on_saved:
                self.on_saved()

            self._kayit_sayisi += 1
            print("OK")
        except Exception as e:
            print("HATA:", e)

    def _tani(self):
        """Kart sağlığı. Okunamayan bilgi atlanır (her kartta her özellik yok)."""
        def yaz(anahtar, oku):
            try:
                print(anahtar + "=" + str(oku()))
            except Exception:
                pass

        def alan(bos):
            s = os.statvfs("/")
            blok = s[1] or s[0]
            return blok * (s[4] if bos else s[2])

        yaz("YAZILIM", lambda: self.surum)
        yaz("CP", lambda: os.uname().release)
        yaz("KART", lambda: os.uname().machine)
        yaz("BOS", lambda: alan(True))
        yaz("TOPLAM", lambda: alan(False))
        yaz("AYAR_BOYUT", lambda: os.stat(self.path)[6])
        yaz("AYAR_SAYI", lambda: len(self.settings))
        yaz("BELLEK", lambda: gc.mem_free())
        yaz("ACIK", lambda: int(time.monotonic()))
        yaz("CALISMA", lambda: int(time.monotonic() - self._baslangic))
        yaz("SIFIRLAMA", lambda: str(microcontroller.cpu.reset_reason).split(".")[-1])
        yaz("BASLAMA", lambda: str(supervisor.runtime.run_reason).split(".")[-1])
        yaz("SICAKLIK", lambda: "%.1f" % microcontroller.cpu.temperature)
        yaz("YAZILABILIR", lambda: 0 if storage.getmount("/").readonly else 1)
        yaz("KAYIT_SAYISI", lambda: self._kayit_sayisi)
        yaz("KIMLIK", kart_kimligi)
        if self.tani_ek:
            try:
                for anahtar, deger in self.tani_ek().items():
                    print(anahtar + "=" + str(deger).replace("\n", " ").replace("\r", " "))
            except Exception:
                pass
        print("SON")

    def _led(self, komut):
        if self.on_led is None:
            print("HATA: LED desteklenmiyor")
            return
        try:
            parcalar = komut.split(" ")
            ek = parcalar[4:6]           # hız ve akış renkleri (yeni uygulama gönderir)
            try:
                self.on_led(parcalar[1], parcalar[2], int(parcalar[3]), *ek)
            except TypeError:
                # eski code.py: yalnızca mod, renk, parlaklık alır
                self.on_led(parcalar[1], parcalar[2], int(parcalar[3]))
            print("OK")
        except Exception as e:
            print("HATA:", e)

    # -----------------------------------------------------
    # Kart yazılımı güncelleme
    # -----------------------------------------------------

    def _dosya_baslat(self, ad):
        self._dosya_iptal()
        if not guncellenebilir(ad):
            print("HATA: bu dosya guncellenemez:", ad)
            return
        try:
            self._dosya_adi = ad
            self._dosya = open("/" + ad + ".tmp", "wb")
            print("OK")
        except Exception as e:
            self._dosya = None
            self._dosya_adi = None
            print("HATA:", e)

    def _dosya_parca(self, b64):
        if self._dosya is None:
            print("HATA: acik dosya yok")
            return
        try:
            self._dosya.write(binascii.a2b_base64(b64))
            print("OK")
        except Exception as e:
            self._dosya_iptal()
            print("HATA:", e)

    def _dosya_bitir(self, boyut):
        if self._dosya is None:
            print("HATA: acik dosya yok")
            return

        ad = self._dosya_adi
        gecici = "/" + ad + ".tmp"
        try:
            self._dosya.close()
            self._dosya = None

            if os.stat(gecici)[6] != int(boyut):
                raise ValueError("boyut tutmuyor")

            self._yedekle(ad)
            try:
                os.remove("/" + ad)
            except OSError:
                pass
            os.rename(gecici, "/" + ad)
            self._dosya_adi = None
            print("OK")
        except Exception as e:
            self._dosya_iptal()
            print("HATA:", e)

    def _yedekle(self, ad):
        """Dosyanın güncellemeden önceki hâlini /yedek'e kopyalar (güncelleme başına bir kez)."""
        if self._yedeklenen is None:
            # yeni güncelleme: önceki yedekleri temizle
            self._yedeklenen = []
            try:
                os.mkdir(YEDEK)
            except OSError:
                pass
            try:
                for eski in os.listdir(YEDEK):
                    _sil(YEDEK + "/" + eski)
            except OSError:
                pass
        if ad in self._yedeklenen:
            return
        self._yedeklenen.append(ad)
        if _var("/" + ad):
            _kopyala("/" + ad, YEDEK + "/" + ad)
        else:
            with open(YENI_DOSYALAR, "a") as d:
                d.write(ad + "\n")

    def _denemeye_hazirla(self):
        """Güncellemeden sonra yeniden başlarken: yeni yazılım deneme olarak açılsın."""
        if not self._yedeklenen:
            return
        try:
            with open(DENEME, "w") as d:
                d.write(self.surum)
            # Sonraki çalışmada code.py yerine bu dosya ana program olur (CircuitPython 7.3+)
            supervisor.set_next_code_file("/settings_server.py")
        except Exception as e:
            _sil(DENEME)                     # deneme modu yoksa onay bekleyen de olmasın
            print("Deneme modu açılamadı:", e)

    def _dosya_iptal(self):
        if self._dosya is not None:
            try:
                self._dosya.close()
            except Exception:
                pass
            self._dosya = None
        if self._dosya_adi:
            try:
                os.remove("/" + self._dosya_adi + ".tmp")
            except OSError:
                pass
            self._dosya_adi = None


# =========================================================
# GÜVENLİ GÜNCELLEME: yeni yazılımı dene, olmazsa eskisine dön
# =========================================================

def geri_don(neden):
    """/yedek'teki dosyaları geri koyar, nedeni /geri_donus.txt'ye yazar."""
    try:
        eski = open(DENEME).read().strip()
    except OSError:
        eski = "?"
    try:
        for ad in os.listdir(YEDEK):
            if guncellenebilir(ad):
                _kopyala(YEDEK + "/" + ad, "/" + ad)
    except OSError:
        pass
    try:
        with open(YENI_DOSYALAR) as d:
            for ad in d.read().split():
                if guncellenebilir(ad):
                    _sil("/" + ad)
    except OSError:
        pass
    _sil(YENI_DOSYALAR)
    _sil(DENEME)
    try:
        with open(GERI_DONUS, "w") as d:
            d.write(eski + "|" + neden[:180])
    except OSError:
        pass
    print("Yeni yazılım çalışmadı, " + eski + " sürümüne geri dönüldü:", neden)


def yedek_surumu():
    """Yedekteki (son güncellemeden önceki) yazılımın sürümü; yedek yoksa None."""
    try:
        with open(YEDEK + "/code.py") as d:
            for _ in range(80):
                satir = d.readline()
                if not satir:
                    break
                if satir.startswith("SURUM"):
                    return satir.split('"')[1]
        return "?"
    except (OSError, IndexError):
        return None


def yedege_don():
    """Elle geri alma: yedekteki dosyaları geri koyar, güncellemeyle gelen yeni dosyaları siler.
    Yedek kullanıldıktan sonra silinir (iki kez geri alınmaz)."""
    try:
        for ad in os.listdir(YEDEK):
            if guncellenebilir(ad):
                _kopyala(YEDEK + "/" + ad, "/" + ad)
    except OSError:
        pass
    try:
        with open(YENI_DOSYALAR) as d:
            for ad in d.read().split():
                if guncellenebilir(ad):
                    _sil("/" + ad)
    except OSError:
        pass
    try:
        for ad in os.listdir(YEDEK):
            _sil(YEDEK + "/" + ad)
    except OSError:
        pass
    print("Önceki kart yazılımına dönüldü")


def deneme_calistir():
    supervisor.runtime.autoreload = False
    if not _var(DENEME):
        import code                          # deneme yoksa normal açılış
        return
    try:
        import code                          # yeni yazılım: normalde buradan hiç dönmez
        neden = "yazılım beklenmedik şekilde durdu"
    except Exception as e:
        neden = type(e).__name__ + ": " + str(e)
    geri_don(neden)
    time.sleep(0.5)
    supervisor.reload()


if __name__ == "__main__":
    deneme_calistir()
