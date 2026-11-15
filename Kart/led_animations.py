"""
led_animations.py - CircuitPython NeoPixel animasyon kütüphanesi

Kullanım:
    import neopixel, board
    from led_animations import LedAnimator, hex_to_rgb

    pix = neopixel.NeoPixel(board.GP15, 13, brightness=1, auto_write=False)
    leds = LedAnimator(pix, mode="rainbow")

    # mod + renk + parlaklık (0-1) + hız (%) + akışın 3 rengi
    leds.ayarla("comet", hex_to_rgb("0066FF"), 0.5, hiz=150,
                renkler=((255, 0, 0), (140, 0, 255), (255, 255, 255)))

    while True:
        leds.update()        # bloklamaz, kendi zamanlamasını yönetir
        if button_pressed:
            leds.next_mode()

Renkli efektler (seçilen renkle çalışır):
    color, breathe, comet, chase, sparkle, wipe, scanner, heartbeat
Çok renkli akış:
    red_purple_white - 3 renk şerit boyunca akar (renkler ayarlanabilir,
                       varsayılan kırmızı > mor > beyaz)
Hız:
    100 = normal, 50 = yarı hız, 200 = iki kat hızlı (25-300 arası)

Eski adlar da çalışır: "red_comet", "purple_breathe", "white_wipe" ... -> o efekt o renkle,
"red_solid" / "purple_solid" / "white_solid" -> sabit renk.
"""

import time
import math
import random


def wheel(pos):
    """0-255 arası değeri renk çarkındaki (R, G, B) rengine çevirir."""
    pos &= 255
    if pos < 85:
        return (255 - pos * 3, pos * 3, 0)
    if pos < 170:
        pos -= 85
        return (0, 255 - pos * 3, pos * 3)
    pos -= 170
    return (pos * 3, 0, 255 - pos * 3)


def hex_to_rgb(hex_str):
    """'0066FF' ya da '#0066FF' -> (0, 102, 255)"""
    h = hex_str.strip().lstrip("#")
    if len(h) != 6:
        raise ValueError("renk 6 haneli olmali")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def _clamp(value, low=0, high=255):
    return max(low, min(high, value))


def _scale(color, k):
    """Rengi 0-1 arası katsayıyla karartır."""
    if k <= 0:
        return (0, 0, 0)
    if k >= 1:
        return color
    return (int(color[0] * k), int(color[1] * k), int(color[2] * k))


def _blend(a, b, t):
    """a rengiyle b rengi arasında t (0-1) oranında karışım."""
    return (
        int(a[0] + (b[0] - a[0]) * t),
        int(a[1] + (b[1] - a[1]) * t),
        int(a[2] + (b[2] - a[2]) * t),
    )


RED = (255, 0, 0)
PURPLE = (140, 0, 255)
WHITE = (255, 255, 255)

# Akışın varsayılan renkleri
FLOW_COLORS = (RED, PURPLE, WHITE)

# Eski "red_comet" gibi adlar için
NAMED_COLORS = {"red": RED, "purple": PURPLE, "white": WHITE}

BASE_INTERVAL = 0.02    # hız %100'deyken kareler arası süre (saniye)


class LedAnimator:
    # Mod sırası: next_mode() (PIXEL_MODE tuşu) bu sırayla ilerler.
    # Modlar settings.txt'de adla saklanır; sırayı değiştirmek eski ayarları bozmaz.
    MODES = (
        "white",       # Beyaz
        "red",         # Kırmızı
        "green",       # Yeşil
        "blue",        # Mavi
        "purple",      # Mor
        "color",       # Özel renk (sabit)
        "breathe",     # Nefes (seçilen renk)
        "rainbow",     # Gökkuşağı
        "random",      # Rastgele (sabit)
        "wave",        # Dalga
        "fire",        # Ateş
        "comet",       # Kuyruklu yıldız (seçilen renk)
        "scanner",     # Tarayıcı / ileri-geri (seçilen renk)
        "chase",       # Takip, tiyatro ışığı (seçilen renk)
        "wipe",        # Doldur / boşalt (seçilen renk)
        "sparkle",     # Pırıltı (seçilen renk)
        "confetti",    # Konfeti
        "colorcycle",  # Renk geçişi (tüm şerit)
        "red_purple_white",  # 3 renk akışı (renkler ayarlanabilir)
        "police",      # Polis çakarı
        "heartbeat",   # Kalp atışı (seçilen renk)
        "off",         # Kapalı
    )

    STATIC_COLORS = {
        "white": WHITE,
        "red": RED,
        "green": (0, 255, 0),
        "blue": (0, 0, 255),
        "purple": PURPLE,
        "off": (0, 0, 0),
    }

    # Kare çizmesi gerekmeyen (bir kez boyanan) modlar
    STATIC_MODES = ("white", "red", "green", "blue", "purple", "off", "color", "random")

    # Eski adlardaki efekt kısmı
    OLD_EFFECTS = ("solid", "breathe", "comet", "chase", "sparkle", "wipe", "scanner", "heartbeat")

    def __init__(self, pixels, mode=0, interval=BASE_INTERVAL):
        """
        pixels   : neopixel.NeoPixel nesnesi
        mode     : başlangıç modu (sayı veya isim)
        interval : kareler arası süre (saniye, hız %100 iken)
        """
        self.pixels = pixels
        self.count = len(pixels)
        self.interval = interval
        self.color = (0, 102, 255)            # renkli efektlerin rengi
        self.flow_colors = FLOW_COLORS        # akışın 3 rengi

        self._frame = 0
        self._rainbow_offset = 0
        self._last_update = 0
        self._levels = [0.0] * self.count     # pırıltı / tarayıcı parlaklık düzeyleri
        self._buf = [(0, 0, 0)] * self.count  # konfeti ara belleği
        self.parlaklik = 1.0                  # kullanıcının seçtiği parlaklık (0-1)
        self.akim_siniri = 400                # mA, 0 = sınırsız (USB'yi zorlamasın)

        self.mode = 0
        self.set_mode(mode)

    # -----------------------------------------------------
    # Mod yönetimi
    # -----------------------------------------------------

    @property
    def mode_name(self):
        return self.MODES[self.mode]

    def _cozumle(self, mode):
        """
        Mod adını doğrular. Eski "red_comet" gibi adlar (efekt, renk) olarak çözülür.
        Döner: (mod adı, renk ya da None). Bilinmeyen adda ValueError.
        """
        if mode in self.MODES:
            return mode, None

        parca = mode.split("_", 1)
        if len(parca) == 2 and parca[0] in NAMED_COLORS and parca[1] in self.OLD_EFFECTS:
            if parca[1] == "solid":
                return parca[0], None                       # red / purple / white sabit
            return parca[1], NAMED_COLORS[parca[0]]

        raise ValueError("bilinmeyen mod: " + mode)

    def set_mode(self, mode):
        """Modu sayı ya da isimle ayarlar."""
        if isinstance(mode, str):
            mode, renk = self._cozumle(mode)
            if renk is not None:
                self.color = renk
            mode = self.MODES.index(mode)
        self.mode = mode % len(self.MODES)
        self._frame = 0
        self._levels = [0.0] * self.count
        self._buf = [(0, 0, 0)] * self.count
        self._enter_mode()

    def next_mode(self):
        """Bir sonraki moda geçer ve yeni mod adını döndürür."""
        self.set_mode(self.mode + 1)
        return self.mode_name

    def prev_mode(self):
        """Bir önceki moda geçer ve yeni mod adını döndürür."""
        self.set_mode(self.mode - 1)
        return self.mode_name

    def set_speed(self, hiz):
        """Animasyon hızı (%): 100 normal, 50 yarı hız, 200 iki kat (25-300)."""
        try:
            hiz = int(hiz)
        except (TypeError, ValueError):
            hiz = 100
        hiz = max(25, min(300, hiz))
        self.interval = BASE_INTERVAL * 100 / hiz

    def ayarla(self, mod, renk, parlaklik, hiz=100, renkler=None):
        """
        Mod, renk ((r, g, b)), parlaklık (0-1), hız (%) ve akışın 3 rengini tek seferde uygular.
        Geçersiz mod adı ValueError verir; o durumda hiçbir şey değişmez.
        """
        if isinstance(mod, str):
            self._cozumle(mod)               # bilinmeyen adda burada hata verir

        self.color = renk
        if renkler:
            self.flow_colors = tuple(renkler)
        self.set_speed(hiz)
        self.parlaklik = max(0.02, min(1.0, parlaklik))
        self.pixels.brightness = self.parlaklik
        self.set_mode(mod)

    # Bir LED tam parlaklıkta renk başına ~20 mA çeker (beyaz ~60 mA), kapalıyken ~1 mA.
    LED_MA = 20
    BOSTA_MA = 1

    def _goster(self):
        """Kareyi gösterir; tahmini akım sınırı aşıyorsa parlaklık o kare için kısılır."""
        p = self.parlaklik
        if self.akim_siniri > 0:
            toplam = 0
            for c in self.pixels[:]:
                toplam += c[0] + c[1] + c[2]
            led_ma = toplam * self.LED_MA / 255   # parlaklık 1 iken
            kalan = self.akim_siniri - self.count * self.BOSTA_MA
            if led_ma * p > kalan:
                p = max(0.02, kalan / led_ma) if kalan > 0 else 0.02
        if abs(self.pixels.brightness - p) > 0.004:
            self.pixels.brightness = p
        self.pixels.show()

    def _enter_mode(self):
        """Statik modlar sadece moda girerken bir kez çizilir."""
        name = self.mode_name

        if name in self.STATIC_COLORS:
            self.pixels.fill(self.STATIC_COLORS[name])
            self._goster()

        elif name == "color":
            self.pixels.fill(self.color)
            self._goster()

        elif name == "random":
            self.randomize()

        else:
            self.pixels.fill((0, 0, 0))
            self._goster()

    # -----------------------------------------------------
    # Genel yardımcılar
    # -----------------------------------------------------

    def randomize(self):
        """Her LED'e rastgele renk verir."""
        for i in range(self.count):
            self.pixels[i] = (
                random.randint(0, 255),
                random.randint(0, 255),
                random.randint(0, 255),
            )
        self._goster()

    def off(self):
        self.pixels.fill((0, 0, 0))
        self._goster()

    # -----------------------------------------------------
    # Ana güncelleme (ana döngüde sürekli çağır)
    # -----------------------------------------------------

    def update(self):
        now = time.monotonic()
        if now - self._last_update < self.interval:
            return
        self._last_update = now

        name = self.mode_name
        if name in self.STATIC_MODES:
            return

        self._frame += 1

        if name == "breathe":
            self._breathe()
        elif name == "rainbow":
            self._rainbow()
        elif name == "wave":
            self._wave()
        elif name == "fire":
            self._fire()
        elif name == "comet":
            self._comet()
        elif name == "scanner":
            self._scanner()
        elif name == "chase":
            self._chase()
        elif name == "wipe":
            self._wipe()
        elif name == "sparkle":
            self._sparkle()
        elif name == "confetti":
            self._confetti()
        elif name == "colorcycle":
            self._colorcycle()
        elif name == "red_purple_white":
            self._flow()
        elif name == "police":
            self._police()
        elif name == "heartbeat":
            self._heartbeat()

        self._goster()

    # -----------------------------------------------------
    # Seçilen renkle çalışan efektler
    # -----------------------------------------------------

    def _breathe(self):
        # Yavaşça yanıp söner, hiç tam kararmaz
        level = (math.sin(self._frame / 25) + 1) / 2
        self.pixels.fill(_scale(self.color, 0.03 + 0.97 * level))

    def _comet(self):
        # Parlak baş şerit boyunca döner, arkasında sönen 6 LED'lik kuyruk
        tail = min(6, self.count)
        head = (self._frame // 2) % self.count
        for i in range(self.count):
            d = (head - i) % self.count
            self.pixels[i] = _scale(self.color, (1 - d / tail) ** 2) if d < tail else (0, 0, 0)

    def _chase(self):
        # Her üç LED'den biri yanar ve kayar, diğerleri çok hafif yanar
        step = self._frame // 6
        dim = _scale(self.color, 0.04)
        for i in range(self.count):
            self.pixels[i] = self.color if (i + step) % 3 == 0 else dim

    def _wipe(self):
        # Şerit baştan sona tek tek dolar, sonra aynı yönde boşalır
        pos = (self._frame // 4) % (2 * self.count)
        for i in range(self.count):
            if pos < self.count:
                lit = i <= pos
            else:
                lit = i > pos - self.count
            self.pixels[i] = self.color if lit else (0, 0, 0)

    def _sparkle(self):
        # Rastgele LED'ler parlayıp yavaşça söner
        lv = self._levels
        for i in range(self.count):
            lv[i] *= 0.88
        if random.random() < 0.3:
            lv[random.randint(0, self.count - 1)] = 1.0
        for i in range(self.count):
            self.pixels[i] = _scale(self.color, lv[i])

    def _scanner(self):
        # Tek nokta iki uç arasında gidip gelir, arkasında kısa iz (Kara Şimşek)
        lv = self._levels
        for i in range(self.count):
            lv[i] *= 0.6
        span = self.count - 1
        p = 0
        if span > 0:
            p = (self._frame // 3) % (2 * span)
            if p > span:
                p = 2 * span - p
        lv[p] = 1.0
        for i in range(self.count):
            self.pixels[i] = _scale(self.color, lv[i])

    def _heartbeat(self):
        # "Güm-güm" ve kısa bir dinlenme (yaklaşık 1,2 sn)
        t = self._frame % 60
        beat1 = math.exp(-((t - 4) / 2.5) ** 2)
        beat2 = 0.6 * math.exp(-((t - 16) / 2.5) ** 2)
        self.pixels.fill(_scale(self.color, 0.03 + 0.97 * max(beat1, beat2)))

    # -----------------------------------------------------
    # Kendi renklerini kullanan efektler
    # -----------------------------------------------------

    def _flow(self):
        # 3 renk şerit boyunca yumuşak geçişle akar
        pal = self.flow_colors
        n = len(pal)
        offset = self._frame * 0.02
        for i in range(self.count):
            pos = (i * n / self.count + offset) % n
            idx = int(pos)
            self.pixels[i] = _blend(pal[idx], pal[(idx + 1) % n], pos - idx)

    def _rainbow(self):
        for i in range(self.count):
            index = (i * 256 // self.count) + self._rainbow_offset
            self.pixels[i] = wheel(index)
        self._rainbow_offset = (self._rainbow_offset + 1) % 256

    def _wave(self):
        for i in range(self.count):
            wave = (math.sin((i + self._frame / 3) * 0.5) + 1) / 2
            self.pixels[i] = (0, int(255 * wave), int(255 * (1 - wave)))

    def _fire(self):
        for i in range(self.count):
            flicker = random.randint(-30, 30)
            self.pixels[i] = (
                _clamp(200 + flicker),
                _clamp(80 + flicker // 2),
                0,
            )

    def _confetti(self):
        # Rastgele renkli noktalar belirip yavaşça söner
        for i in range(self.count):
            self._buf[i] = _scale(self._buf[i], 0.9)
        if random.random() < 0.35:
            self._buf[random.randint(0, self.count - 1)] = wheel(random.randint(0, 255))
        for i in range(self.count):
            self.pixels[i] = self._buf[i]

    def _colorcycle(self):
        # Tüm şerit aynı renkte, renk çarkında yavaşça döner
        self.pixels.fill(wheel(self._frame // 2))

    def _police(self):
        # Sol yarı kırmızı, sağ yarı mavi; sırayla çift çakar
        t = self._frame % 40
        half = self.count // 2
        red_on = t < 20 and (t % 10) < 5
        blue_on = t >= 20 and (t % 10) < 5
        for i in range(self.count):
            if i < half:
                self.pixels[i] = (255, 0, 0) if red_on else (0, 0, 0)
            else:
                self.pixels[i] = (0, 0, 255) if blue_on else (0, 0, 0)
