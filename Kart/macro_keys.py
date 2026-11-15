"""
macro_keys.py - CircuitPython HID makro kütüphanesi

Kullanım:
    from macro_keys import MacroKeys, load_settings

    macros = MacroKeys()
    macros.run("CTRL+ALT+S")          # klavye kısayolu
    macros.run("MEDIA_PLAY_PAUSE")    # medya tuşu
    macros.run("DISPLAY")             # Windows+P (ekran değiştirme)
    macros.run("TEXT:Merhaba dünya")  # metin yazdır (US düzeni)
    macros.run("TEXT:sifre{ENTER}")   # yazıdan sonra Enter'a bas
    macros.duzen = "TR"               # bilgisayar Türkçe Q klavye kullanıyorsa (ç ş ğ ü ö ı yazılır)
    macros.duzen = "TRF"              # bilgisayar Türkçe F klavye kullanıyorsa
    macros.volume_up()
    macros.run("SCROLL_DOWN")         # fare tekerleği (1 çentik)
    macros.run("SCROLL_DOWN:5")       # fare tekerleği 5 çentik
    macros.run("MOUSE_MOVE:100:-50")  # imleci 100 px sağa, 50 px yukarı taşı (kart 2.4)

    while True:
        macros.update()               # Windows tuşunu zamanı gelince bırakır
"""

import time
import usb_hid

from adafruit_hid.keyboard import Keyboard

from adafruit_hid.keyboard_layout_us import KeyboardLayoutUS
from adafruit_hid.keycode import Keycode
from adafruit_hid.consumer_control import ConsumerControl
from adafruit_hid.consumer_control_code import ConsumerControlCode

# Fare (tekerlek ve tıklama). adafruit_hid/mouse.mpy yoksa fare komutları sessizce devre dışı kalır.
try:
    from adafruit_hid.mouse import Mouse
except ImportError:
    Mouse = None


# ---------------------------------------------------------
# Tuş isimleri (settings.txt'de yazılabilecek isimler)
# keycode.mpy içindeki tüm tuşlar + kısa takma adlar
# ---------------------------------------------------------

KEY_ALIASES = {
    # --- Harfler ---
    "A": Keycode.A, "B": Keycode.B, "C": Keycode.C, "D": Keycode.D,
    "E": Keycode.E, "F": Keycode.F, "G": Keycode.G, "H": Keycode.H,
    "I": Keycode.I, "J": Keycode.J, "K": Keycode.K, "L": Keycode.L,
    "M": Keycode.M, "N": Keycode.N, "O": Keycode.O, "P": Keycode.P,
    "Q": Keycode.Q, "R": Keycode.R, "S": Keycode.S, "T": Keycode.T,
    "U": Keycode.U, "V": Keycode.V, "W": Keycode.W, "X": Keycode.X,
    "Y": Keycode.Y, "Z": Keycode.Z,

    # --- Rakamlar ---
    "ONE": Keycode.ONE, "TWO": Keycode.TWO, "THREE": Keycode.THREE,
    "FOUR": Keycode.FOUR, "FIVE": Keycode.FIVE, "SIX": Keycode.SIX,
    "SEVEN": Keycode.SEVEN, "EIGHT": Keycode.EIGHT, "NINE": Keycode.NINE,
    "ZERO": Keycode.ZERO,
    "1": Keycode.ONE, "2": Keycode.TWO, "3": Keycode.THREE,
    "4": Keycode.FOUR, "5": Keycode.FIVE, "6": Keycode.SIX,
    "7": Keycode.SEVEN, "8": Keycode.EIGHT, "9": Keycode.NINE,
    "0": Keycode.ZERO,

    # --- Fonksiyon tuşları ---
    "F1": Keycode.F1, "F2": Keycode.F2, "F3": Keycode.F3,
    "F4": Keycode.F4, "F5": Keycode.F5, "F6": Keycode.F6,
    "F7": Keycode.F7, "F8": Keycode.F8, "F9": Keycode.F9,
    "F10": Keycode.F10, "F11": Keycode.F11, "F12": Keycode.F12,
    "F13": Keycode.F13, "F14": Keycode.F14, "F15": Keycode.F15,
    "F16": Keycode.F16, "F17": Keycode.F17, "F18": Keycode.F18,
    "F19": Keycode.F19, "F20": Keycode.F20, "F21": Keycode.F21,
    "F22": Keycode.F22, "F23": Keycode.F23, "F24": Keycode.F24,

    # --- Düzenleme / genel ---
    "ENTER": Keycode.ENTER,
    "RETURN": Keycode.RETURN,
    "ESCAPE": Keycode.ESCAPE,
    "ESC": Keycode.ESCAPE,
    "BACKSPACE": Keycode.BACKSPACE,
    "BKSP": Keycode.BACKSPACE,
    "TAB": Keycode.TAB,
    "SPACEBAR": Keycode.SPACEBAR,
    "SPACE": Keycode.SPACE,
    "DELETE": Keycode.DELETE,
    "DEL": Keycode.DELETE,
    "INSERT": Keycode.INSERT,
    "HOME": Keycode.HOME,
    "END": Keycode.END,
    "PAGE_UP": Keycode.PAGE_UP,
    "PGUP": Keycode.PAGE_UP,
    "PAGE_DOWN": Keycode.PAGE_DOWN,
    "PGDN": Keycode.PAGE_DOWN,
    "CAPS_LOCK": Keycode.CAPS_LOCK,
    "CAPS": Keycode.CAPS_LOCK,
    "PRINT_SCREEN": Keycode.PRINT_SCREEN,
    "PRTSC": Keycode.PRINT_SCREEN,
    "SCROLL_LOCK": Keycode.SCROLL_LOCK,
    "PAUSE": Keycode.PAUSE,
    "APPLICATION": Keycode.APPLICATION,
    "MENU": Keycode.APPLICATION,
    "POWER": Keycode.POWER,

    # --- Oklar ---
    "UP_ARROW": Keycode.UP_ARROW,
    "DOWN_ARROW": Keycode.DOWN_ARROW,
    "LEFT_ARROW": Keycode.LEFT_ARROW,
    "RIGHT_ARROW": Keycode.RIGHT_ARROW,
    "UP": Keycode.UP_ARROW,
    "DOWN": Keycode.DOWN_ARROW,
    "LEFT": Keycode.LEFT_ARROW,
    "RIGHT": Keycode.RIGHT_ARROW,

    # --- Noktalama ---
    "MINUS": Keycode.MINUS,
    "EQUALS": Keycode.EQUALS,
    "LEFT_BRACKET": Keycode.LEFT_BRACKET,
    "RIGHT_BRACKET": Keycode.RIGHT_BRACKET,
    "BACKSLASH": Keycode.BACKSLASH,
    "POUND": Keycode.POUND,
    "SEMICOLON": Keycode.SEMICOLON,
    "QUOTE": Keycode.QUOTE,
    "GRAVE_ACCENT": Keycode.GRAVE_ACCENT,
    "COMMA": Keycode.COMMA,
    "PERIOD": Keycode.PERIOD,
    "FORWARD_SLASH": Keycode.FORWARD_SLASH,

    # --- Sayısal tuş takımı ---
    "KEYPAD_NUMLOCK": Keycode.KEYPAD_NUMLOCK,
    "KEYPAD_FORWARD_SLASH": Keycode.KEYPAD_FORWARD_SLASH,
    "KEYPAD_ASTERISK": Keycode.KEYPAD_ASTERISK,
    "KEYPAD_MINUS": Keycode.KEYPAD_MINUS,
    "KEYPAD_PLUS": Keycode.KEYPAD_PLUS,
    "KEYPAD_ENTER": Keycode.KEYPAD_ENTER,
    "KEYPAD_ONE": Keycode.KEYPAD_ONE,
    "KEYPAD_TWO": Keycode.KEYPAD_TWO,
    "KEYPAD_THREE": Keycode.KEYPAD_THREE,
    "KEYPAD_FOUR": Keycode.KEYPAD_FOUR,
    "KEYPAD_FIVE": Keycode.KEYPAD_FIVE,
    "KEYPAD_SIX": Keycode.KEYPAD_SIX,
    "KEYPAD_SEVEN": Keycode.KEYPAD_SEVEN,
    "KEYPAD_EIGHT": Keycode.KEYPAD_EIGHT,
    "KEYPAD_NINE": Keycode.KEYPAD_NINE,
    "KEYPAD_ZERO": Keycode.KEYPAD_ZERO,
    "KEYPAD_PERIOD": Keycode.KEYPAD_PERIOD,
    "KEYPAD_BACKSLASH": Keycode.KEYPAD_BACKSLASH,
    "KEYPAD_EQUALS": Keycode.KEYPAD_EQUALS,

    # --- Değiştirici (modifier) tuşlar ---
    "CONTROL": Keycode.CONTROL,
    "CTRL": Keycode.CONTROL,
    "LEFT_CONTROL": Keycode.LEFT_CONTROL,
    "RIGHT_CONTROL": Keycode.RIGHT_CONTROL,
    "SHIFT": Keycode.SHIFT,
    "LEFT_SHIFT": Keycode.LEFT_SHIFT,
    "RIGHT_SHIFT": Keycode.RIGHT_SHIFT,
    "ALT": Keycode.ALT,
    "LEFT_ALT": Keycode.LEFT_ALT,
    "RIGHT_ALT": Keycode.RIGHT_ALT,
    "OPTION": Keycode.OPTION,
    "GUI": Keycode.GUI,
    "LEFT_GUI": Keycode.LEFT_GUI,
    "RIGHT_GUI": Keycode.RIGHT_GUI,
    "WINDOWS": Keycode.WINDOWS,
    "WIN": Keycode.WINDOWS,
    "COMMAND": Keycode.COMMAND,
}

# Medya komutları
CONSUMER_COMMANDS = {
    "MEDIA_PREVIOUS": ConsumerControlCode.SCAN_PREVIOUS_TRACK,
    "MEDIA_NEXT": ConsumerControlCode.SCAN_NEXT_TRACK,
    "MEDIA_PLAY_PAUSE": ConsumerControlCode.PLAY_PAUSE,
    "MUTE": ConsumerControlCode.MUTE,
    "VOLUME_UP": ConsumerControlCode.VOLUME_INCREMENT,
    "VOLUME_DOWN": ConsumerControlCode.VOLUME_DECREMENT,
    # Özel klavye tuşları (HID Consumer kodları; adafruit_hid'de adı olmayanlar sayı olarak)
    "MEDIA_STOP": 0xB7,
    "MEDIA_FAST_FORWARD": 0xB3,
    "MEDIA_REWIND": 0xB4,
    "BRIGHTNESS_UP": 0x6F,     # ekran parlaklığı (çoğunlukla dizüstünde çalışır)
    "BRIGHTNESS_DOWN": 0x70,
    "CALCULATOR": 0x192,
    "EMAIL": 0x18A,
    "MY_COMPUTER": 0x194,      # Bu bilgisayar / Dosya Gezgini
    "BROWSER_HOME": 0x223,     # varsayılan tarayıcı
    "WINDOWS_SEARCH": 0x221,   # Windows araması
    "BROWSER_BACK": 0x224,
    "BROWSER_FORWARD": 0x225,
    "BROWSER_STOP": 0x226,
    "BROWSER_REFRESH": 0x227,
    "BROWSER_FAVORITES": 0x22A,
    "MEDIA_PLAYER": 0x183,     # varsayılan medya oynatıcı
}


# ---------------------------------------------------------
# Türkçe Q klavye düzeni (Windows). Kaynak: Neradoc/Circuitpython_Keyboard_Layouts
# (keyboard_layout_win_tr.py, MIT lisansı). Bir değer: tuş kodu, 0x80 biti = Shift.
# TR_ALTGR'deki karakterler sağ Alt (AltGr) ile basılır. 0 = bu düzende yazılamaz (^ ` ~).
# ---------------------------------------------------------
TR_ASCII = (
    b'\x00\x00\x00\x00\x00\x00\x00\x00*+(\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
    b'\x00\x00\x00\x00\x00)\x00\x00\x00\x00,\x9e5 !\xa2\xa3\x9f\xa5\xa6-\xa11.8\xa4\'\x1e\x1f'
    b' !"#$%&\xb8\xb15\xa7\x1e\xad\x14\x84\x85\x86\x87\x88\x89\x8a\x8b\x8c\x8d\x8e\x8f'
    b'\x90\x91\x92\x93\x94\x95\x96\x97\x98\x99\x9a\x9b\x9c\x9d%-&\x00\xae\x00\x04\x05'
    b'\x06\x07\x08\t\n\x0b\x0c\r\x0e\x0f\x10\x11\x12\x13\x14\x15\x16\x17\x18\x19\x1a\x1b'
    b'\x1c\x1d$.\'\x00\x00'
)
TR_ALTGR = "#$<>@[\\]i{|}£½€₺"
TR_YUKSEK = {
    "ı": 0x0C, "İ": 0xB4, "ğ": 0x2F, "Ğ": 0xAF, "ü": 0x30, "Ü": 0xB0,
    "ş": 0x33, "Ş": 0xB3, "ö": 0x36, "Ö": 0xB6, "ç": 0x37, "Ç": 0xB7,
    "é": 0xB5, "£": 0x1F, "½": 0x22, "€": 0x08, "₺": 0x17,
}


# ---------------------------------------------------------
# Türkçe F klavye düzeni (Windows KBDTUF). Aynı biçim: tuş kodu, 0x80 biti = Shift,
# TRF_ALTGR'dekiler AltGr ile. ^ ` ~ ölü tuş olduğu için yazılamaz (0).
# ---------------------------------------------------------
TRF_ASCII = (
    b'\x00\x00\x00\x00\x00\x00\x00\x00*+(\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00'
    b'\x00\x00\x00\x00\x00)\x00\x00\x00\x00,\x9e\x9f \xa1\xa2\xa3\xa4\xa5\xa6\xb558.7-\'\x1e\x1f'
    b' !"#$%&\xb7\xb8d\xa7\xe4\xad\x14\x89\xb6\x99\x9c\x87\x94\x9a\x92\x95\x9d\x8d\x8f'
    b'\x8e\x8c\x97\x93\xaf\x98\x90\x8b\x84\x86\xb0\xb1\xb3\x91%-&\x00\xae\x00\t6'
    b'\x19\x1c\x07\x14\x1a\x12\x16\x1d\r\x0f\x0e\x0c\x17\x13/\x18\x10\x0b\x04\x06013'
    b'\x11$.\'\x00\x00'
)
TRF_ALTGR = "#@[\\]{|}€₺£½¼¾"
TRF_YUKSEK = {
    "ı": 0x15, "İ": 0x96, "ğ": 0x08, "Ğ": 0x88, "ü": 0x0A, "Ü": 0x8A,
    "ş": 0x34, "Ş": 0xB4, "ö": 0x1B, "Ö": 0x9B, "ç": 0x05, "Ç": 0x85,
    "€": 0x07, "₺": 0x0B, "£": 0x13, "½": 0x22, "¼": 0x21, "¾": 0x23,
}


# ---------------------------------------------------------
# Düzende olmayan karakterler (é, ä, ñ, ß, ^ ...): Windows Alt kodu (Alt + sayısal tuş takımında 0nnn).
# Windows'un ANSI kod sayfasının 128-255 arası: ABD Windows'u 1252, Türkçe Windows'u 1254.
# Boşluk (\x00) = o kodda karakter yok.
# ---------------------------------------------------------
ANSI_1252 = "\u20ac\u0000\u201a\u0192\u201e\u2026\u2020\u2021\u02c6\u2030\u0160\u2039\u0152\u0000\u017d\u0000\u0000\u2018\u2019\u201c\u201d\u2022\u2013\u2014\u02dc\u2122\u0161\u203a\u0153\u0000\u017e\u0178\u00a0\u00a1\u00a2\u00a3\u00a4\u00a5\u00a6\u00a7\u00a8\u00a9\u00aa\u00ab\u00ac\u00ad\u00ae\u00af\u00b0\u00b1\u00b2\u00b3\u00b4\u00b5\u00b6\u00b7\u00b8\u00b9\u00ba\u00bb\u00bc\u00bd\u00be\u00bf\u00c0\u00c1\u00c2\u00c3\u00c4\u00c5\u00c6\u00c7\u00c8\u00c9\u00ca\u00cb\u00cc\u00cd\u00ce\u00cf\u00d0\u00d1\u00d2\u00d3\u00d4\u00d5\u00d6\u00d7\u00d8\u00d9\u00da\u00db\u00dc\u00dd\u00de\u00df\u00e0\u00e1\u00e2\u00e3\u00e4\u00e5\u00e6\u00e7\u00e8\u00e9\u00ea\u00eb\u00ec\u00ed\u00ee\u00ef\u00f0\u00f1\u00f2\u00f3\u00f4\u00f5\u00f6\u00f7\u00f8\u00f9\u00fa\u00fb\u00fc\u00fd\u00fe\u00ff"
ANSI_1254 = "\u20ac\u0000\u201a\u0192\u201e\u2026\u2020\u2021\u02c6\u2030\u0160\u2039\u0152\u0000\u0000\u0000\u0000\u2018\u2019\u201c\u201d\u2022\u2013\u2014\u02dc\u2122\u0161\u203a\u0153\u0000\u0000\u0178\u00a0\u00a1\u00a2\u00a3\u00a4\u00a5\u00a6\u00a7\u00a8\u00a9\u00aa\u00ab\u00ac\u00ad\u00ae\u00af\u00b0\u00b1\u00b2\u00b3\u00b4\u00b5\u00b6\u00b7\u00b8\u00b9\u00ba\u00bb\u00bc\u00bd\u00be\u00bf\u00c0\u00c1\u00c2\u00c3\u00c4\u00c5\u00c6\u00c7\u00c8\u00c9\u00ca\u00cb\u00cc\u00cd\u00ce\u00cf\u011e\u00d1\u00d2\u00d3\u00d4\u00d5\u00d6\u00d7\u00d8\u00d9\u00da\u00db\u00dc\u0130\u015e\u00df\u00e0\u00e1\u00e2\u00e3\u00e4\u00e5\u00e6\u00e7\u00e8\u00e9\u00ea\u00eb\u00ec\u00ed\u00ee\u00ef\u011f\u00f1\u00f2\u00f3\u00f4\u00f5\u00f6\u00f7\u00f8\u00f9\u00fa\u00fb\u00fc\u0131\u015f\u00ff"
_ansi = {}


def ansi_kodu(ch, turkce):
    """Karakterin Windows ANSI kodu (Alt+0nnn için); yoksa None."""
    o = ord(ch)
    if 32 <= o < 127:
        return o
    tablo = ANSI_1254 if turkce else ANSI_1252
    if tablo not in _ansi:
        _ansi[tablo] = {c: 128 + i for i, c in enumerate(tablo) if c != "\x00"}
    return _ansi[tablo].get(ch)


def tr_tuslari(ch, f=False):
    """Türkçe Q (f=True: Türkçe F) düzeninde bir karakter için basılacak tuşlar; yazılamıyorsa None."""
    o = ord(ch)
    if o < 128:
        kod = (TRF_ASCII if f else TR_ASCII)[o]
    else:
        kod = (TRF_YUKSEK if f else TR_YUKSEK).get(ch, 0)
    if not kod:
        return None
    tuslar = []
    if kod & 0x80:
        tuslar.append(Keycode.SHIFT)
    if ch in (TRF_ALTGR if f else TR_ALTGR):
        tuslar.append(Keycode.RIGHT_ALT)
    tuslar.append(kod & 0x7F)
    return tuslar


def split_text_macro(text, gecerli_mi):
    """
    'sifre{TAB}ab{ENTER}' -> [("text","sifre"), ("key","TAB"), ("text","ab"), ("key","ENTER")]
    Geçerli olmayan {...} parçaları düz yazı olarak kalır.
    """
    parcalar = []
    tampon = ""
    i = 0
    n = len(text)

    while i < n:
        c = text[i]
        if c == "{":
            j = text.find("}", i + 1)
            if j > i + 1 and gecerli_mi(text[i + 1:j]):
                if tampon:
                    parcalar.append(("text", tampon))
                    tampon = ""
                parcalar.append(("key", text[i + 1:j]))
                i = j + 1
                continue
        tampon += c
        i += 1

    if tampon:
        parcalar.append(("text", tampon))
    return parcalar

# Fare komutları: encoder ile kaydırma / yakınlaştırma için idealdir
MOUSE_COMMANDS = (
    "SCROLL_UP",     # tekerlek yukarı
    "SCROLL_DOWN",   # tekerlek aşağı
    "ZOOM_IN",       # Ctrl + tekerlek yukarı
    "ZOOM_OUT",      # Ctrl + tekerlek aşağı
    "MOUSE_LEFT",    # sol tık
    "MOUSE_RIGHT",   # sağ tık
    "MOUSE_MIDDLE",  # orta tık
)


def split_macro(metin):
    """
    Makro gövdesini adımlara ayırır. Adımlar | ile ayrılır;
    yazının içindeki | ve \\ karakterleri \\| ve \\\\ olarak kaçırılmıştır.
        'CTRL+C|WAIT:200|ALT+TAB|TEXT:a\\|b' -> ['CTRL+C', 'WAIT:200', 'ALT+TAB', 'TEXT:a|b']
    """
    adimlar = []
    tampon = ""
    i = 0
    n = len(metin)

    while i < n:
        c = metin[i]
        if c == "\\" and i + 1 < n:
            tampon += metin[i + 1]
            i += 2
        elif c == "|":
            adimlar.append(tampon)
            tampon = ""
            i += 1
        else:
            tampon += c
            i += 1

    adimlar.append(tampon)
    return [a for a in adimlar if a]


# ---------------------------------------------------------
# settings.txt okuma
# ---------------------------------------------------------

def load_settings(path="/settings.txt", defaults=None):
    """
    KEY=VALUE satırlarını okur. Boş satırlar ve # ile başlayanlar atlanır.
    Dosyada olmayan ayarlar için `defaults` değerleri kullanılır.
    """
    settings = dict(defaults) if defaults else {}

    try:
        with open(path, "r") as file:
            for line in file:
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                key, value = line.split("=", 1)
                settings[key.strip()] = value.strip()
        print(path, "okundu.")
    except Exception as e:
        print(path, "okunamadı, varsayılanlar kullanılıyor:", e)

    return settings


# ---------------------------------------------------------
# Ana sınıf
# ---------------------------------------------------------

class MacroKeys:
    def __init__(self, devices=None, win_p_timeout=1.4):
        """
        devices       : usb_hid.devices (verilmezse otomatik alınır)
        win_p_timeout : Windows+P menüsünde son basıştan sonra
                        Windows tuşunun ne kadar süre basılı tutulacağı (sn)
        """
        devices = devices or usb_hid.devices
        self.keyboard = Keyboard(devices)
        self.layout = KeyboardLayoutUS(self.keyboard)
        self.consumer = ConsumerControl(devices)

        self.mouse = None
        if Mouse is not None:
            try:
                self.mouse = Mouse(devices)
            except Exception as e:
                print("Fare başlatılamadı:", e)

        self.win_p_timeout = win_p_timeout
        self.duzen = "US"                 # "US", "TR" (Türkçe Q) ya da "TRF" (Türkçe F): bilgisayardaki düzen
        self.harf_bekleme = 0             # sn: metin yazarken harfler arası bekleme (0 = en hızlı)
        self._winp_open = False
        self._winp_last = 0

    # -----------------------------------------------------
    # Komut çalıştırma
    # -----------------------------------------------------

    def run(self, command):
        """
        Komutu çalıştırır. Tanınırsa True, tanınmazsa False döner.
        (PIXEL_MODE gibi kütüphane dışı komutları ana kod ele alır.)
        """
        if command.startswith("TEXT:"):
            return self.type_text(command[5:])

        if command in CONSUMER_COMMANDS:
            self.consumer.send(CONSUMER_COMMANDS[command])
            return True

        if command == "DISPLAY":
            self._display_switch()
            return True

        if command in MOUSE_COMMANDS:
            return self.mouse_command(command)

        if command.startswith("SCROLL_UP:") or command.startswith("SCROLL_DOWN:"):
            return self.scroll(command)

        if command.startswith("MOUSE_MOVE:"):
            return self.mouse_move(command)

        return self.send_combo(command)

    # -----------------------------------------------------
    # Fare
    # -----------------------------------------------------

    def mouse_command(self, command):
        if self.mouse is None:
            print("Fare desteği yok (adafruit_hid/mouse.mpy eksik olabilir)")
            return False

        if command == "SCROLL_UP":
            self.mouse.move(wheel=1)
        elif command == "SCROLL_DOWN":
            self.mouse.move(wheel=-1)
        elif command in ("ZOOM_IN", "ZOOM_OUT"):
            self.keyboard.press(Keycode.CONTROL)
            self.mouse.move(wheel=1 if command == "ZOOM_IN" else -1)
            self.keyboard.release(Keycode.CONTROL)
        elif command == "MOUSE_LEFT":
            self.mouse.click(Mouse.LEFT_BUTTON)
        elif command == "MOUSE_RIGHT":
            self.mouse.click(Mouse.RIGHT_BUTTON)
        elif command == "MOUSE_MIDDLE":
            self.mouse.click(Mouse.MIDDLE_BUTTON)
        return True

    def scroll(self, command):
        """'SCROLL_DOWN:5' -> tekerleği 5 çentik aşağı (1-50)."""
        if self.mouse is None:
            print("Fare desteği yok (adafruit_hid/mouse.mpy eksik olabilir)")
            return False
        ad, _, sayi = command.partition(":")
        try:
            n = max(1, min(50, int(sayi)))
        except ValueError:
            n = 1
        self.mouse.move(wheel=n if ad == "SCROLL_UP" else -n)
        return True

    def mouse_move(self, command):
        """'MOUSE_MOVE:100:-50' -> imleç 100 px sağa, 50 px yukarı (x: sağa +, y: aşağı +)."""
        if self.mouse is None:
            print("Fare desteği yok (adafruit_hid/mouse.mpy eksik olabilir)")
            return False
        try:
            _, x, y = command.split(":")
            x = max(-4000, min(4000, int(x)))
            y = max(-4000, min(4000, int(y)))
        except ValueError:
            return False
        # Tek raporda en fazla 127 px gider; parça parça gönderilir
        while x or y:
            px = max(-127, min(127, x))
            py = max(-127, min(127, y))
            self.mouse.move(x=px, y=py)
            x -= px
            y -= py
        return True

    def send_combo(self, command):
        """'CTRL+ALT+S' gibi bir kombinasyonu gönderir."""
        keys = []

        for part in command.split("+"):
            keycode = self.find_key(part.strip())
            if keycode is None:
                print("Bilinmeyen tuş:", part)
                return False
            keys.append(keycode)

        if keys:
            self.keyboard.send(*keys)
            return True
        return False

    # -----------------------------------------------------
    # Tuş arama / yardımcılar (keycode.mpy)
    # -----------------------------------------------------

    @staticmethod
    def find_key(name):
        """İsimden keycode bulur; yoksa None. Önce KEY_ALIASES, sonra Keycode."""
        name = name.strip().upper()
        if name in KEY_ALIASES:
            return KEY_ALIASES[name]
        return getattr(Keycode, name, None)

    @staticmethod
    def is_modifier(keycode):
        """Ctrl/Shift/Alt/GUI gibi bir değiştirici tuşsa True."""
        return Keycode.modifier_bit(keycode) != 0

    @staticmethod
    def key_names():
        """Kullanılabilen tüm tuş isimlerini döndürür (sıralı)."""
        return sorted(KEY_ALIASES.keys())

    # -----------------------------------------------------
    # Metin yazma (keyboard_layout_us.mpy)
    # -----------------------------------------------------

    # -----------------------------------------------------
    # Basılı tut / bırak (makroda PRESS:x, RELEASE:x). x: kısayol ("SHIFT", "CTRL+ALT") ya da MOUSE_LEFT/RIGHT/MIDDLE
    # -----------------------------------------------------

    def _fare_tusu(self, ad):
        if Mouse is None:
            return None
        return {"MOUSE_LEFT": Mouse.LEFT_BUTTON, "MOUSE_RIGHT": Mouse.RIGHT_BUTTON,
                "MOUSE_MIDDLE": Mouse.MIDDLE_BUTTON}.get(ad)

    def basili_tut(self, ad, bas=True):
        """bas=True: basılı tutar, False: bırakır. Tanınırsa True."""
        tus = self._fare_tusu(ad)
        if tus is not None:
            if self.mouse is None:
                return False
            if bas:
                self.mouse.press(tus)
            else:
                self.mouse.release(tus)
            return True

        kodlar = []
        for parca in ad.split("+"):
            k = self.find_key(parca)
            if k is None:
                print("Bilinmeyen tuş:", parca)
                return False
            kodlar.append(k)
        if bas:
            self.keyboard.press(*kodlar)
        else:
            self.keyboard.release(*kodlar)
        return True

    def hepsini_birak(self):
        """Makro sonunda: basılı kalan tuş ve fare tuşu kalmasın."""
        try:
            self.keyboard.release_all()
        except Exception:
            pass
        if self.mouse is not None:
            try:
                self.mouse.release_all()
            except Exception:
                pass

    # -----------------------------------------------------
    # Alt kodu: düzende olmayan karakteri Alt + sayısal tuş takımıyla yazar
    # -----------------------------------------------------

    def _numlock_acik(self):
        try:
            return self.keyboard.led_on(Keyboard.LED_NUM_LOCK)
        except Exception:
            return True                   # bilinemiyorsa açık say

    def _alt_kodu(self, ch):
        kod = ansi_kodu(ch, self.duzen != "US")
        if kod is None:
            print("Yazılamıyor:", ch)
            return
        rakamlar = (Keycode.KEYPAD_ZERO, Keycode.KEYPAD_ONE, Keycode.KEYPAD_TWO, Keycode.KEYPAD_THREE,
                    Keycode.KEYPAD_FOUR, Keycode.KEYPAD_FIVE, Keycode.KEYPAD_SIX, Keycode.KEYPAD_SEVEN,
                    Keycode.KEYPAD_EIGHT, Keycode.KEYPAD_NINE)
        numlock_kapali = not self._numlock_acik()
        if numlock_kapali:
            self.keyboard.send(Keycode.KEYPAD_NUMLOCK)
        self.keyboard.press(Keycode.LEFT_ALT)
        for r in "%04d" % kod:
            self.keyboard.press(rakamlar[int(r)])
            self.keyboard.release(rakamlar[int(r)])
        self.keyboard.release(Keycode.LEFT_ALT)
        if numlock_kapali:
            self.keyboard.send(Keycode.KEYPAD_NUMLOCK)

    def _yaz_us(self, metin):
        """ABD düzeni: ASCII hızlıca, diğer karakterler Alt koduyla."""
        parca = ""
        for ch in metin:
            if ord(ch) < 128 and not self.harf_bekleme:
                parca += ch
                continue
            if parca:
                self.layout.write(parca)
                parca = ""
            if ord(ch) < 128:
                self.layout.write(ch)          # yavaş yazma: harf harf
            else:
                self._alt_kodu(ch)
            if self.harf_bekleme:
                time.sleep(self.harf_bekleme)
        if parca:
            self.layout.write(parca)

    def type_text(self, text):
        """
        Metni US klavye düzenine göre yazar.
        Süslü parantez içindeki tuşlara basar: "sifre{TAB}parola{ENTER}"
        {ENTER}, {TAB}, {ESC}, {CTRL+A} gibi geçerli tuş/kombinasyon adları kullanılabilir.
        """
        try:
            for tur, deger in split_text_macro(text, self._combo_gecerli):
                if tur != "text":
                    self.send_combo(deger)
                elif self.duzen in ("TR", "TRF"):
                    self._yaz_tr(deger)
                else:
                    self._yaz_us(deger)
            return True
        except Exception as e:
            print("Metin yazılamadı:", e)
            return False

    def _yaz_tr(self, metin):
        """Türkçe Q ya da F düzeninde yazar; düzende olmayan karakter atlanır."""
        f = self.duzen == "TRF"
        for ch in metin:
            tuslar = tr_tuslari(ch, f)
            if tuslar is None:
                self._alt_kodu(ch)            # düzende yok (é, ä, ^ ...): Alt koduyla
            else:
                self.keyboard.send(*tuslar)
            if self.harf_bekleme:
                time.sleep(self.harf_bekleme)

    def _combo_gecerli(self, ad):
        """'ENTER' ya da 'CTRL+A' gibi bir ad tamamen bilinen tuşlardan mı oluşuyor?"""
        for parca in ad.split("+"):
            if self.find_key(parca) is None:
                return False
        return True

    def keycodes_for(self, char):
        """Bir karakter için gereken keycode'ları döndürür (örn. 'A' -> SHIFT, A)."""
        return self.layout.keycodes(char)

    # -----------------------------------------------------
    # Ses
    # -----------------------------------------------------

    def volume_up(self):
        self.consumer.send(ConsumerControlCode.VOLUME_INCREMENT)

    def volume_down(self):
        self.consumer.send(ConsumerControlCode.VOLUME_DECREMENT)

    # -----------------------------------------------------
    # Windows + P (ekran değiştirme)
    # -----------------------------------------------------

    def _display_switch(self):
        now = time.monotonic()

        if not self._winp_open:
            # İlk basış: Windows basılı kalır, menü açılır
            self.keyboard.press(Keycode.WINDOWS)
            self.keyboard.press(Keycode.P)
            self.keyboard.release(Keycode.P)
            self._winp_open = True
            time.sleep(0.3)
        else:
            # Menü açıkken P'ye tekrar basınca sonraki seçeneğe geçer
            self.keyboard.press(Keycode.P)
            self.keyboard.release(Keycode.P)
            time.sleep(0.1)

        self._winp_last = now

    def update(self):
        """Ana döngüde sürekli çağır: süre dolunca Windows tuşunu bırakır."""
        if self._winp_open and (
            time.monotonic() - self._winp_last >= self.win_p_timeout
        ):
            self.keyboard.release(Keycode.WINDOWS)
            self._winp_open = False
