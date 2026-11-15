import board
import digitalio
import storage

# boot.py yalnızca kart USB'ye takılınca (ya da RESET'te) çalışır.
# Kart yazılımı güncellemesiyle gelir; hata verse bile kart açılsın diye her adım ayrı korunur.

# --- USB'de görünen ad: Windows'ta "Makro Klavye" yazar (kart 2.3+) ---
# Değişiklik klavye çıkarılıp yeniden takılınca görünür.
try:
    import supervisor
    supervisor.set_usb_identification(manufacturer="Makro Klavye", product="Makro Klavye")
except Exception:
    pass   # eski CircuitPython'da yok: varsayılan ad kalır

# --- Dosya sistemi ---
# Normalde: CIRCUITPY sürücüsü bilgisayarda HİÇ GÖRÜNMEZ, dosya sistemi kod tarafına açılır
# (kod settings.txt'ye yazabilir; kart yazılımı uygulamadan seri port üzerinden güncellenir).
#
# Kod dosyalarını PC'den elle kopyalamak istediğinde klavyeyi
# encoder'ın altındaki 9 numaralı tuşa (GP29) BASILI TUTARAK USB'ye tak:
# bu durumda CIRCUITPY sürücüsü görünür ve PC'ye yazılabilir kalır.
try:
    tus = digitalio.DigitalInOut(board.GP29)
    tus.switch_to_input(pull=digitalio.Pull.UP)
    basili = not tus.value
    tus.deinit()
except Exception as e:
    print("boot.py:", e)
    basili = False

if not basili:
    try:
        storage.remount("/", readonly=False)
    except Exception as e:
        print("boot.py remount:", e)
    try:
        storage.disable_usb_drive()      # CIRCUITPY sürücüsü bilgisayarda görünmesin
    except Exception as e:
        print("boot.py usb:", e)
