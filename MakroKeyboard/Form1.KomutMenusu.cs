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
    // Form1'in parçası: Komut menüsü: arama, tür kartları, kategoriler
    public partial class Form1
    {
        // =================================================================
        //  KOMUT MENÜSÜ
        //  Üstte arama, altında beş "tür" kartı (kısayol, metin, uygulama,
        //  makro, boş), en altta solda kategoriler + sağda o kategorinin komutları
        // =================================================================
        // Kategoriler: { ad, simge, hazır komutlar... }
        private static readonly string[][] MenuKategorileri =
        {
            new[] { "Genel", "", "WINDOWS+D", "CTRL+C", "CTRL+V", "ALT+TAB", "DISPLAY", "PIXEL_MODE" },
            new[] { "Düzenleme", "", "CTRL+X", "CTRL+Z", "CTRL+Y", "CTRL+A", "CTRL+S", "CTRL+F" },
            new[] { "Medya ve Ses", "", "MEDIA_PLAY_PAUSE", "MEDIA_PREVIOUS", "MEDIA_NEXT", "MEDIA_STOP",
                    "MEDIA_FAST_FORWARD", "MEDIA_REWIND", "MUTE", "VOLUME_UP", "VOLUME_DOWN" },
            new[] { "Tarayıcı", "", "CTRL+T", "CTRL+W", "CTRL+SHIFT+T", "F5", "BROWSER_HOME",
                    "BROWSER_BACK", "BROWSER_FORWARD", "BROWSER_REFRESH", "BROWSER_STOP", "BROWSER_FAVORITES" },
            new[] { "Pencere ve Sistem", "", "SHIFT+WINDOWS+S", "CTRL+SHIFT+ESC", "WINDOWS+E",
                    "WINDOWS+LEFT", "WINDOWS+RIGHT", "WINDOWS+V", "WINDOWS+L", "NOSLEEP" },
            new[] { "Özel Tuşlar", "", "BRIGHTNESS_UP", "BRIGHTNESS_DOWN", "CALCULATOR", "EMAIL", "MY_COMPUTER",
                    "WINDOWS_SEARCH", "MEDIA_PLAYER" },
            new[] { "Ek Tuşlar", "\uE765", "APPLICATION", "PAUSE", "SCROLL_LOCK", "INSERT", "PRINT_SCREEN", "KEYPAD_EQUALS" },
            new[] { "Fare", "", "SCROLL_UP", "SCROLL_DOWN", "ZOOM_IN", "ZOOM_OUT",
                    "MOUSE_LEFT", "MOUSE_RIGHT", "MOUSE_MIDDLE" },
            new[] { "F13 – F24", "\uE92E", "F13", "F14", "F15", "F16", "F17", "F18",
                    "F19", "F20", "F21", "F22", "F23", "F24" }
        };

        // Tür kartları: { öğe, simge, kısa ad, açıklama }
        private static readonly string[][] MenuTurleri =
        {
            new[] { "@OZEL",     "", "Kısayol",  "Kendi tuş kombinasyonun" },
            new[] { "@METIN",    "", "Metin",    "Bir yazı yazdır" },
            new[] { "@UYGULAMA", "", "Uygulama", "Program, klasör, site aç" },
            new[] { "@MAKRO",    "", "Makro",    "Çok adımlı işlem" },
            new[] { "NONE",      "", "Boş",      "Tuş bir şey yapmasın" }
        };

        private const string SikKategori = "Sık kullanılan";

        // Aramada adın dışında da eşleşen kelimeler
        private static readonly Dictionary<string, string> AramaKelimeleri = new Dictionary<string, string>
        {
            { "WINDOWS+D", "masaustu desktop pencereleri gizle" },
            { "MEDIA_STOP", "muzik video medya durdur stop" },
            { "MEDIA_FAST_FORWARD", "muzik video ileri sar hizli" },
            { "MEDIA_REWIND", "muzik video geri sar" },
            { "BRIGHTNESS_UP", "ekran parlaklik aydinlik isik" },
            { "BRIGHTNESS_DOWN", "ekran parlaklik karart isik" },
            { "CALCULATOR", "hesap makinesi calculator" },
            { "BROWSER_HOME", "internet tarayici chrome edge web" },
            { "EMAIL", "posta mail outlook" },
            { "MY_COMPUTER", "bu bilgisayar dosya gezgini explorer" },
            { "NOSLEEP", "uyku uyuma kilitlenme ekran koruyucu fare oynat kapanmasin acik tut" },
            { "CTRL+C", "copy" },
            { "CTRL+V", "paste" },
            { "ALT+TAB", "pencere gecis degistir" },
            { "DISPLAY", "ekran monitor projeksiyon yansit win p" },
            { "MEDIA_PLAY_PAUSE", "muzik sarki medya baslat duraklat play pause" },
            { "MEDIA_PREVIOUS", "muzik sarki medya geri" },
            { "MEDIA_NEXT", "muzik sarki medya ileri" },
            { "MUTE", "ses sessiz kapat" },
            { "VOLUME_UP", "ses yukselt volume" },
            { "VOLUME_DOWN", "ses dusur kis volume" },
            { "SCROLL_UP", "fare tekerlek kaydir" },
            { "SCROLL_DOWN", "fare tekerlek kaydir" },
            { "ZOOM_IN", "buyut zoom" },
            { "ZOOM_OUT", "kucult zoom" },
            { "MOUSE_LEFT", "fare tikla mouse" },
            { "MOUSE_RIGHT", "fare tikla mouse menu" },
            { "MOUSE_MIDDLE", "fare tikla mouse tekerlek" },
            { "CTRL+X", "cut" },
            { "CTRL+Z", "undo" },
            { "CTRL+Y", "redo ileri al" },
            { "CTRL+A", "hepsini select all" },
            { "CTRL+S", "save" },
            { "CTRL+F", "ara find" },
            { "SHIFT+WINDOWS+S", "ekran alintisi screenshot resim kirp" },
            { "CTRL+SHIFT+ESC", "task manager islem program kapat" },
            { "WINDOWS+E", "explorer klasor dosyalar" },
            { "WINDOWS+LEFT", "pencere bol yarim ekran" },
            { "WINDOWS+RIGHT", "pencere bol yarim ekran" },
            { "WINDOWS+V", "pano kopyalananlar clipboard" },
            { "WINDOWS+L", "kilit kilitle oturum lock" },
            { "CTRL+T", "tarayici sekme chrome yeni" },
            { "CTRL+W", "tarayici sekme kapat" },
            { "CTRL+SHIFT+T", "tarayici sekme geri ac" },
            { "F5", "tarayici yenile refresh" },
            { "NONE", "bos hicbir sey devre disi" },
            { "@OZEL", "kisayol ozel tus kombinasyon" },
            { "@METIN", "metin yazi text sifre yaz" },
            { "@UYGULAMA", "uygulama program klasor site web dosya ac link" },
            { "@MAKRO", "makro adim cok kayit" },
            { "PIXEL_MODE", "led isik mod renk" },
            { "APPLICATION", "menu tusu sag tik baglam context" }, { "PAUSE", "pause break duraklat" },
            { "SCROLL_LOCK", "scroll lock kaydirma kilidi" }, { "INSERT", "insert ekle uzerine yaz" },
            { "PRINT_SCREEN", "print screen prtsc ekran goruntusu" }, { "KEYPAD_EQUALS", "esittir tus takimi numpad" },
            { "WINDOWS_SEARCH", "windows arama ara bul search" }, { "BROWSER_BACK", "tarayici geri back" },
            { "BROWSER_FORWARD", "tarayici ileri forward" }, { "BROWSER_REFRESH", "tarayici yenile refresh" },
            { "BROWSER_STOP", "tarayici durdur stop" }, { "BROWSER_FAVORITES", "tarayici sik kullanilanlar yer imi favori" },
            { "MEDIA_PLAYER", "medya oynatici muzik video player" },
            { "F13", "f13 ek fonksiyon obs discord autohotkey ahk bos kisayol" }, { "F14", "f14 ek fonksiyon obs discord autohotkey ahk" },
            { "F15", "f15 ek fonksiyon obs discord autohotkey ahk" }, { "F16", "f16 ek fonksiyon obs discord autohotkey ahk" },
            { "F17", "f17 ek fonksiyon obs discord autohotkey ahk" }, { "F18", "f18 ek fonksiyon obs discord autohotkey ahk" },
            { "F19", "f19 ek fonksiyon obs discord autohotkey ahk" }, { "F20", "f20 ek fonksiyon obs discord autohotkey ahk" },
            { "F21", "f21 ek fonksiyon obs discord autohotkey ahk" }, { "F22", "f22 ek fonksiyon obs discord autohotkey ahk" },
            { "F23", "f23 ek fonksiyon obs discord autohotkey ahk" }, { "F24", "f24 ek fonksiyon obs discord autohotkey ahk" }
        };

        // Ölçüler
        private const int MenuGenislik = 580, MenuBosluk = 8, MenuSatir = 30;
        private const int MenuSolGenislik = 168;                         // kategori listesi
        private const int MenuSagX = MenuBosluk + MenuSolGenislik + 9;   // komut listesi
        private const int MenuSagGenislik = MenuGenislik - MenuSagX - MenuBosluk;

        private Panel menuGovde;
        private TextBox txtMenuAra;
        private string menuKategori;                                       // sol listede seçili kategori
        private readonly List<string> menuSonuclari = new List<string>();
        private int menuSecim;                                             // aramada Enter'ın seçeceği sonuç

        // Öğenin listede görünen adı (gizli ComboBox'taki adıyla aynı)
        private static string MenuOgeAdi(string oge)
        {
            switch (oge)
            {
                case "@OZEL": return OzelKisayolAdi;
                case "@METIN": return MetinYazAdi;
                case "@UYGULAMA": return UygulamaAdi;
                case "@MAKRO": return MakroAdi;
            }

            for (int i = 0; i < Hazirlar.GetLength(0); i++)
                if (Hazirlar[i, 1] == oge) return Hazirlar[i, 0];

            return null;
        }

        // Aramada bulunabilen her şey: önce türler, sonra kategorilerdeki komutlar
        private static IEnumerable<string> TumMenuOgeleri()
        {
            foreach (string[] tur in MenuTurleri) yield return tur[0];
            foreach (string[] kat in MenuKategorileri)
                for (int i = 2; i < kat.Length; i++)
                    yield return kat[i];
        }

        private static string[] MenuKategorisi(string ad)
        {
            return MenuKategorileri.FirstOrDefault(k => k[0] == ad);
        }

        // Komutun sağda soluk yazılan ipucu: "CTRL+SHIFT+T" -> "Ctrl + Shift + T"
        private static string MenuIpucu(string oge)
        {
            switch (oge)
            {
                case "DISPLAY": return "Win + P";
                case "NOSLEEP": return "aç / kapat";
                case "PIXEL_MODE": return "sıradaki efekt";
            }

            // F13-F24: gerçek klavyede yok; başka programda kısayol olarak atanır
            int fno;
            if (oge.StartsWith("F") && int.TryParse(oge.Substring(1), out fno) && fno >= 13)
                return "programda ata";

            if (!oge.Contains("+") && !(oge.Length <= 3 && oge.StartsWith("F"))) return "";

            return string.Join(" + ", oge.Split('+').Select(p =>
            {
                switch (p)
                {
                    case "CTRL": return "Ctrl";
                    case "SHIFT": return "Shift";
                    case "ALT": return "Alt";
                    case "WINDOWS": return "Win";
                    case "ESC": return "Esc";
                    case "TAB": return "Tab";
                    case "LEFT": return "←";
                    case "RIGHT": return "→";
                    default: return p;
                }
            }));
        }

        // Türkçe harfleri sadeleştirir: "Yapıştır" -> "yapistir"
        private static string AramaNorm(string s)
        {
            s = (s ?? "").ToLower(new System.Globalization.CultureInfo("tr-TR"));
            return s.Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g')
                    .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c');
        }

        // Menüden en çok seçilen hazır komutlar (türler hariç, onların kendi kartı var)
        private static List<string> SikKullanilanlar()
        {
            HashSet<string> turler = new HashSet<string>(MenuTurleri.Select(t => t[0]));
            return UygulamaAyarlari.MenuSayaclari()
                .Where(kv => kv.Value > 0 && !turler.Contains(kv.Key) && MenuOgeAdi(kv.Key) != null)
                .OrderByDescending(kv => kv.Value)
                .Take(MenuKategorileri.Max(k => k.Length - 2))
                .Select(kv => kv.Key)
                .ToList();
        }

        private void KomutMenusuAc(Control kutu)
        {
            // Açıkken kutuya tıklanınca menü kapanır; aynı tıklama onu yeniden açmasın
            if ((DateTime.Now - komutMenusuKapandi).TotalMilliseconds < 250) return;

            Panel icerik = KomutMenusuIcerigi();

            ToolStripControlHost tutucu = new ToolStripControlHost(icerik)
            {
                AutoSize = false,
                Size = icerik.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            komutMenusu = new ToolStripDropDown
            {
                Padding = new Padding(1),
                Renderer = new MenuCizici(),
                DropShadowEnabled = true
            };
            komutMenusu.Items.Add(tutucu);
            komutMenusu.Closed += (s, e) => komutMenusuKapandi = DateTime.Now;

            // Sağ kenara hizalı: pencerenin sağ tarafındaki panelden taşmasın
            komutMenusu.Show(kutu, new Point(kutu.Width - icerik.Width - 2, kutu.Height + 4));
            txtMenuAra.Focus();
        }

        private Panel KomutMenusuIcerigi()
        {
            Panel p = new Panel { BackColor = Tema.Kart };
            int icGenislik = MenuGenislik - MenuBosluk * 2;

            // --- Arama kutusu ---
            YuvarlakPanel araKutu = new YuvarlakPanel
            {
                Location = new Point(MenuBosluk, MenuBosluk),
                Size = new Size(icGenislik, 36),
                Yaricap = 10,
                DolguRengi = Tema.KartAcik,
                KenarRengi = Tema.Kenar
            };
            Etiket(araKutu, "", Ikon(11f), SolukYazi, 6, 0, 30, 36, ContentAlignment.MiddleCenter);
            txtMenuAra = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.KartAcik,
                ForeColor = Lacivert,
                Font = new Font("Segoe UI", 10.5f),
                Location = new Point(40, 8),
                Width = araKutu.Width - 52
            };
            Label araIpucu = Etiket(araKutu, "Komut ara… (örn. ses, kopyala, sekme, uyku)",
                                    new Font("Segoe UI", 10.5f), Tema.Pasif, 40, 7);
            araIpucu.Cursor = Cursors.IBeam;
            araIpucu.Click += (s, e) => txtMenuAra.Focus();
            araKutu.Click += (s, e) => txtMenuAra.Focus();
            txtMenuAra.PreviewKeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) e.IsInputKey = true;
            };
            txtMenuAra.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (menuSecim >= 0 && menuSecim < menuSonuclari.Count) KomutMenusundenSec(menuSonuclari[menuSecim]);
                }
                else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && menuSonuclari.Count > 0)
                {
                    // Ok tuşlarıyla sonuçlar arasında gezinme
                    e.SuppressKeyPress = true;
                    int yeni = menuSecim + (e.KeyCode == Keys.Down ? 1 : -1);
                    if (yeni < 0 || yeni >= menuSonuclari.Count) return;
                    menuSecim = yeni;
                    MenuGovdesiniDoldur(false);
                }
            };
            txtMenuAra.TextChanged += (s, e) =>
            {
                araIpucu.Visible = txtMenuAra.Text.Length == 0;
                MenuGovdesiniDoldur();
            };
            araKutu.Controls.Add(txtMenuAra);
            araIpucu.BringToFront();
            p.Controls.Add(araKutu);

            int y = araKutu.Bottom + 10;

            // --- Tür kartları: tuş ne yapsın? ---
            int turW = (icGenislik - 6 * (MenuTurleri.Length - 1)) / MenuTurleri.Length;
            for (int i = 0; i < MenuTurleri.Length; i++)
                TurKartiOlustur(p, MenuTurleri[i], MenuBosluk + i * (turW + 6), y, turW);
            y += 62 + 10;

            p.Controls.Add(new Panel
            {
                Location = new Point(MenuBosluk, y),
                Size = new Size(icGenislik, 1),
                BackColor = Tema.Kenar
            });
            y += 9;

            // --- Gövde: solda kategoriler, sağda komutlar (aramada tüm genişlikte sonuçlar) ---
            int kategoriSayisi = MenuKategorileri.Length + 1;   // + Sık kullanılan
            int enCokKomut = MenuKategorileri.Max(k => k.Length - 2);
            int govdeH = Math.Max(kategoriSayisi * (MenuSatir + 2), enCokKomut * MenuSatir);

            menuGovde = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(MenuGenislik, govdeH),
                BackColor = Tema.Kart
            };
            p.Controls.Add(menuGovde);

            // Açılışta seçili komutun kategorisi; yoksa sık kullanılanlar ya da ilk kategori
            string secili = TumMenuOgeleri().FirstOrDefault(o => MenuOgeAdi(o) == cmbUygulama.Text);
            string[] kat = secili == null ? null
                         : MenuKategorileri.FirstOrDefault(k => Array.IndexOf(k, secili, 2) >= 2);
            menuKategori = kat != null ? kat[0]
                         : SikKullanilanlar().Count > 0 ? SikKategori
                         : MenuKategorileri[0][0];

            MenuGovdesiniDoldur();

            p.Size = new Size(MenuGenislik, y + govdeH + MenuBosluk);
            return p;
        }

        // Üstteki büyük kart: simge, ad ve kısa açıklama
        private void TurKartiOlustur(Control p, string[] tur, int x, int y, int genislik)
        {
            bool aktif = MenuOgeAdi(tur[0]) == cmbUygulama.Text;

            YuvarlakPanel k = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(genislik, 62),
                Yaricap = 10,
                DolguRengi = aktif ? Tema.Secili : Tema.KartAcik,
                HoverRengi = aktif ? Color.Empty : Tema.Hover,
                KenarRengi = aktif ? Mavi : Tema.Kenar
            };
            Etiket(k, tur[1], Ikon(14f), aktif ? Mavi : Lacivert, 0, 6, genislik, 22, ContentAlignment.MiddleCenter);
            Etiket(k, tur[2], new Font("Segoe UI", 9.5f, FontStyle.Bold), aktif ? Mavi : Lacivert,
                   0, 28, genislik, 16, ContentAlignment.MiddleCenter);
            Label a = Etiket(k, tur[3], new Font("Segoe UI", 7.5f), SolukYazi, 2, 44, genislik - 4, 14,
                             ContentAlignment.MiddleCenter);
            a.AutoEllipsis = true;

            HoverBagla(k);
            TiklaBagla(k, (s, e) => KomutMenusundenSec(tur[0]));
            p.Controls.Add(k);
        }

        // Arama boşsa kategoriler + komutlar, doluysa eşleşen her şey (iki sütun)
        private void MenuGovdesiniDoldur(bool aramaDegisti = true)
        {
            menuGovde.SuspendLayout();
            foreach (Control c in menuGovde.Controls.Cast<Control>().ToList()) c.Dispose();

            string ara = AramaNorm(txtMenuAra.Text.Trim());

            if (ara.Length == 0)
            {
                menuSonuclari.Clear();
                menuSecim = -1;
                KategorileriDoldur();
            }
            else
            {
                if (aramaDegisti)
                {
                    menuSonuclari.Clear();
                    menuSecim = 0;
                    string[] kelimeler = ara.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string oge in TumMenuOgeleri())
                    {
                        if (MenuOgeAdi(oge) == null || menuSonuclari.Contains(oge)) continue;
                        string ek;
                        AramaKelimeleri.TryGetValue(oge, out ek);
                        string metin = AramaNorm(MenuOgeAdi(oge) + " " + ek + " " + oge.Replace("+", " "));
                        if (kelimeler.All(k => metin.Contains(k))) menuSonuclari.Add(oge);
                    }
                }

                int satirSayisi = menuGovde.Height / MenuSatir;
                int sutunW = (MenuGenislik - MenuBosluk * 3) / 2;
                int sigan = Math.Min(menuSonuclari.Count, satirSayisi * 2);

                // Önce sol sütun yukarıdan aşağı, sonra sağ sütun (ok tuşlarıyla gezinme sırası)
                for (int i = 0; i < sigan; i++)
                {
                    int sutun = i / satirSayisi, satir = i % satirSayisi;
                    MenuOgesiOlustur(menuGovde, menuSonuclari[i],
                                     MenuBosluk + sutun * (sutunW + MenuBosluk), satir * MenuSatir,
                                     sutunW, i == menuSecim, true);
                }

                if (menuSonuclari.Count == 0)
                    Etiket(menuGovde, "Sonuç bulunamadı", new Font("Segoe UI", 10f), SolukYazi,
                           0, 20, menuGovde.Width, 30, ContentAlignment.MiddleCenter);
                else if (menuSonuclari.Count > sigan)
                    Etiket(menuGovde, "+" + (menuSonuclari.Count - sigan) + " sonuç daha, aramayı daralt",
                           new Font("Segoe UI", 8f), SolukYazi, MenuBosluk, menuGovde.Height - 16,
                           MenuGenislik - MenuBosluk * 2, 16, ContentAlignment.MiddleRight);
            }

            menuGovde.ResumeLayout();
        }

        // Sol: kategori listesi; sağ: seçili kategorinin komutları
        private void KategorileriDoldur()
        {
            List<string> sik = SikKullanilanlar();
            if (menuKategori == SikKategori && sik.Count == 0) menuKategori = MenuKategorileri[0][0];

            int y = 0;
            if (sik.Count > 0)
            {
                KategoriSatiri(SikKategori, "", y);
                y += MenuSatir + 2;
            }
            foreach (string[] k in MenuKategorileri)
            {
                KategoriSatiri(k[0], k[1], y);
                y += MenuSatir + 2;
            }

            // Ayırıcı çizgi
            menuGovde.Controls.Add(new Panel
            {
                Location = new Point(MenuBosluk + MenuSolGenislik + 4, 0),
                Size = new Size(1, menuGovde.Height),
                BackColor = Tema.Kenar
            });

            IEnumerable<string> komutlar = menuKategori == SikKategori
                ? sik
                : MenuKategorisi(menuKategori).Skip(2);

            y = 0;
            foreach (string oge in komutlar)
            {
                if (MenuOgeAdi(oge) == null) continue;
                MenuOgesiOlustur(menuGovde, oge, MenuSagX, y, MenuSagGenislik);
                y += MenuSatir;
            }
        }

        private void KategoriSatiri(string ad, string simge, int y)
        {
            bool secili = ad == menuKategori;

            YuvarlakPanel o = new YuvarlakPanel
            {
                Location = new Point(MenuBosluk, y),
                Size = new Size(MenuSolGenislik, MenuSatir),
                Yaricap = 8,
                DolguRengi = secili ? Tema.Secili : Tema.Kart,
                HoverRengi = secili ? Color.Empty : Tema.Hover,
                KenarRengi = Color.Transparent
            };
            Etiket(o, simge, Ikon(10f), secili ? Mavi : SolukYazi, 4, 0, 26, MenuSatir, ContentAlignment.MiddleCenter);
            Label l = Etiket(o, ad, new Font("Segoe UI", 9.5f, secili ? FontStyle.Bold : FontStyle.Regular),
                             secili ? Mavi : Lacivert, 32, 0, MenuSolGenislik - 36, MenuSatir, ContentAlignment.MiddleLeft);
            l.AutoEllipsis = true;

            HoverBagla(o);
            TiklaBagla(o, (s, e) =>
            {
                if (menuKategori == ad) return;
                menuKategori = ad;
                MenuGovdesiniDoldur();
                txtMenuAra.Focus();
            });
            menuGovde.Controls.Add(o);
        }

        // Tıklanabilir komut satırı: solda ad, sağda soluk ipucu (kısayol tuşları).
        // vurgu: aramada Enter'ın seçeceği öğe (kenarlıklı); kategoriGoster: aramada hangi kategoride olduğu
        private YuvarlakPanel MenuOgesiOlustur(Control p, string oge, int x, int y, int genislik,
                                               bool vurgu = false, bool kategoriGoster = false)
        {
            string ad = MenuOgeAdi(oge);
            bool aktif = ad == cmbUygulama.Text;

            string ipucu = MenuIpucu(oge);
            if (kategoriGoster)
            {
                string[] kat = MenuKategorileri.FirstOrDefault(k => Array.IndexOf(k, oge, 2) >= 2);
                string tur = MenuTurleri.Where(t => t[0] == oge).Select(t => t[3]).FirstOrDefault();
                ipucu = ipucu.Length > 0 ? ipucu : kat != null ? kat[0] : tur ?? "";
            }

            YuvarlakPanel o = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(genislik, MenuSatir - 2),
                Yaricap = 8,
                DolguRengi = aktif ? Tema.Secili : Tema.Kart,
                HoverRengi = aktif ? Color.Empty : Tema.Hover,
                KenarRengi = vurgu ? Mavi : Color.Transparent
            };

            int ipucuW = ipucu.Length == 0 ? 0 : Math.Min(genislik / 2, TextRenderer.MeasureText(ipucu, new Font("Segoe UI", 8.5f)).Width + 6);
            Label l = Etiket(o, ad, new Font("Segoe UI", 9.5f, aktif ? FontStyle.Bold : FontStyle.Regular),
                             aktif ? Mavi : Lacivert, 10, 0, genislik - 16 - ipucuW, MenuSatir - 2, ContentAlignment.MiddleLeft);
            l.AutoEllipsis = true;
            if (ipucuW > 0)
            {
                Label i = Etiket(o, ipucu, new Font("Segoe UI", 8.5f), SolukYazi,
                                 genislik - 8 - ipucuW, 0, ipucuW, MenuSatir - 2, ContentAlignment.MiddleRight);
                i.AutoEllipsis = true;
            }

            HoverBagla(o);
            TiklaBagla(o, (s, e) => KomutMenusundenSec(oge));
            p.Controls.Add(o);
            return o;
        }

        private void KomutMenusundenSec(string oge)
        {
            if (komutMenusu != null) komutMenusu.Close();

            string ad = MenuOgeAdi(oge);
            int i = ad == null ? -1 : cmbUygulama.Items.IndexOf(ad);
            if (i < 0) return;

            UygulamaAyarlari.MenuSayaciArtir(oge);

            if (i == cmbUygulama.SelectedIndex)
                ComboDegisti(cmbUygulama, EventArgs.Empty);   // aynı öğe: Uygulama/Makro/Özel yeniden açılsın
            else
                cmbUygulama.SelectedIndex = i;
        }

        // Menünün zemini ve kenarı temaya uysun
        private class MenuCizici : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var b = new SolidBrush(Tema.Kart))
                    e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var k = new Pen(Tema.Kenar))
                    e.Graphics.DrawRectangle(k, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        private YuvarlakPanel KutuOlustur(int x, int y)
        {
            return new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(335, 52),
                Yaricap = 10,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };
        }
    }
}
