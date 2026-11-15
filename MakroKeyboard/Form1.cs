using IWshRuntimeLibrary;   // COM referansı: Windows Script Host Object Model
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Ana pencere. Kod konulara göre birkaç dosyaya bölünmüştür (hepsi aynı Form1 sınıfı):
    //   Form1.cs               alanlar, açılış, ana arayüz
    //   Form1.Atama.cs         tuş seçimi, basış türü, makro, tuş adı, encoder, kısayol kutusu
    //   Form1.KomutMenusu.cs   komut seçme menüsü
    //   Form1.Baglanti.cs      seri bağlantı
    //   Form1.KartGuncelleme.cs kart yazılımı ve uygulama güncelleme
    //   Form1.Profiller.cs     ayarlar penceresi, profiller, geçmiş
    //   Form1.Esitleme.cs      ayarların klavyeyle taşınması, uygulama kısayolları
    //   Form1.TusIslemleri.cs  canlı izleme, sürükle-bırak, çakışma, geri al, sağ tık menüsü
    //   Form1.Istatistik.cs    istatistik ve ısı haritası
    //   Form1.Tepsi.cs         sistem tepsisi
    //   Form1.Yardimcilar.cs   açıklama balonu, kısayollar, yazdırılabilir liste, LED
    //   Form1.Rehber.cs        ilk açılış tanıtımı
    public partial class Form1 : Form, IAyarlarSahibi
    {
        // ---------- Win32 ----------
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr hWnd, int msg, int wParam, int lParam);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int WM_NCHITTEST = 0x84, HTCLIENT = 1;

        private readonly bool win11 = Environment.OSVersion.Version.Build >= 22000;
        private const int KoseCapi = 40;   // sadece Windows 10 için

        private Panel baslikCubugu;

        // ---------- Renkler ----------
        private static readonly Color Mavi = Color.FromArgb(16, 104, 232);
        private static readonly Color MaviKoyu = Color.FromArgb(10, 82, 190);
        private static readonly Color Lacivert = Tema.Yazi;
        private static readonly Color SolukYazi = Tema.SolukYazi;
        private static readonly Color KenarAcik = Tema.Kenar;
        private static readonly Color KutuDolgu = Tema.KartAcik;

        // Yerleşim (16 tuşlu uygulamadaki gibi): solda profiller, ortada klavye, sağda tuş ayarları
        private const int SolX = 20, SolG = 200;
        private const int OrtaX = 236, OrtaG = 669;
        private const int SagX = OrtaX + OrtaG + 16;
        private KlavyeGovdesi govde;

        // ---------- Donanım / komut bilgisi ----------
        // Index 0-7 = tuş 1-8, 8 = ampul tuşu (9), 9 = encoder'a basma
        private static readonly string[] Pinler =
            { "GP1", "GP2", "GP3", "GP4", "GP5", "GP6", "GP7", "GP8", "GP29", "GP28" };

        // Kart bağlanana kadar gösterilen varsayılanlar (code.py'deki DEFAULT_SETTINGS ile aynı)
        private static readonly string[] VarsayilanKomutlar =
        {
            "WINDOWS+D", "MEDIA_PREVIOUS", "MEDIA_PLAY_PAUSE", "MEDIA_NEXT", "ALT+TAB",
            "CTRL+C", "CTRL+V", "DISPLAY", "PIXEL_MODE", "MUTE"
        };

        // Açılır liste: {görünen ad, settings.txt komutu}. Listenin son satırı "Özel Kısayol".
        private static readonly string[,] Hazirlar =
        {
            { "Masaüstü",      "WINDOWS+D" },
            { "Kopyala",       "CTRL+C" },
            { "Yapıştır",      "CTRL+V" },
            { "Önceki Şarkı",  "MEDIA_PREVIOUS" },
            { "Oynat/Durdur",  "MEDIA_PLAY_PAUSE" },
            { "Sonraki Şarkı", "MEDIA_NEXT" },
            { "Alt + Tab",     "ALT+TAB" },
            { "Ekran Modu",    "DISPLAY" },
            { "LED Modu",      "PIXEL_MODE" },
            { "Sustur",        "MUTE" },
            { "Ses Artır",     "VOLUME_UP" },
            { "Ses Azalt",     "VOLUME_DOWN" },
            { "Kaydır Yukarı", "SCROLL_UP" },
            { "Kaydır Aşağı",  "SCROLL_DOWN" },
            { "Yakınlaştır",   "ZOOM_IN" },
            { "Uzaklaştır",    "ZOOM_OUT" },
            { "Sol Tık",       "MOUSE_LEFT" },
            { "Sağ Tık",       "MOUSE_RIGHT" },
            { "Orta Tık",      "MOUSE_MIDDLE" },

            // Hazır kısayollar (karta normal kısayol olarak gider)
            { "Kes",              "CTRL+X" },
            { "Geri Al",          "CTRL+Z" },
            { "Yinele",           "CTRL+Y" },
            { "Tümünü Seç",       "CTRL+A" },
            { "Dosyayı Kaydet",   "CTRL+S" },
            { "Bul",              "CTRL+F" },
            { "Ekran Görüntüsü",  "SHIFT+WINDOWS+S" },
            { "Görev Yöneticisi", "CTRL+SHIFT+ESC" },
            { "Dosya Gezgini",    "WINDOWS+E" },
            { "Sola Yasla",       "WINDOWS+LEFT" },
            { "Sağa Yasla",       "WINDOWS+RIGHT" },
            { "Pano Geçmişi",     "WINDOWS+V" },
            { "Ekranı Kilitle",   "WINDOWS+L" },
            { "Yeni Sekme",       "CTRL+T" },
            { "Sekmeyi Kapat",    "CTRL+W" },
            { "Kapanan Sekme",    "CTRL+SHIFT+T" },
            { "Sayfayı Yenile",   "F5" },

            // Özel klavye tuşları (kart yazılımı 2.2 ve sonrası)
            { "Durdur",              "MEDIA_STOP" },
            { "İleri Sar",           "MEDIA_FAST_FORWARD" },
            { "Geri Sar",            "MEDIA_REWIND" },
            { "Parlaklık Artır",     "BRIGHTNESS_UP" },
            { "Parlaklık Azalt",     "BRIGHTNESS_DOWN" },
            { "Hesap Makinesi",      "CALCULATOR" },
            { "Tarayıcı",            "BROWSER_HOME" },
            { "E-posta",             "EMAIL" },
            { "Bu Bilgisayar",       "MY_COMPUTER" },
            { "Uyku Engelleyici",    "NOSLEEP" },

            // Dizüstü klavyelerde çoğu zaman olmayan tuşlar
            { "Menü Tuşu",           "APPLICATION" },
            { "Pause",               "PAUSE" },
            { "Scroll Lock",         "SCROLL_LOCK" },
            { "Insert",              "INSERT" },
            { "Print Screen",        "PRINT_SCREEN" },
            { "Tuş Takımı =",        "KEYPAD_EQUALS" },

            // Windows'un tanıdığı ek medya kodları (kart 2.7 ve sonrası)
            { "Windows Arama",       "WINDOWS_SEARCH" },
            { "Tarayıcı Geri",       "BROWSER_BACK" },
            { "Tarayıcı İleri",      "BROWSER_FORWARD" },
            { "Tarayıcı Yenile",     "BROWSER_REFRESH" },
            { "Tarayıcı Durdur",     "BROWSER_STOP" },
            { "Sık Kullanılanlar",   "BROWSER_FAVORITES" },
            { "Medya Oynatıcı",      "MEDIA_PLAYER" },

            // Ek fonksiyon tuşları: gerçek klavyede yok, OBS / Discord / AutoHotkey'de kısayol olarak atanır
            { "F13", "F13" }, { "F14", "F14" }, { "F15", "F15" }, { "F16", "F16" },
            { "F17", "F17" }, { "F18", "F18" }, { "F19", "F19" }, { "F20", "F20" },
            { "F21", "F21" }, { "F22", "F22" }, { "F23", "F23" }, { "F24", "F24" },        // kart 2.3: fareyi arada bir kıpırdatır

            { "Boş",           "NONE" }
        };

        private const string OzelKisayolAdi = "Özel Kısayol";
        private const string MetinYazAdi = "Metin Yaz";
        private const string UygulamaAdi = "Uygulama, klasör, site";
        private const string MakroAdi = "Makro (çok adımlı)";

        // Her tuşun uygulama kısayolu SABİT ve önceden belirli (CTRL+ALT+harf).
        // Sıra tuş sırasıdır: BUTON 1 -> Y, BUTON 2 -> U, BUTON 3 -> I ...
        // Kullanıcı bunları değiştiremez. Harfleri değiştirmek istersen sadece bu diziyi düzenle.
        private static readonly string[] UygulamaTuslari =
            { "Y", "U", "I", "O", "P", "H", "J", "K", "L", "N" };

        // .lnk dosyaları burada durur: %APPDATA%\Microsoft\Windows\Start Menu\Programs\MakroKeyboard
        // (Windows, kısayol tuşlarını yalnızca Başlat menüsü ve Masaüstündeki kısayollar için çalıştırır)
        private static string UygulamaKlasoru
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs", "MakroKeyboard");
            }
        }
        private const string VarsayilanBilgi = "Değişiklikler settings.txt dosyasına kaydedilir.";

        // ---------- Veri ----------
        private class Atama
        {
            public string Numara, Kisayol, Pin;   // Kisayol = karttaki gerçek komut (CTRL+ALT+C, MUTE, TEXT:...)
            public Atama(string n, string k, string p)
            { Numara = n; Kisayol = k; Pin = p; }
        }

        private class Kutucuk
        {
            public YuvarlakPanel Panel;
            public Label Numara, Ad, Kisayol;
            public YuvarlakPanel Nokta;     // tuşa verilen renk (sol üstte küçük daire)
            public Label Simge;             // tuşa verilen simge (sağ üstte)
        }

        private readonly List<Atama> atamalar = new List<Atama>();
        private readonly List<Kutucuk> kutucuklar = new List<Kutucuk>();
        private int secili = 0;

        // ---------- Seri bağlantı ----------
        private readonly SeriBaglanti seri = new SeriBaglanti();
        private bool klavyeBagli;
        private bool islemde;          // seri işlem sürerken diğerleri bekler
        private bool yukleniyor;       // kod kontrolleri doldururken olayları sustur
        private readonly KlavyeKancasi kanca = new KlavyeKancasi();   // Alt+F4, Alt+Tab gibi sistem kısayollarını yakalar
        private Timer nabiz;

        // Kullanım istatistikleri (tuş başına sayaç; dakikada bir diske yazılır)
        private readonly Dictionary<string, int> istatistik = UygulamaAyarlari.IstatistikOku();
        private bool istatistikDegisti;
        private Timer istatistikZamanlayici;

        // Sistem tepsisi
        private NotifyIcon tepsi;
        private ContextMenuStrip tepsiMenu;
        private ToolStripMenuItem tepsiProfilMenu;
        private bool cikisIstendi, tepsiBilgisiGosterildi;

        // "--tepsi" ile başlatıldıysa (Windows açılışı) pencere gizli açılır ve kart aranır
        private static readonly bool tepsideBaslat =
            Environment.GetCommandLineArgs().Any(a => a == "--tepsi") && UygulamaAyarlari.Tepsi;
        private bool gizliBasla = tepsideBaslat;
        private bool tumPortlariDene = true;   // açılışta bir kez tüm portlar denenir (klavye takılıysa kendiliğinden bağlanır)

        // Kart takılınca kendiliğinden bağlanmak için bilinen seri portlar
        private HashSet<string> bilinenPortlar = new HashSet<string>(SerialPort.GetPortNames());

        // Yeni takılan ya da bağlantısı kopan portlar: kart açılırken (birkaç saniye)
        // PING'e cevap veremeyebilir, bu yüzden bu portlar süre dolana kadar her nabızda yeniden denenir
        private readonly Dictionary<string, DateTime> bekleyenPortlar = new Dictionary<string, DateTime>();
        private const int YenidenDenemeSuresi = 30;   // saniye

        // En son bağlanılan port (kopunca seri.PortAdi boşalır)
        private string sonPort = "";
        private ToolStripMenuItem tepsiBaglan;

        // Kartın tüm ayarları (BUTTON*, LED_* ...): kaydederken hepsi birlikte gönderilir
        private Dictionary<string, string> kartAyarlari = new Dictionary<string, string>();

        // Canlı tuş izleme: basılan tuşun kartı kısa süre yeşil yanar
        private int vurguIndex = -1;

        // Sürükle-bırak ile tuş yer değiştirme
        private int surukleKaynak = -1, surukleHedef = -1;
        private Point surukleBaslangic;
        private bool surukleBasildi, surukleniyor, tiklamaYut;

        // Geri al: her karta yazmadan önceki durum (profil metni olarak saklanır)
        private readonly List<KeyValuePair<string, string>> geriAlYigini = new List<KeyValuePair<string, string>>();
        private bool geriAliniyor;
        private const int GeriAlSiniri = 20;
        private YuvarlakPanel btnGeriAl;
        private Label lblGeriAl;

        // Sağ tık menüsü ile kopyalanan tuş
        private class TusPanosu
        {
            public string Kisa, Uzun, Cift, UygHedef, UygAd, Kaynak;
            public TusAdRenk Gorunum;   // tuşun adı ve rengi (yoksa null)
        }
        private TusPanosu pano;
        private int menuTusu = -1;

        // Tuşlara verilen ad ve renk (yalnızca uygulamada görünür, karta gitmez)
        private Dictionary<int, TusAdRenk> tusAdlari = UygulamaAyarlari.TusAdlariOku();

        // Isı haritası: tuş kartları kullanım sayısına göre renklenir
        private bool isiHaritasi = UygulamaAyarlari.IsiHaritasi;
        private YuvarlakPanel btnIsi;

        // Encoder hazır çevirme modları: {görünen ad, sağa, sola}
        private static readonly string[,] EncoderModlari =
        {
            { "Ses",                  "VOLUME_UP",          "VOLUME_DOWN" },
            { "Sayfa kaydırma",       "SCROLL_DOWN",        "SCROLL_UP" },
            { "Yakınlaştırma",        "ZOOM_IN",            "ZOOM_OUT" },
            { "Sekmeler arası",       "CTRL+TAB",           "CTRL+SHIFT+TAB" },
            { "Geri al / Yinele",     "CTRL+Y",             "CTRL+Z" },
            { "Video: kare kare",     "RIGHT",              "LEFT" },
            { "Şarkı değiştir",       "MEDIA_NEXT",         "MEDIA_PREVIOUS" },
            { "Sanal masaüstü",       "CTRL+WINDOWS+RIGHT", "CTRL+WINDOWS+LEFT" },
            { "Liste gezinme (ok)",   "DOWN",               "UP" },
            { "Ekran parlaklığı",     "BRIGHTNESS_UP",      "BRIGHTNESS_DOWN" }
        };
        private Label lblEncoderModu;   // sağ panelde "Çevirme: Ses ▾" (yalnızca encoder seçiliyken)

        // İlk açılış rehberi
        private YuvarlakPanel atamaKarti, komutKutusu, btnKaydet, btnLed;
        private Control lblAyarlarDugme;

        // Kısayolların Windows'ta (ve yaygın programlarda) zaten yaptığı iş. "!" ile başlayan: dikkat gerektirir.
        private static readonly Dictionary<string, string> WindowsKisayollari = new Dictionary<string, string>
        {
            { "ALT+F4", "!Açık pencereyi ya da programı kapatır" },
            { "CTRL+W", "!Çoğu programda sekmeyi ya da pencereyi kapatır" },
            { "CTRL+Q", "!Bazı programlardan tamamen çıkar" },
            { "WINDOWS+L", "!Ekranı kilitler" },
            { "CTRL+ALT+DELETE", "!Güvenlik ekranını açar (kilitle, oturumu kapat...)" },
            { "SHIFT+DELETE", "!Seçili dosyayı Geri Dönüşüm Kutusu'na atmadan kalıcı siler" },
            { "CTRL+WINDOWS+F4", "!Açık sanal masaüstünü kapatır" },
            { "CTRL+F4", "Belgeyi ya da sekmeyi kapatır" },
            { "CTRL+SHIFT+DELETE", "Tarayıcıda geçmiş temizleme penceresini açar" },
            { "WINDOWS+M", "Tüm pencereleri küçültür" },
            { "WINDOWS+D", "Masaüstünü gösterir" },
            { "WINDOWS+E", "Dosya Gezgini'ni açar" },
            { "WINDOWS+R", "Çalıştır penceresini açar" },
            { "WINDOWS+I", "Ayarlar'ı açar" },
            { "WINDOWS+A", "Hızlı ayarları açar" },
            { "WINDOWS+N", "Bildirimleri açar" },
            { "WINDOWS+S", "Aramayı açar" },
            { "WINDOWS+V", "Pano geçmişini açar" },
            { "WINDOWS+TAB", "Görev görünümünü açar" },
            { "WINDOWS+P", "Ekran yansıtma menüsünü açar" },
            { "WINDOWS+K", "Kablosuz ekran / cihaz bağlamayı açar" },
            { "WINDOWS+G", "Xbox Game Bar'ı açar" },
            { "WINDOWS+H", "Sesle yazmayı açar" },
            { "WINDOWS+X", "Hızlı bağlantı menüsünü açar" },
            { "WINDOWS+PERIOD", "Emoji panelini açar" },
            { "WINDOWS+UP", "Pencereyi büyütür" },
            { "WINDOWS+DOWN", "Pencereyi küçültür" },
            { "WINDOWS+LEFT", "Pencereyi sola yaslar" },
            { "WINDOWS+RIGHT", "Pencereyi sağa yaslar" },
            { "WINDOWS+PRINT_SCREEN", "Ekran görüntüsünü Resimler klasörüne kaydeder" },
            { "SHIFT+WINDOWS+S", "Ekran alıntısı aracını açar" },
            { "CTRL+WINDOWS+D", "Yeni sanal masaüstü açar" },
            { "CTRL+WINDOWS+LEFT", "Soldaki sanal masaüstüne geçer" },
            { "CTRL+WINDOWS+RIGHT", "Sağdaki sanal masaüstüne geçer" },
            { "CTRL+SHIFT+ESC", "Görev Yöneticisi'ni açar" },
            { "CTRL+ESC", "Başlat menüsünü açar" },
            { "ALT+TAB", "Pencereler arasında geçer" },
            { "ALT+ESC", "Pencereler arasında sırayla geçer" },
            { "ALT+SPACE", "Pencere menüsünü açar" },
            { "ALT+ENTER", "Seçilenin özelliklerini açar" },
            { "ALT+LEFT", "Geri gider (tarayıcı, Gezgin)" },
            { "ALT+RIGHT", "İleri gider (tarayıcı, Gezgin)" },
            { "CTRL+A", "Tümünü seçer" },
            { "CTRL+C", "Kopyalar" },
            { "CTRL+V", "Yapıştırır" },
            { "CTRL+X", "Keser" },
            { "CTRL+Z", "Geri alır" },
            { "CTRL+Y", "Yineler" },
            { "CTRL+S", "Kaydeder" },
            { "CTRL+F", "Bul / ara" },
            { "CTRL+P", "Yazdırma penceresini açar" },
            { "CTRL+N", "Yeni pencere ya da belge açar" },
            { "CTRL+O", "Dosya açar" },
            { "CTRL+T", "Tarayıcıda yeni sekme açar" },
            { "CTRL+R", "Sayfayı yeniler" },
            { "CTRL+D", "Tarayıcıda yer imi ekler (bazı programlarda siler)" },
            { "CTRL+SHIFT+T", "Tarayıcıda kapanan sekmeyi geri açar" },
            { "CTRL+SHIFT+N", "Gezgin'de yeni klasör, tarayıcıda gizli pencere açar" },
            { "CTRL+TAB", "Sonraki sekmeye geçer" },
            { "CTRL+SHIFT+TAB", "Önceki sekmeye geçer" },
            { "F1", "Yardımı açar" },
            { "F2", "Seçileni yeniden adlandırır" },
            { "F5", "Sayfayı / klasörü yeniler" },
            { "F11", "Tam ekrana geçer" },
            { "PRINT_SCREEN", "Ekran alıntısı / ekran görüntüsü alır" },
            { "ESC", "İptal eder ya da kapatır" },
            { "DELETE", "Seçileni siler" }
        };

        // Tuş kartlarında açıklama baloncuğu
        private readonly ToolTip baloncuk = new ToolTip { InitialDelay = 500, AutoPopDelay = 20000, ReshowDelay = 200 };

        // Başlık çubuğundaki profil seçici

        // Bağlantı bildirimi için önceki durum
        private bool sonBaglantiDurumu;
        private string sonIslem;   // son denenen yazma işleminin açıklaması (olay günlüğü için)
        private Timer vurguZamanlayici;

        // LED önizleme
        private string ledMod = "rainbow", ledRenk = "0066FF", ledAkis = LedAyarForm.VarsayilanAkis;
        private int ledParlaklik = 100, ledHiz = 100;
        private bool ledGonderiyor, ledTekrar;

        // ---------- Kontroller ----------
        private YuvarlakPanel hap, nokta, btnBaglan;
        private Label lblDurum, lblPort, lblBaglanYazi, lblBilgi, lblAdKalem;
        private Label lblTusAdi, lblTusPin;
        private ComboBox cmbUygulama;          // seçimi tutar (gizli); görünen kısım aşağıdaki menü
        private Label lblKomutSecim;
        private ToolStripDropDown komutMenusu;
        private DateTime komutMenusuKapandi = DateTime.MinValue;
        private TextBox txtKisayol;

        // Kısayol / Metin sekmeleri
        private Label lblIpucu;
        private Label lblKutuBaslik;    // kutunun içindeki küçük başlık: "YAZILACAK METİN", "KISAYOL" ...
        private bool metinModu;
        private bool yakalamaAcik;      // kısayol kutusu şu an tuş yakalıyor mu (yalnızca isteyince)
        private Label lblKlavyeIkon;    // kutudaki klavye simgesi (hazır komutlarda gizli)
        private YuvarlakPanel kisayolKutu;   // kısayol / metin / uygulama kutusu (sekmeler gizlenince yukarı kayar)
        private Label lblGozIkon;       // metin kutusunda: gizli metin aç / kapa
        private bool metinGizli;        // düzenlenen metin ●●● olarak gösterilsin mi
        private Label lblKaydetYazi;    // "AYARI KAYDET" / "• KAYDEDİLMEDİ"
        private string komutHam = "";   // karta gidecek kısayol (CTRL+ALT+C, MUTE...)
        private string metinHam = "";   // yazılacak metin (TEXT: öneki olmadan)
        private bool uygulamaModu;
        private bool makroModu;

        // Basış türü: 0 kısa, 1 uzun, 2 çift   (encoder'da: 0 basma, 1 sağa çevirme, 2 sola çevirme)
        private int basisTuru;
        private readonly YuvarlakPanel[] basisPaneller = new YuvarlakPanel[5];
        private readonly Label[] basisYazilar = new Label[5];
        private string bekleyenExe;     // seçilmiş ama henüz kaydedilmemiş hedef (.exe yolu ya da shell:AppsFolder\...)
        private string bekleyenAd;      // o hedefin görünen adı
        // kısayol tuşu (CTRL+ALT+Y) -> .lnk dosya yolu
        private readonly Dictionary<string, string> uygulamalar = new Dictionary<string, string>();
        // kısayol tuşu -> ne açtığı ("Uygulama", "Klasör", "Dosya", "Web sitesi")
        private readonly Dictionary<string, string> uygulamaTurleri = new Dictionary<string, string>();

        // =================================================================
        public Form1()
        {
            InitializeComponent();

            // Windows'un standart başlık çubuğunu kaldır; özel başlık çubuğunu kullan.
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(1336, 700);

            // Her zaman üstte + son konum (ekran değiştiyse ve pencere dışarıda kalıyorsa ortala)
            TopMost = UygulamaAyarlari.HerZamanUstte;
            Point? konum = UygulamaAyarlari.PencereKonumu;
            if (konum.HasValue && EkrandaGorunur(new Rectangle(konum.Value, Size)))
            {
                StartPosition = FormStartPosition.Manual;
                Location = konum.Value;
            }

            for (int i = 0; i < Pinler.Length; i++)
                atamalar.Add(new Atama(i < 9 ? (i + 1).ToString() : "⟳", VarsayilanKomutlar[i], Pinler[i]));

            BaslikCubuguOlustur();
            ArayuzOlustur();
            SayfalariOlustur();

            // Pencere büyütülüp küçültülebilir; son boyut hatırlanır
            MinimumSize = EnKucukBoyut;
            Size kayitli = UygulamaAyarlari.PencereBoyutu;
            Rectangle ekran = Screen.FromPoint(konum ?? Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Max(EnKucukBoyut.Width, Math.Min(ekran.Width, kayitli.Width)),
                                  Math.Max(EnKucukBoyut.Height, Math.Min(ekran.Height, kayitli.Height)));

            try { Icon = Properties.Resources.MakroKlavye; } catch (Exception) { }

            // İlk açılışta kısa tanıtım (tepside gizli başladıysa pencere ilk açıldığında)
            Shown += async (s, e) =>
            {
                if (UygulamaAyarlari.PencereTamEkran && !tamEkran) TamEkranDegistir();

                // Açılınca klavyeye kendiliğinden bağlan (önce bilinen port denenir, hızlıdır)
                if (!klavyeBagli && !islemde && tumPortlariDene)
                {
                    tumPortlariDene = false;
                    Task baglan = BaglanVeOku(null, true);
                }

                if (!UygulamaAyarlari.RehberGoruldu)
                {
                    BeginInvoke(new Action(RehberBaslat));
                    return;
                }

                if (YenilikleriGoster()) return;

                await Task.Delay(2500);   // pencere ve bağlantı otursun
                await UygulamaGuncellemesiniSor();
            };
        }

        // Başlık çubuğunun yeterli kısmı bir ekranda görünüyor mu?
        private static bool EkrandaGorunur(Rectangle pencere)
        {
            Rectangle baslik = new Rectangle(pencere.X, pencere.Y, pencere.Width, 40);
            return Screen.AllScreens.Any(s =>
            {
                Rectangle kesisim = Rectangle.Intersect(s.WorkingArea, baslik);
                return kesisim.Width >= 150 && kesisim.Height >= 20;
            });
        }

        private void KonumuKaydet()
        {
            if (!Visible || WindowState != FormWindowState.Normal) return;
            try
            {
                Rectangle r = tamEkran ? normalSinir : Bounds;
                UygulamaAyarlari.PencereKonumu = r.Location;
                UygulamaAyarlari.PencereBoyutu = r.Size;
                UygulamaAyarlari.PencereTamEkran = tamEkran;
            }
            catch (Exception) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            KonumuKaydet();

            // Kart güncellenirken kapanırsa karttaki dosyalar yarım kalabilir
            if (UygulamaGuncelleme.KartGuncelleniyor && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Kart yazılımı güncelleniyor. Bitmesini bekle (yarım dakika kadar), sonra kapat.",
                                "Makro Klavye", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Tepsi açıksa kapat düğmesi pencereyi gizler, uygulama çalışmaya devam eder
            bool kullaniciKapatti = e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None;

            if (kullaniciKapatti && !cikisIstendi && Visible && DegisiklikVar() &&
                MessageBox.Show(this, TusEtiketi(secili) + " (" + BasisAdi() + ") için yaptığın değişiklik kaydedilmedi.\n\n" +
                                "Yine de kapatılsın mı?", "Kaydedilmemiş değişiklik",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            if (!cikisIstendi && kullaniciKapatti && UygulamaAyarlari.Tepsi)
            {
                e.Cancel = true;
                Hide();

                if (!tepsiBilgisiGosterildi)
                {
                    tepsiBilgisiGosterildi = true;
                    tepsi.ShowBalloonTip(3000, "Makro Klavye",
                        "Arka planda çalışıyor. Açmak için simgeye çift tıkla.", ToolTipIcon.Info);
                }
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            nabiz.Stop();
            vurguZamanlayici.Stop();
            istatistikZamanlayici.Stop();
            IstatistikKaydet();
            kanca.Dispose();
            seri.Dispose();

            if (tepsi != null)
            {
                tepsi.Visible = false;
                tepsi.Dispose();
            }

            base.OnFormClosed(e);
        }

        // "--tepsi" ile başlatılınca pencere hiç görünmeden tepside açılır
        protected override void SetVisibleCore(bool value)
        {
            if (gizliBasla)
            {
                gizliBasla = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }

            base.SetVisibleCore(value);
        }

        // Uygulama ikinci kez açılınca (Program.cs) bu kopyanın penceresi öne gelir
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.GosterMesaji && Program.GosterMesaji != 0)
            {
                PencereyiGoster();
                return;
            }
            if (m.Msg == WM_NCHITTEST && !tamEkran && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt64() == HTCLIENT) m.Result = (IntPtr)KenarBul(m.LParam);
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- Köşe yuvarlama ----------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (win11)
            {
                int tercih = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                                      ref tercih, sizeof(int));
            }
        }

        private GraphicsPath RoundedPath(Rectangle r, int cap)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, cap, cap, 180, 90);
            p.AddArc(r.Right - cap, r.Y, cap, cap, 270, 90);
            p.AddArc(r.Right - cap, r.Bottom - cap, cap, cap, 0, 90);
            p.AddArc(r.X, r.Bottom - cap, cap, cap, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (!win11 && Width > 0 && Height > 0)
            {
                if (tamEkran) Region = null;
                else
                {
                    using (var path = RoundedPath(new Rectangle(0, 0, Width, Height), KoseCapi))
                        Region = new Region(path);
                }
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (!win11 && !tamEkran)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = RoundedPath(r, KoseCapi))
                using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2))
                    e.Graphics.DrawPath(pen, path);
            }
        }

        // ---------- Zemin: açık maviden hafif koyuya gradyan ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientRectangle.Width < 1 || ClientRectangle.Height < 1) return;
            using (var fr = new LinearGradientBrush(ClientRectangle,
                Tema.Zemin1, Tema.Zemin2, 50f))
                e.Graphics.FillRectangle(fr, ClientRectangle);
        }

        // ---------- Sürükleme ----------
        private void SurukleBaslat(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Control tiklanan = sender as Control;
            if (e.Clicks == 2 && tiklanan != null && (tiklanan == baslikCubugu || tiklanan.Parent == baslikCubugu))   // başlığa çift tık: büyüt / eski boyut
            {
                TamEkranDegistir();
                return;
            }
            if (!tamEkran)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        // ---------- Başlık çubuğu ----------
        private void BaslikCubuguOlustur()
        {
            baslikCubugu = new Panel
            {
                Dock = DockStyle.Top,
                Width = ClientSize.Width,
                Height = 40,
                BackColor = Tema.Baslik
            };

            PictureBox ikon = new PictureBox
            {
                Image = BaslikSimgesi(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(22, 22),
                Location = new Point(14, 9),
                BackColor = Color.Transparent
            };

            Label baslik = new Label
            {
                Text = "Makro Klavye",
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(44, 11),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            Label surum = new Label
            {
                Text = UygulamaGuncelleme.Surum,
                ForeColor = Color.FromArgb(150, 170, 195),
                AutoSize = true,
                Location = new Point(140, 13),
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent
            };
            baslik.SizeChanged += (s, e) => surum.Left = baslik.Right + 2;
            baslikCubugu.Controls.Add(surum);

            Label btnKapat = BaslikButonu("✕", Color.FromArgb(232, 17, 35));
            btnKapat.Click += (s, e) => Close();

            Label btnKucult = BaslikButonu("–", Color.FromArgb(70, 70, 70));
            btnKucult.Click += (s, e) => WindowState = FormWindowState.Minimized;

            // Büyüt / eski boyut (başlığa çift tıklamak da aynısı)
            btnBuyut = BaslikButonu("\uE922", Color.FromArgb(70, 70, 70));
            btnBuyut.Font = new Font("Segoe MDL2 Assets", 9f);
            btnBuyut.Click += (s, e) => TamEkranDegistir();
            baloncuk.SetToolTip(btnBuyut, "Büyüt / küçült (başlığa çift tıkla)");

            // Konumları formun değil, başlık panelinin genişliğine göre hesapla.
            btnKapat.Location = new Point(baslikCubugu.ClientSize.Width - btnKapat.Width - 5, 5);
            btnKapat.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBuyut.Location = new Point(btnKapat.Left - btnBuyut.Width - 5, 5);
            btnBuyut.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnKucult.Location = new Point(btnBuyut.Left - btnKucult.Width - 5, 5);
            btnKucult.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            baslikCubugu.Controls.Add(btnBuyut);

            // Sayfa sekmeleri: Tuşlar / Işık / Ayarlar (16 tuşlu uygulamadaki gibi)
            SekmeleriOlustur(baslikCubugu);

            baslikCubugu.Controls.Add(ikon);
            baslikCubugu.Controls.Add(baslik);
            baslikCubugu.Controls.Add(btnKucult);
            baslikCubugu.Controls.Add(btnKapat);

            baslikCubugu.MouseDown += SurukleBaslat;
            ikon.MouseDown += SurukleBaslat;
            baslik.MouseDown += SurukleBaslat;
            surum.MouseDown += SurukleBaslat;

            Controls.Add(baslikCubugu);
            baslikCubugu.BringToFront();

            MouseDown += SurukleBaslat;
        }

        private Bitmap BaslikSimgesi()
        {
            try { return new Icon(Properties.Resources.MakroKlavye, new Size(32, 32)).ToBitmap(); }
            catch (Exception) { return KlavyeIkonuOlustur(); }
        }

        // Uygulama simgesinin küçük boyu (yoksa eski çizim)
        private Icon TepsiSimgesi()
        {
            try { return new Icon(Properties.Resources.MakroKlavye, SystemInformation.SmallIconSize); }
            catch (Exception) { return Icon.FromHandle(KlavyeIkonuOlustur().GetHicon()); }
        }

        private Bitmap KlavyeIkonuOlustur()
        {
            Bitmap bmp = new Bitmap(64, 64);

            using (Graphics g = Graphics.FromImage(bmp))
            using (SolidBrush mavi = new SolidBrush(Color.FromArgb(41, 128, 235)))
            using (SolidBrush beyaz = new SolidBrush(Color.White))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (GraphicsPath zemin =
                    YuvarlakDikdortgen(new Rectangle(0, 0, 64, 64), 14))
                {
                    g.FillPath(mavi, zemin);
                }

                for (int satir = 0; satir < 2; satir++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Rectangle tus = new Rectangle(10 + i * 12, 17 + satir * 12, 8, 8);
                        using (GraphicsPath yol = YuvarlakDikdortgen(tus, 2))
                            g.FillPath(beyaz, yol);
                    }
                }

                using (GraphicsPath bosluk =
                    YuvarlakDikdortgen(new Rectangle(18, 41, 28, 8), 2))
                {
                    g.FillPath(beyaz, bosluk);
                }
            }

            return bmp;
        }

        private GraphicsPath YuvarlakDikdortgen(Rectangle r, int yaricap)
        {
            int d = yaricap * 2;
            GraphicsPath yol = new GraphicsPath();

            yol.AddArc(r.X, r.Y, d, d, 180, 90);
            yol.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            yol.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            yol.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            yol.CloseFigure();

            return yol;
        }

        private Label BaslikButonu(string yazi, Color hoverRengi)
        {
            Label l = new Label
            {
                Text = yazi,
                Size = new Size(36, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11f),
                Cursor = Cursors.Hand
            };
            l.MouseEnter += (s, e) => l.BackColor = hoverRengi;
            l.MouseLeave += (s, e) => l.BackColor = Color.Transparent;
            return l;
        }

        // =================================================================
        //  ANA ARAYÜZ
        // =================================================================
        private static Font Ikon(float boyut)
        {
            return new Font("Segoe MDL2 Assets", boyut);
        }

        private Label Etiket(Control ebeveyn, string yazi, Font font, Color renk,
                             int x, int y, int w = 0, int h = 0,
                             ContentAlignment hiza = ContentAlignment.TopLeft)
        {
            var l = new Label
            {
                Text = yazi,
                Font = font,
                ForeColor = renk,
                BackColor = Color.Transparent,
                TextAlign = hiza,
                Left = x,
                Top = y
            };
            if (w > 0) { l.AutoSize = false; l.Size = new Size(w, h); }
            else l.AutoSize = true;
            ebeveyn.Controls.Add(l);
            return l;
        }

        private void TiklaBagla(Control c, EventHandler h)
        {
            c.Click += h;
            foreach (Control alt in c.Controls) TiklaBagla(alt, h);
        }

        private void HoverBagla(YuvarlakPanel p)
        {
            Action guncelle = () =>
                p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));

            EventHandler gir = (s, e) => p.Hover = true;
            EventHandler cik = (s, e) => guncelle();

            Action<Control> bagla = null;
            bagla = c =>
            {
                c.MouseEnter += gir;
                c.MouseLeave += cik;
                c.Cursor = Cursors.Hand;
                foreach (Control alt in c.Controls) bagla(alt);
            };
            bagla(p);
        }

        private void ArayuzOlustur()
        {
            DoubleBuffered = true;
            seri.BilinenPortlar = UygulamaAyarlari.BilinenPortlar();   // önce bunlar denenir

            // Tasarımcıdaki kontroller: klavye artık çizilerek gösteriliyor (KlavyeGovdesi)
            panel1.Visible = false;
            pictureBox1.Visible = false;

            DurumSatiriOlustur();
            ProfilPaneliOlustur();
            AtamaKartiOlustur();
            AyarPaneliOlustur();

            // Kısayol kutusuna odaklanınca tuşlar sisteme gitmeden yakalanır
            kanca.Etkin = () => txtKisayol.Focused && yakalamaAcik && !metinModu && !uygulamaModu && !makroModu;
            kanca.TusBasildi += (ctrl, shift, alt, win, tus) =>
                BeginInvoke(new Action(() => KisayolYakala(ctrl, shift, alt, win, tus)));
            Deactivate += (s, e) => kanca.Durdur();

            // Karttan gelen canlı olaylar (arka plan iş parçacığından arayüze aktarılır)
            seri.TusBasildi += (n, tur) => UiyeAt(() =>
            {
                IstatistikArtir("BUTTON" + n + "_" + (tur ?? "SHORT"));
                IsiYazisiGuncelle(n);
                TusVurgula(n, true, tur);
            });
            seri.EncoderDondu += yon => UiyeAt(() =>
            {
                IstatistikArtir(yon > 0 ? "ENC_CW" : "ENC_CCW");
                IsiYazisiGuncelle(atamalar.Count - 1);
                TusVurgula(atamalar.Count - 1, false, null);
            });
            // LED tuşuyla değişen mod: sonraki kayıtta eski mod geri yazılmasın
            seri.LedModDegisti += mod => UiyeAt(() =>
            {
                kartAyarlari["LED_MODE"] = mod;
                ledMod = mod;
                AktifProfileLedYaz();
                GovdeIsigiGuncelle();
                IsikDisaridanDegisti();
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi,
                                            "Klavyedeki LED tuşuyla efekt değişti: " + LedAyarForm.ModAdi(mod));
            });
            seri.UykuDegisti += acik => UiyeAt(() =>
            {
                kartAyarlari["UYKU_ENGEL"] = acik ? "1" : "0";
                string m = "Uyku engelleyici " + (acik ? "açıldı: bilgisayar uykuya geçmeyecek." : "kapandı.");
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi, "Klavyedeki tuşla " + m);
                Bildir(m);
            });
            seri.Koptu += () => UiyeAt(() =>
            {
                DurumGuncelle(false);
                Bildir("Klavye bağlantısı koptu. Tekrar takınca kendiliğinden bağlanır.");
            });

            vurguZamanlayici = new Timer { Interval = 350 };
            vurguZamanlayici.Tick += (s, e) =>
            {
                vurguZamanlayici.Stop();
                vurguIndex = -1;
                KutucukStilleri();
            };

            UygulamalariTara();
            KutuculariYenile();
            SecimYap(0);
            GeriAlGorunum();

            // Bağlıyken kartın hâlâ orada olup olmadığını 3 saniyede bir yokla
            nabiz = new Timer { Interval = 3000 };
            nabiz.Tick += async (s, e) =>
            {
                if (islemde) return;

                if (!klavyeBagli)
                {
                    await YeniPortKontrol();
                    return;
                }

                PortListesiniGuncelle();

                islemde = true;
                try
                {
                    bool tamam = await Task.Run(() => seri.Ping());
                    if (!tamam)
                    {
                        DurumGuncelle(false);
                        Bildir("Klavye bağlantısı koptu. Tekrar takınca kendiliğinden bağlanır.");
                    }
                }
                finally { islemde = false; }
            };
            nabiz.Start();

            istatistikZamanlayici = new Timer { Interval = 60000 };
            istatistikZamanlayici.Tick += (s, e) => IstatistikKaydet();
            istatistikZamanlayici.Start();

            TepsiOlustur();
        }

        // ---------- Üst durum satırı + BAĞLAN düğmesi ----------
        private void DurumSatiriOlustur()
        {
            hap = new YuvarlakPanel
            {
                Location = new Point(OrtaX, 58),
                Size = new Size(190, 40),
                Yaricap = 20
            };
            nokta = new YuvarlakPanel
            {
                Location = new Point(17, 14),
                Size = new Size(13, 13),
                Yaricap = 7
            };
            hap.Controls.Add(nokta);
            lblDurum = Etiket(hap, "", new Font("Segoe UI", 9.5f, FontStyle.Bold),
                              Lacivert, 40, 11);
            Controls.Add(hap);

            // Bağlı klavyenin adı + kalem: tıklayınca ad değişir (port bilgisi ipucunda)
            Etiket(this, "\uE765", Ikon(15f), Lacivert, OrtaX + 214, 65);
            lblPort = Etiket(this, "", new Font("Segoe UI", 10f, FontStyle.Bold), Lacivert, OrtaX + 244, 66);
            lblPort.AutoSize = true;
            lblPort.MaximumSize = new Size(180, 22);
            lblPort.AutoEllipsis = true;
            lblPort.Cursor = Cursors.Hand;
            lblPort.Click += (s, e) => KlavyeyeAdVer();
            lblPort.SizeChanged += (s, e) => { if (lblAdKalem != null) lblAdKalem.Left = lblPort.Right + 4; };

            lblAdKalem = Etiket(this, "\uE70F", Ikon(11f), Mavi, OrtaX + 244, 69);
            lblAdKalem.Cursor = Cursors.Hand;
            lblAdKalem.Click += (s, e) => KlavyeyeAdVer();
            baloncuk.SetToolTip(lblAdKalem, "Klavyeye ad ver");

            btnBaglan = new YuvarlakPanel
            {
                Location = new Point(OrtaX + OrtaG - 160, 58),
                Size = new Size(160, 40),
                Yaricap = 20,
                DolguRengi = Mavi,
                HoverRengi = MaviKoyu
            };
            lblBaglanYazi = Etiket(btnBaglan, "BAĞLAN", new Font("Segoe UI", 9.5f, FontStyle.Bold),
                                   Color.White, 0, 0, 160, 40, ContentAlignment.MiddleCenter);
            HoverBagla(btnBaglan);
            TiklaBagla(btnBaglan, (s, e) => BaglanTikla());
            Controls.Add(btnBaglan);
            pencereSagi.Add(btnBaglan);

            DurumGuncelle(false);
        }

        // Üstteki yazı: bağlı klavyenin adı (kalemle değişir); port ipucunda görünür
        private void KlavyeEtiketiGuncelle(bool bagli)
        {
            lblPort.Text = bagli ? UygulamaAyarlari.KlavyeAdi(UygulamaAyarlari.AktifKlavye) : "Klavye bağlı değil";
            lblPort.ForeColor = bagli ? Lacivert : SolukYazi;
            lblAdKalem.Visible = bagli;
            lblAdKalem.Left = lblPort.Right + 4;

            string ipucu = bagli ? "Seri port: " + seri.PortAdi + "  ·  Ad vermek için tıkla" : "Klavye bağlı değil";
            baloncuk.SetToolTip(lblPort, ipucu);
        }

        private void KlavyeyeAdVer()
        {
            string kimlik = UygulamaAyarlari.AktifKlavye;
            if (!klavyeBagli)
            {
                Bildir("Ad vermek için önce klavyeye bağlan.");
                return;
            }
            if (kimlik.Length == 0)
            {
                Bildir("Klavyeye ad vermek için kart yazılımı 2.4 gerekir (Ayarlar > Kart'tan güncelle).");
                return;
            }

            string ad = GirisKutusu.Sor(this, "Klavyeye ad ver",
                "Birden çok klavye kullanıyorsan hangisinin bağlı olduğu bu adla görünür. " +
                "Her klavyenin geçmişi ve etkin profili ayrı tutulur.",
                "Örn. Ofis klavyesi", UygulamaAyarlari.KlavyeAdi(kimlik),
                d => d.Length == 0 ? "Bir ad yaz." : d.Length > 40 ? "En fazla 40 harf." : null);
            if (ad == null) return;

            UygulamaAyarlari.KlavyeAdiYaz(kimlik, ad);
            KlavyeEtiketiGuncelle(true);
            Bildir("Klavyenin adı: " + ad);
        }

        private void DurumGuncelle(bool bagli)
        {
            klavyeBagli = bagli;

            hap.DolguRengi = bagli ? Tema.OkDolgu : Tema.HataDolgu;
            hap.KenarRengi = bagli ? Tema.OkKenar : Tema.HataKenar;
            nokta.DolguRengi = bagli ? Color.FromArgb(34, 197, 94) : Color.FromArgb(220, 60, 60);

            lblDurum.Text = bagli ? "KLAVYE BAĞLI" : "BAĞLI DEĞİL";
            lblDurum.ForeColor = bagli ? Tema.OkYazi : Tema.HataYazi;
            KlavyeEtiketiGuncelle(bagli);
            lblBaglanYazi.Text = bagli ? "YENİDEN OKU" : "BAĞLAN";
            GovdeIsigiGuncelle();
            IsikBilgisi();

            hap.Invalidate(true);

            if (bagli && seri.PortAdi.Length > 0)
            {
                sonPort = seri.PortAdi;
                // Bir sonraki bağlanışta önce bu port denensin (hızlı bağlanma)
                UygulamaAyarlari.PortuHatirla(sonPort);
                seri.BilinenPortlar = UygulamaAyarlari.BilinenPortlar();
            }

            // Tepsi simgesinin üstüne gelince durum görünsün (bildirimler kapalı olsa bile)
            if (tepsi != null)
                tepsi.Text = bagli ? "Makro Klavye - bağlı (" + sonPort + ")" : "Makro Klavye - bağlı değil";

            if (bagli == sonBaglantiDurumu) return;
            sonBaglantiDurumu = bagli;

            UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBaglanti,
                                        bagli ? "Klavye bağlandı (" + sonPort + ")" : "Klavye bağlantısı koptu");

            // Bağlantı koptu: kart çıkarıp takılınca port adı çoğu zaman aynı kalır (COM5 -> COM5).
            // Bu port bir süre her nabızda yeniden denensin.
            if (!bagli && sonPort.Length > 0)
                bekleyenPortlar[sonPort] = DateTime.Now.AddSeconds(YenidenDenemeSuresi);

            // Tepsideyken takıldı / çıkarıldı bildirimi
            if (tepsi != null && tepsi.Visible && UygulamaAyarlari.BaglantiBildirimi &&
                (!Visible || WindowState == FormWindowState.Minimized))
            {
                tepsi.ShowBalloonTip(3000, "Makro Klavye",
                    bagli ? "Klavye bağlandı (" + sonPort + ")." : "Klavye bağlantısı koptu.",
                    bagli ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
        }

        // ---------- Sol alt: TUŞ ATAMALARI kartı ----------
        private void AtamaKartiOlustur()
        {
            var kart = atamaKarti = new YuvarlakPanel
            {
                Location = new Point(OrtaX, 110),
                Size = new Size(OrtaG, 570),
                Yaricap = 18,
                DolguRengi = Tema.Panel,
                KenarRengi = Tema.PanelKenar
            };
            Etiket(kart, "TUŞ ATAMALARI", new Font("Segoe UI", 10.5f, FontStyle.Bold),
                   Lacivert, 22, 18);
            Label surukle = Etiket(kart, "Sürükle · Sağ tık: menü", new Font("Segoe UI", 8.5f),
                   SolukYazi, 199, 20, 154, 18, ContentAlignment.MiddleRight);

            btnIsi = KucukDugme(kart, "\uE9D2", "Isı haritası", 361, 13, 112, () => IsiHaritasiDegistir());
            BaloncukAyarla(btnIsi, "Tuşları ne kadar kullandığına göre renklendir");
            IsiDugmesiGorunum();

            YuvarlakPanel btnListe = KucukDugme(kart, "", "Liste", 479, 13, 86, () => ListeMenusuAc());
            baloncuk.SetToolTip(btnListe, "Tuş listesini resim olarak kaydet ya da yazdır");
            foreach (Control c in btnListe.Controls) baloncuk.SetToolTip(c, "Tuş listesini resim olarak kaydet ya da yazdır");

            btnGeriAl = KucukDugme(kart, "", "Geri al", 571, 13, 86, () => GeriAl());
            lblGeriAl = (Label)btnGeriAl.Controls[btnGeriAl.Controls.Count - 1];

            // Klavyenin çizimi: tuş kutucukları gerçek yerlerine (1-4 üstte, 5-8 altta, sağda encoder ve 9) konur
            govde = new KlavyeGovdesi
            {
                Location = new Point((OrtaG - KlavyeGovdesi.Boyut.Width) / 2, 56)
            };
            kart.Controls.Add(govde);

            for (int i = 0; i < atamalar.Count; i++)
            {
                var a = atamalar[i];
                Rectangle alan = KlavyeGovdesi.TusAlani(i);
                int genislik = alan.Width, yukseklik = alan.Height;
                bool encoder = (i == atamalar.Count - 1);

                var tile = new YuvarlakPanel
                {
                    Location = alan.Location,
                    Size = alan.Size,
                    Yaricap = encoder ? genislik / 2 : 12
                };

                Font numFont = encoder ? new Font("Segoe UI Symbol", 15f)
                                       : new Font("Segoe UI", 13f, FontStyle.Bold);

                int ust = (yukseklik - 96) / 2 + (encoder ? 6 : 2);   // yazılar kutucuğun ortasında
                int yan = encoder ? 10 : 4;                           // yuvarlak encoder'da kenarlara taşmasın
                var num = Etiket(tile, a.Numara, numFont, Lacivert, 0, ust + 6, genislik, 28,
                                 ContentAlignment.MiddleCenter);
                var ad = Etiket(tile, "", new Font("Segoe UI", 9f, FontStyle.Bold), Lacivert,
                                yan, ust + 38, genislik - 2 * yan, 20, ContentAlignment.MiddleCenter);
                var ks = Etiket(tile, "", new Font("Segoe UI", 8f), SolukYazi,
                                yan, ust + 58, genislik - 2 * yan, 20, ContentAlignment.MiddleCenter);
                ad.AutoEllipsis = true;
                ks.AutoEllipsis = true;

                var nokta = new YuvarlakPanel
                {
                    Location = encoder ? new Point(20, 20) : new Point(10, 10),
                    Size = new Size(12, 12),
                    Yaricap = 5,
                    Visible = false
                };
                tile.Controls.Add(nokta);

                var simge = Etiket(tile, "", Ikon(12f), Lacivert, genislik - (encoder ? 40 : 32), encoder ? 14 : 6, 26, 26, ContentAlignment.MiddleCenter);
                simge.Visible = false;

                // Numara etiketi kartın tüm genişliğini kaplıyor: nokta ve simge onun önünde dursun
                nokta.BringToFront();
                simge.BringToFront();

                kutucuklar.Add(new Kutucuk { Panel = tile, Numara = num, Ad = ad, Kisayol = ks, Nokta = nokta, Simge = simge });

                int indeks = i;
                TiklaBagla(tile, (s, e) =>
                {
                    if (tiklamaYut) { tiklamaYut = false; return; }   // sürükleme / sağ tık, seçim yapma
                    KullaniciTusSec(indeks);
                });
                HoverBagla(tile);
                SurukleBagla(tile, indeks);
                TusMenusuBagla(tile, indeks);

                govde.Controls.Add(tile);
            }

            OzetOlustur(kart, govde.Bottom + 14);

            foreach (Control c in new Control[] { surukle, btnIsi, btnListe, btnGeriAl })
                c.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            kart.Resize += (s, e) => KlavyeyiOrtala();
            Controls.Add(kart);
            pencereDolu.Add(kart);
        }

        // ---------- Klavyenin altında: seçili tuşun bütün basışları ----------
        //  Solda basışlar (kısa, uzun, çift), sağda çevirmeler (encoder'da sağa / sola,
        //  ve tuş / encoder basılıyken sağa / sola). Satıra tıklayınca o basış düzenlenir.
        private Label lblOzetBaslik;
        private readonly YuvarlakPanel[] ozetSatirlari = new YuvarlakPanel[7];
        private readonly Label[] ozetBasis = new Label[7], ozetKomut = new Label[7];
        private int ozetY;

        private void OzetOlustur(Control kart, int y)
        {
            ozetY = y;
            int x = (OrtaG - KlavyeGovdesi.Boyut.Width) / 2, w = KlavyeGovdesi.Boyut.Width;
            lblOzetBaslik = Etiket(kart, "", new Font("Segoe UI", 9f, FontStyle.Bold), SolukYazi, x + 4, y, w - 8, 20);

            int satirG = (w - 10) / 2;
            for (int t = 0; t < ozetSatirlari.Length; t++)
            {
                int basis = t;
                var satir = ozetSatirlari[t] = new YuvarlakPanel
                {
                    Location = new Point(x, y + 24),
                    Size = new Size(satirG, 33),
                    Yaricap = 9,
                    DolguRengi = KutuDolgu,
                    HoverRengi = Tema.Hover
                };
                ozetBasis[t] = Etiket(satir, "", new Font("Segoe UI", 8.5f), SolukYazi, 12, 0, 118, 33, ContentAlignment.MiddleLeft);
                ozetKomut[t] = Etiket(satir, "", new Font("Segoe UI", 9f, FontStyle.Bold), Lacivert, 130, 0, satirG - 138, 33, ContentAlignment.MiddleLeft);
                ozetKomut[t].AutoEllipsis = true;
                HoverBagla(satir);
                TiklaBagla(satir, (s, e) => { if (Array.IndexOf(GecerliBasislar, basis) >= 0) KullaniciBasisSec(basis); });
                kart.Controls.Add(satir);
            }
        }

        // Seçili tuşun bütün basışları ne yapıyor
        private void OzetGuncelle()
        {
            if (lblOzetBaslik == null || secili < 0 || secili >= atamalar.Count) return;

            bool enc = EncoderSecili;
            string[] adlar = enc
                ? new[] { "Basma", "Uzun basma", "Çift basma", "Sağa çevir", "Sola çevir", "Basılı + sağa", "Basılı + sola" }
                : new[] { "Kısa basış", "Uzun basış", "Çift basış", "", "", "Tut + sağa çevir", "Tut + sola çevir" };
            string[] anahtarlar =
            {
                null, "BUTTON" + secili + "_LONG", "BUTTON" + secili + "_DOUBLE", "ENCODER_CW", "ENCODER_CCW",
                "BUTTON" + secili + "_CW", "BUTTON" + secili + "_CCW"
            };
            int[] gecerli = GecerliBasislar;

            lblOzetBaslik.Text = (enc ? "ENCODER" : "BUTON " + (secili + 1)) + "  ·  BÜTÜN BASIŞLAR  (tıkla: o basışı düzenle)";

            // sol sütun: 0-2, sağ sütun: kalanlar sırayla
            int x = govde != null ? govde.Left : ozetSatirlari[0].Left;
            int satirG = ozetSatirlari[0].Width;
            int sol = 0, sag = 0;

            for (int t = 0; t < ozetSatirlari.Length; t++)
            {
                bool gorunur = Array.IndexOf(gecerli, t) >= 0;
                ozetSatirlari[t].Visible = gorunur;
                if (!gorunur) continue;

                if (t <= 2) ozetSatirlari[t].Location = new Point(x, ozetY + 24 + (sol++) * 37);
                else ozetSatirlari[t].Location = new Point(x + satirG + 10, ozetY + 24 + (sag++) * 37);

                string komut = t == 0 ? atamalar[secili].Kisayol
                             : Al(anahtarlar[t], t == 3 ? "VOLUME_UP" : t == 4 ? "VOLUME_DOWN" : "");
                bool bos = komut.Length == 0 || komut == "NONE";

                ozetBasis[t].Text = adlar[t];
                string ad = bos ? "—" : AdBul(komut);
                string kisa = bos ? "" : GosterimKisayol(komut);
                ozetKomut[t].Text = bos || kisa == ad ? ad : ad + "  ·  " + kisa;

                bool aktif = t == basisTuru;
                ozetKomut[t].ForeColor = bos ? Tema.Pasif : (aktif ? Mavi : Lacivert);
                ozetSatirlari[t].DolguRengi = aktif ? Tema.Secili : KutuDolgu;
                ozetSatirlari[t].KenarRengi = aktif ? Mavi : Color.Transparent;
                ozetSatirlari[t].HoverRengi = aktif ? Color.Empty : Tema.Hover;
                ozetSatirlari[t].Invalidate(true);
            }
        }

        // Tuş yuvalarının ışığı: kartın LED efektinin renkleri (bağlı değilken karanlık)
        private void GovdeIsigiGuncelle()
        {
            if (govde == null) return;
            govde.IsikAyarla(klavyeBagli ? KlavyeGovdesi.ModRenkleri(ledMod, ledRenk, ledAkis) : new Color[0], ledParlaklik);
        }

        // ---------- Sol: PROFİLLER ----------
        private YuvarlakPanel profilPaneli;
        private Panel profilListesi;
        private Label lblProfilBos;

        private void ProfilPaneliOlustur()
        {
            profilPaneli = new YuvarlakPanel
            {
                Location = new Point(SolX, 58),
                Size = new Size(SolG, 622),
                Yaricap = 20,
                DolguRengi = Tema.Panel,
                KenarRengi = Tema.PanelKenar
            };

            Etiket(profilPaneli, "\uE8F1", Ikon(13f), Lacivert, 18, 22);
            Etiket(profilPaneli, "PROFİLLER", new Font("Segoe UI", 10.5f, FontStyle.Bold), Lacivert, 44, 20);

            // + : şu anki ayarları yeni profil olarak kaydet
            YuvarlakPanel ekle = new YuvarlakPanel
            {
                Location = new Point(SolG - 46, 15),
                Size = new Size(30, 30),
                Yaricap = 15,
                DolguRengi = Mavi,
                HoverRengi = MaviKoyu
            };
            Etiket(ekle, "\uE710", Ikon(10f), Color.White, 0, 0, 30, 30, ContentAlignment.MiddleCenter);
            HoverBagla(ekle);
            TiklaBagla(ekle, (s, e) => YeniProfilAc());
            BaloncukAyarla(ekle, "Klavyedeki şu anki ayarları yeni profil olarak kaydet");
            profilPaneli.Controls.Add(ekle);

            profilListesi = new Panel
            {
                Location = new Point(10, 58),
                Size = new Size(SolG - 20, 622 - 58 - 160),
                BackColor = Color.Transparent,
                AutoScroll = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            profilPaneli.Controls.Add(profilListesi);

            // + : listenin üstünde ad kutusu açılır (Enter kaydet, Esc vazgeç)
            yeniProfilKutu = new YuvarlakPanel
            {
                Location = new Point(10, 58),
                Size = new Size(SolG - 20, 36),
                Yaricap = 10,
                DolguRengi = KutuDolgu,
                KenarRengi = Mavi,
                KenarKalinligi = 2,
                Visible = false
            };
            txtYeniProfil = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = KutuDolgu,
                ForeColor = Lacivert,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(12, 9),
                Width = SolG - 20 - 24
            };
            txtYeniProfil.HandleCreated += (s, e) => IpucuYaz(txtYeniProfil.Handle, EM_SETCUEBANNER, (IntPtr)1, "Profil adı, Enter");
            txtYeniProfil.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; YeniProfilKaydet(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; YeniProfilKapat(); }
            };
            txtYeniProfil.LostFocus += (s, e) => { if (txtYeniProfil.Text.Trim().Length == 0) YeniProfilKapat(); };
            yeniProfilKutu.Controls.Add(txtYeniProfil);
            profilPaneli.Controls.Add(yeniProfilKutu);

            lblProfilBos = Etiket(profilPaneli, "Henüz profil yok.\n\nKlavyedeki ayarları + ile bir adla sakla; sonra buradan tek tıkla geç.",
                                  new Font("Segoe UI", 9f), SolukYazi, 18, 64, SolG - 36, 110);

            // Altta: hazır profiller, yedekle, geri yükle (eskiden Ayarlar > Profiller'deydi)
            YuvarlakPanel hazir = ProfilAltDugmesi("\uE8F1", "Hazır profiller…", 622 - 152,
                "Ofis, müzik, oyun, OBS, Visual Studio… hazır tuş düzenlerinden biriyle başla");
            TiklaBagla(hazir, (s, e) => HazirProfilMenusuAc(hazir));
            YuvarlakPanel yedekle = ProfilAltDugmesi("\uE78C", "Yedekle…", 622 - 104,
                "Tam yedek (.zip): profiller, Makrolarım, geçmiş, tuş adları / simgeleri, uygulama ayarları ve klavyedeki ayarlar. " +
                "Yeni bilgisayara taşımak için de kullanılır.");
            TiklaBagla(yedekle, (s, e) => ProfilYedekle());
            YuvarlakPanel geriYukle = ProfilAltDugmesi("\uE777", "Geri yükle…", 622 - 56,
                "Tam yedekten (.zip) ya da klavye ayarları dosyasından (.txt) geri yükle");
            TiklaBagla(geriYukle, (s, e) => YedektenGeriYukle());

            Controls.Add(profilPaneli);
            pencereSolu.Add(profilPaneli);
            ProfilListesiYenile();
        }

        private YuvarlakPanel yeniProfilKutu;
        private TextBox txtYeniProfil;
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern IntPtr IpucuYaz(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void YeniProfilAc()
        {
            if (yeniProfilKutu.Visible)
            {
                if (txtYeniProfil.Text.Trim().Length > 0) YeniProfilKaydet();
                else YeniProfilKapat();
                return;
            }
            if (ProfilMetni() == null)
            {
                Bildir("Önce klavyeye bağlan. Profil, klavyedeki ayarlardan oluşturulur.");
                MessageBox.Show(this, "Önce klavyeye bağlan. Profil, klavyedeki şu anki ayarlardan oluşturulur.",
                                "Yeni profil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            txtYeniProfil.Text = "";
            yeniProfilKutu.Visible = true;
            yeniProfilKutu.BringToFront();
            profilListesi.SetBounds(profilListesi.Left, 58 + 44, profilListesi.Width, profilListesi.Height - 44);
            lblProfilBos.Visible = false;
            txtYeniProfil.Focus();
        }

        private void YeniProfilKapat()
        {
            if (!yeniProfilKutu.Visible) return;
            yeniProfilKutu.Visible = false;
            profilListesi.SetBounds(profilListesi.Left, 58, profilListesi.Width, profilListesi.Height + 44);
            lblProfilBos.Visible = UygulamaAyarlari.Profiller().Count == 0;
        }

        private void YeniProfilKaydet()
        {
            string ad = UygulamaAyarlari.AdTemizle(txtYeniProfil.Text);
            if (ad.Length == 0) { YeniProfilKapat(); return; }

            if (System.IO.File.Exists(UygulamaAyarlari.ProfilYolu(ad)) &&
                MessageBox.Show(this, "\"" + ad + "\" profili zaten var. Üzerine yazılsın mı?", "Profil",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                txtYeniProfil.Focus();
                return;
            }

            txtYeniProfil.Text = "";
            YeniProfilKapat();
            ProfilOlarakKaydet(ad);
        }

        private YuvarlakPanel ProfilAltDugmesi(string simge, string yazi, int y, string ipucu)
        {
            YuvarlakPanel d = new YuvarlakPanel
            {
                Location = new Point(10, y),
                Size = new Size(SolG - 20, 40),
                Yaricap = 12,
                DolguRengi = KutuDolgu,
                KenarRengi = Tema.Kenar,
                HoverRengi = Tema.Hover,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Etiket(d, simge, Ikon(11f), Lacivert, 12, 0, 26, 40, ContentAlignment.MiddleCenter);
            Etiket(d, yazi, new Font("Segoe UI", 9f, FontStyle.Bold), Lacivert, 42, 0, SolG - 20 - 50, 40, ContentAlignment.MiddleLeft);
            HoverBagla(d);
            BaloncukAyarla(d, ipucu);
            profilPaneli.Controls.Add(d);
            return d;
        }

        private void ProfilListesiYenile()
        {
            if (profilListesi == null) return;

            List<string> adlar = UygulamaAyarlari.Profiller();
            string aktif = UygulamaAyarlari.AktifProfil;

            profilListesi.SuspendLayout();
            foreach (Control c in profilListesi.Controls.Cast<Control>().ToList()) c.Dispose();

            int w = profilListesi.Width - (adlar.Count * 40 > profilListesi.Height ? SystemInformation.VerticalScrollBarWidth : 0);
            for (int i = 0; i < adlar.Count; i++)
            {
                string ad = adlar[i];
                bool sec = string.Equals(ad, aktif, StringComparison.CurrentCultureIgnoreCase);

                YuvarlakPanel satir = new YuvarlakPanel
                {
                    Location = new Point(0, i * 40),
                    Size = new Size(w, 36),
                    Yaricap = 10,
                    DolguRengi = sec ? Tema.Secili : Tema.Panel,
                    KenarRengi = sec ? Mavi : Color.Transparent,
                    HoverRengi = sec ? Color.Empty : Tema.Hover
                };
                Etiket(satir, sec ? "\uE73E" : "\uE8F1", Ikon(9f), sec ? Mavi : SolukYazi, 8, 0, 22, 36, ContentAlignment.MiddleCenter);
                Label l = Etiket(satir, ad, new Font("Segoe UI", 9.5f, sec ? FontStyle.Bold : FontStyle.Regular),
                                 sec ? Mavi : Lacivert, 32, 0, w - 38, 36, ContentAlignment.MiddleLeft);
                l.AutoEllipsis = true;

                HoverBagla(satir);
                TiklaBagla(satir, (s, e) =>
                {
                    if (!klavyeBagli) { Bildir("Profil yüklemek için önce klavyeye bağlan."); return; }
                    if (!sec) ProfilSec(ad);
                });
                ProfilSatiriMenusu(satir, ad);
                profilListesi.Controls.Add(satir);
            }
            profilListesi.ResumeLayout();
            lblProfilBos.Visible = adlar.Count == 0;
        }

        // Profil satırına sağ tık: güncelle / sil
        private void ProfilSatiriMenusu(Control satir, string ad)
        {
            MouseEventHandler sagTik = (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                ContextMenuStrip m = TemaMenusu(true);

                ToolStripMenuItem yukle = MenuOgesi(null, "Yükle", () => ProfilSec(ad));
                yukle.Enabled = klavyeBagli;
                m.Items.Add(yukle);

                ToolStripMenuItem guncelle = MenuOgesi(null, "Şu anki ayarları bu profile kaydet", () => ProfilOlarakKaydet(ad));
                guncelle.Enabled = klavyeBagli;
                m.Items.Add(guncelle);

                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(MenuOgesi(null, "Sil…", () =>
                {
                    if (MessageBox.Show(this, "\"" + ad + "\" profili silinsin mi? Klavyedeki ayarlar değişmez.", "Profili sil",
                                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try
                    {
                        System.IO.File.Delete(UygulamaAyarlari.ProfilYolu(ad));
                        if (string.Equals(UygulamaAyarlari.AktifProfil, ad, StringComparison.CurrentCultureIgnoreCase))
                            UygulamaAyarlari.AktifProfil = "";
                        ProfilEtiketiGuncelle();
                        Bildir("\"" + ad + "\" profili silindi.");
                    }
                    catch (Exception ex) { Bildir("Silinemedi: " + ex.Message); }
                }));

                m.Closed += (o, a) => BeginInvoke(new Action(m.Dispose));
                m.Show((Control)s, e.Location);
            };
            satir.MouseUp += sagTik;
            foreach (Control c in satir.Controls) c.MouseUp += sagTik;
        }

        // ---------- Sağ: TUŞ AYARLARI paneli ----------
        private void AyarPaneliOlustur()
        {
            var panel = new YuvarlakPanel
            {
                Location = new Point(SagX, 58),
                Size = new Size(395, 622),
                Yaricap = 20,
                DolguRengi = Tema.Panel,
                KenarRengi = Tema.PanelKenar
            };

            var etiketFont = new Font("Segoe UI", 11f, FontStyle.Bold);

            Etiket(panel, "\uE713", Ikon(20f), Lacivert, 30, 30);
            Etiket(panel, "TUŞ AYARLARI", new Font("Segoe UI", 17f, FontStyle.Bold),
                   Lacivert, 66, 26);

            Etiket(panel, "Seçilen Tuş", etiketFont, Lacivert, 30, 84);

            // Encoder seçiliyken: hazır çevirme modları
            lblEncoderModu = Etiket(panel, "", new Font("Segoe UI", 9f, FontStyle.Bold), Mavi,
                                    150, 85, 215, 22, ContentAlignment.MiddleRight);
            lblEncoderModu.Cursor = Cursors.Hand;
            lblEncoderModu.Visible = false;
            lblEncoderModu.Click += (s, e) => EncoderMenusuAc(lblEncoderModu, false);
            baloncuk.SetToolTip(lblEncoderModu, "Encoder'ı çevirince ne olacağını tek tıkla seç");

            var secici = new YuvarlakPanel
            {
                Location = new Point(30, 114),
                Size = new Size(335, 78),
                Yaricap = 10,
                DolguRengi = KutuDolgu,
                KenarRengi = Tema.Kenar
            };
            var sol = Etiket(secici, "\uE76B", Ikon(12f), Lacivert, 8, 0, 36, 78,
                             ContentAlignment.MiddleCenter);
            var sag = Etiket(secici, "\uE76C", Ikon(12f), Lacivert, 291, 0, 36, 78,
                             ContentAlignment.MiddleCenter);
            sol.Cursor = sag.Cursor = Cursors.Hand;
            sol.Click += (s, e) => KullaniciTusSec((secili + atamalar.Count - 1) % atamalar.Count);
            sag.Click += (s, e) => KullaniciTusSec((secili + 1) % atamalar.Count);

            lblTusAdi = Etiket(secici, "BUTON 1", new Font("Segoe UI", 13f, FontStyle.Bold),
                               Lacivert, 44, 14, 247, 28, ContentAlignment.MiddleCenter);
            lblTusPin = Etiket(secici, "(GP1)", new Font("Segoe UI", 10f), SolukYazi,
                               44, 42, 247, 22, ContentAlignment.MiddleCenter);
            lblTusPin.AutoEllipsis = true;
            panel.Controls.Add(secici);

            // Kısa / uzun / çift basış: klavyenin altındaki "Bütün basışlar" listesinden seçilir
            BasisOlustur(new Panel());

            Etiket(panel, "Uygulama / Komut", etiketFont, Lacivert, 30, 212);

            var comboKutu = komutKutusu = KutuOlustur(30, 242);
            cmbUygulama = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12f),
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Left = 16,
                Top = 11,
                Width = 300
            };
            for (int i = 0; i < Hazirlar.GetLength(0); i++) cmbUygulama.Items.Add(Hazirlar[i, 0]);
            cmbUygulama.Items.Add(OzelKisayolAdi);
            cmbUygulama.Items.Add(MetinYazAdi);
            cmbUygulama.Items.Add(UygulamaAdi);
            cmbUygulama.Items.Add(MakroAdi);
            cmbUygulama.SelectedIndexChanged += ComboDegisti;
            cmbUygulama.SelectedIndexChanged += (s, e) => lblKomutSecim.Text = cmbUygulama.Text;
            cmbUygulama.Visible = false;
            comboKutu.Controls.Add(cmbUygulama);

            lblKomutSecim = Etiket(comboKutu, "", new Font("Segoe UI", 12f), Lacivert,
                                   16, 0, 270, 52, ContentAlignment.MiddleLeft);
            lblKomutSecim.AutoEllipsis = true;
            Etiket(comboKutu, "\uE70D", Ikon(11f), SolukYazi, 290, 0, 34, 52, ContentAlignment.MiddleCenter);

            comboKutu.HoverRengi = Tema.Hover;
            HoverBagla(comboKutu);
            TiklaBagla(comboKutu, (s, e) => KomutMenusuAc(comboKutu));
            panel.Controls.Add(comboKutu);

            // --- Kutu: metin / kısayol / uygulama / makro (hazır komutta gizli).
            //     İçindeki küçük başlık ne yapıldığını söyler.
            kisayolKutu = KutuOlustur(30, 310);
            kisayolKutu.Height = 60;
            lblKutuBaslik = Etiket(kisayolKutu, "", new Font("Segoe UI", 7.5f, FontStyle.Bold), SolukYazi,
                                   18, 8, 270, 14);
            lblKutuBaslik.Click += (s, e) => KutuyaTiklandi();
            txtKisayol = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Font = new Font("Segoe UI", 12f),
                Left = 18,
                Top = 26,
                Width = 262,
                ReadOnly = true,
                TabStop = false,      // pencere açılınca kendiliğinden odak almasın
                Cursor = Cursors.Hand
            };
            txtKisayol.PreviewKeyDown += (s, e) => e.IsInputKey = true;   // Tab/ok tuşları da yakalansın
            txtKisayol.KeyDown += KisayolTusu;   // kanca çalışmazsa yedek yöntem
            txtKisayol.GotFocus += (s, e) =>
            {
                if (yakalamaAcik && !metinModu && !uygulamaModu && !makroModu) kanca.Baslat();
                if (!metinModu) BeginInvoke(new Action(() => txtKisayol.SelectionLength = 0));   // mavi seçim olmasın
            };
            txtKisayol.LostFocus += (s, e) => YakalamayiKapat();
            txtKisayol.Click += (s, e) => KutuyaTiklandi();   // uygulama / makro değiştir ya da kısayol yakala
            kisayolKutu.Controls.Add(txtKisayol);
            lblKlavyeIkon = Etiket(kisayolKutu, "\uE765", Ikon(15f), SolukYazi, 290, 0, 34, 60,
                   ContentAlignment.MiddleCenter);
            kisayolKutu.Click += (s, e) => KutuyaTiklandi();
            lblKlavyeIkon.Click += (s, e) => KutuyaTiklandi();

            // Metin kutusunda göz: metni gizli yap (PIN, şifre) ya da göster
            lblGozIkon = Etiket(kisayolKutu, "\uE7B3", Ikon(14f), SolukYazi, 290, 0, 34, 60,
                                ContentAlignment.MiddleCenter);
            lblGozIkon.Cursor = Cursors.Hand;
            lblGozIkon.Visible = false;
            lblGozIkon.Click += (s, e) => MetinGizliDegistir();
            baloncuk.SetToolTip(lblGozIkon, "Gizli metin: uygulamada ●●●●●● görünür, klavye aynen yazar");

            txtKisayol.TextChanged += (s, e) => { if (!yukleniyor) KaydetDurumu(); };
            panel.Controls.Add(kisayolKutu);

            lblIpucu = Etiket(panel, "", new Font("Segoe UI", 8.5f), SolukYazi, 32, 374, 330, 36);   // 2 satıra kadar

            var kaydet = btnKaydet = new YuvarlakPanel
            {
                Location = new Point(30, 432),
                Size = new Size(335, 52),
                Yaricap = 10,
                DolguRengi = Mavi,
                HoverRengi = MaviKoyu
            };
            Etiket(kaydet, "\uE74E", Ikon(15f), Color.White, 22, 0, 30, 52,
                   ContentAlignment.MiddleCenter);
            lblKaydetYazi = Etiket(kaydet, "AYARI KAYDET", new Font("Segoe UI", 11f, FontStyle.Bold),
                                   Color.White, 56, 0, 260, 52, ContentAlignment.MiddleLeft);
            HoverBagla(kaydet);
            TiklaBagla(kaydet, (s, e) => AyariKaydet());
            panel.Controls.Add(kaydet);

            // Kaydedilmemiş değişiklik: düğmenin hemen üstünde kırmızı yazı
            lblKaydedilmedi = Etiket(panel, "●  Kaydedilmedi — AYARI KAYDET'e bas", new Font("Segoe UI", 9f, FontStyle.Bold),
                                     Tema.HataYazi, 32, 411, 330, 20, ContentAlignment.MiddleLeft);
            lblKaydedilmedi.Visible = false;
            lblKaydedilmedi.BringToFront();


            var cizgi = new Panel
            {
                Location = new Point(30, 556),
                Size = new Size(335, 1),
                BackColor = Tema.Kenar,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            panel.Controls.Add(cizgi);

            Etiket(panel, "\uE946", Ikon(13f), SolukYazi, 30, 574).Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblBilgi = Etiket(panel, VarsayilanBilgi, new Font("Segoe UI", 9f), SolukYazi,
                              58, 573, 310, 36);
            lblBilgi.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            Controls.Add(panel);
            pencereSagi.Add(panel);
        }
    }
}
