using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // LED efekti, rengi, parlaklığı ve hızı seçilen pencere.
    //   Üst: 13 LED'lik canlı önizleme (kartın animasyonlarıyla aynı, klavye takılı olmasa da çalışır).
    //   Sol: renk paneli (Windows renk penceresindeki 48 temel renk, son kullanılanlar,
    //        renk spektrumu, parlaklık çubuğu, renk kodu). "3 renk akışı"nda üç renk ayrı ayrı seçilir.
    //   Sağ: efektler, parlaklık, hız, İPTAL / KAYDET.
    // Her değişiklikte "Degisti" olayı tetiklenir (ana form kartta anında önizler).
    // KAYDET -> DialogResult.OK, İPTAL / ✕ / Esc -> DialogResult.Cancel
    public class LedAyarForm : Form
    {
        // ---------- Win32 (köşe yuvarlama + sürükleme) ----------
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int deger, int boyut);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private readonly bool win11 = Environment.OSVersion.Version.Build >= 22000;

        // ---------- Renkler (ana formla aynı) ----------
        private static readonly Color Mavi = Color.FromArgb(16, 104, 232);
        private static readonly Color MaviKoyu = Color.FromArgb(10, 82, 190);
        private static readonly Color Lacivert = Tema.Yazi;
        private static readonly Color SolukYazi = Tema.SolukYazi;
        private static readonly Color Kenar = Tema.Kenar;

        // {kart tarafındaki mod adı, görünen ad}
        private static readonly string[,] Modlar =
        {
            { "white",      "Beyaz" },
            { "red",        "Kırmızı" },
            { "green",      "Yeşil" },
            { "blue",       "Mavi" },
            { "purple",     "Mor" },
            { "color",      "Özel renk" },
            { "breathe",    "Nefes" },
            { "heartbeat",  "Kalp atışı" },
            { "comet",      "Kuyruklu yıldız" },
            { "scanner",    "Tarayıcı" },
            { "chase",      "Takip" },
            { "wipe",       "Doldur" },
            { "sparkle",    "Pırıltı" },
            { "red_purple_white", "3 renk akışı" },
            { "rainbow",    "Gökkuşağı" },
            { "colorcycle", "Renk geçişi" },
            { "wave",       "Dalga" },
            { "fire",       "Ateş" },
            { "confetti",   "Konfeti" },
            { "police",     "Polis" },
            { "random",     "Rastgele" },
            { "off",        "Kapalı" }
        };

        // Kart mod adının görünen karşılığı ("comet" -> "Kuyruklu yıldız")
        internal static string ModAdi(string mod)
        {
            for (int i = 0; i < Modlar.GetLength(0); i++)
                if (Modlar[i, 0] == mod) return Modlar[i, 1];
            return string.IsNullOrEmpty(mod) ? "—" : mod;
        }

        // Windows renk seçme penceresindeki 48 temel renk (aynı sırayla)
        private static readonly string[] TemelRenkler =
        {
            "FF8080", "FFFF80", "80FF80", "00FF80", "80FFFF", "0080FF", "FF80C0", "FF80FF",
            "FF0000", "FFFF00", "80FF00", "00FF40", "00FFFF", "0080C0", "8080C0", "FF00FF",
            "804040", "FF8040", "00FF00", "008080", "004080", "8080FF", "800040", "FF0080",
            "800000", "FF8000", "008000", "008040", "0000FF", "0000A0", "800080", "8000FF",
            "400000", "804000", "004000", "004040", "000080", "000040", "400040", "400080",
            "000000", "808000", "808040", "808080", "408080", "C0C0C0", "400040", "FFFFFF"
        };

        // Seçilen rengi kullanan efektler
        private static readonly string[] RenkliModlar =
            { "color", "breathe", "heartbeat", "comet", "scanner", "chase", "wipe", "sparkle" };

        // Kendi 3 rengini kullanan akış (renkleri ayrıca seçilir)
        private const string AkisModu = "red_purple_white";
        public const string VarsayilanAkis = "FF0000,8C00FF,FFFFFF";

        // Hareketsiz modlar: hız ayarı bunlarda işe yaramaz
        private static readonly string[] SabitModlar =
            { "white", "red", "green", "blue", "purple", "color", "random", "off" };

        public const int HizEnAz = 25, HizEnCok = 300;

        // ---------- Sonuç ----------
        public string Mod { get; private set; }
        public string RenkHex { get; private set; }     // RRGGBB
        public int Parlaklik { get; private set; }      // 5-100
        public int Hiz { get; private set; }            // % (100 = normal)
        private readonly string[] akisRenkleri = new string[3];

        // "FF0000,8C00FF,FFFFFF"
        public string AkisRenkleri
        {
            get { return string.Join(",", akisRenkleri); }
        }

        public event Action Degisti;

        // Gömülü (ana penceredeki Işık sekmesi): başlık ve LED şeridi yok, KAYDET / GERİ AL "Bitti" olayıyla döner
        private readonly bool gomulu;
        private readonly int ox = 16, oy = 124;
        public event Action<bool> Bitti;

        // Önizlemeyi başka bir kontrol gösterir (ör. Işık sekmesindeki klavye demosu)
        internal LedOnizleme Onizleme
        {
            set { ledSeridi = value; OnizlemeGuncelle(); }
        }

        private void Sonuc(bool kaydet)
        {
            if (!gomulu) { DialogResult = kaydet ? DialogResult.OK : DialogResult.Cancel; return; }
            Action<bool> h = Bitti;
            if (h != null) h(kaydet);
        }

        // ---------- Durum ----------
        private int akisSirasi;            // akışta düzenlenen renk (0-2)
        private float ton, doygunluk = 1f; // spektrumda seçili nokta (renk tonu 0-360, doygunluk 0-1)

        // ---------- Kontroller ----------
        private readonly YuvarlakPanel[] modPaneller = new YuvarlakPanel[Modlar.GetLength(0)];
        private readonly Label[] modYazilar = new Label[Modlar.GetLength(0)];

        private YuvarlakPanel solKart;
        private Label lblRenkBaslik, lblRenkUyari, lblSonRenkler;
        private YuvarlakPanel onizleme, hexKutu;
        private TextBox txtHex;
        private readonly YuvarlakPanel[] akisPaneller = new YuvarlakPanel[3];
        private RenkIzgarasi palet, sonRenkler;
        private RenkSpektrumu spektrum;
        private Kaydirici parlaklikCubugu;      // rengin parlaklığı (HSV "V")
        private Kaydirici ledParlaklik, ledHiz;
        private Label lblParlaklik, lblHiz, lblHizBaslik;
        private LedOnizleme ledSeridi;
        private Timer bekle;
        private bool guncelleniyor;

        // =================================================================
        public LedAyarForm(string mod, string renkHex, int parlaklik, int hiz, string akis, bool gomulu = false)
        {
            this.gomulu = gomulu;
            if (gomulu)
            {
                ox = 0;
                oy = 0;
                TopLevel = false;
            }
            Mod = mod;
            RenkHex = HexTemizle(renkHex, "0066FF");
            Parlaklik = Math.Max(5, Math.Min(100, parlaklik));
            Hiz = Math.Max(HizEnAz, Math.Min(HizEnCok, hiz));

            string[] varsayilan = VarsayilanAkis.Split(',');
            string[] gelen = (akis ?? "").Split(',');
            for (int i = 0; i < 3; i++)
                akisRenkleri[i] = HexTemizle(i < gelen.Length ? gelen[i] : null, varsayilan[i]);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = gomulu ? new Size(768, 500) : new Size(800, 640);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            KeyPreview = true;

            // Spektrum / kaydırıcı sürüklenirken çok sık olay gelir: kısa beklemeyle tek önizleme
            bekle = new Timer { Interval = 120 };
            bekle.Tick += (s, e) => { bekle.Stop(); Bildir(); };

            if (!gomulu)
            {
                BaslikOlustur();
                OnizlemeOlustur();
            }
            RenkPaneliOlustur();
            SagPaneliOlustur();

            ModStilleri();
            RenkPaneliniGuncelle(true);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && !gomulu) Sonuc(false);
            };
            MouseDown += SurukleBaslat;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            bekle.Stop();
            base.OnFormClosed(e);
        }

        // "#ff0000" -> "FF0000"; geçersizse varsayılan
        private static string HexTemizle(string hex, string varsayilan)
        {
            hex = (hex ?? "").Trim().TrimStart('#').ToUpperInvariant();
            int sayi;
            return hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out sayi)
                ? hex : varsayilan;
        }

        // ---------- Zemin ve köşeler ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Tema.ZeminBoya(e.Graphics, this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (win11)
            {
                int tercih = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref tercih, sizeof(int));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (!win11 && !gomulu && Width > 0 && Height > 0)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(0, 0, Width, Height), 14))
                    Region = new Region(yol);
            }
        }

        private void SurukleBaslat(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !gomulu)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        // ---------- Başlık ----------
        private void BaslikOlustur()
        {
            Panel baslik = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Tema.Baslik };

            Label ikon = new Label
            {
                Text = "",
                Font = new Font("Segoe MDL2 Assets", 13f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };
            Label yazi = new Label
            {
                Text = "LED Ayarları",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(44, 12),
                BackColor = Color.Transparent
            };
            Label kapat = new Label
            {
                Text = "✕",
                Size = new Size(36, 32),
                Location = new Point(ClientSize.Width - 44, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11f),
                Cursor = Cursors.Hand
            };
            kapat.MouseEnter += (s, e) => kapat.BackColor = Color.FromArgb(232, 17, 35);
            kapat.MouseLeave += (s, e) => kapat.BackColor = Color.Transparent;
            kapat.Click += (s, e) => Sonuc(false);

            baslik.Controls.Add(ikon);
            baslik.Controls.Add(yazi);
            baslik.Controls.Add(kapat);

            baslik.MouseDown += SurukleBaslat;
            ikon.MouseDown += SurukleBaslat;
            yazi.MouseDown += SurukleBaslat;

            Controls.Add(baslik);
        }

        // ---------- Ortak yardımcılar ----------
        private static YuvarlakPanel Kart(Control ebeveyn, int x, int y, int w, int h)
        {
            YuvarlakPanel k = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                Yaricap = 16,
                DolguRengi = Tema.Panel,
                KenarRengi = Tema.PanelKenar
            };
            ebeveyn.Controls.Add(k);
            return k;
        }

        private static Label Yazi(Control ebeveyn, string metin, int x, int y, float boyut, bool kalin, Color renk)
        {
            Label l = new Label
            {
                Text = metin,
                Font = new Font("Segoe UI", boyut, kalin ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = renk,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(x, y)
            };
            ebeveyn.Controls.Add(l);
            return l;
        }

        // Tıklanabilir yuvarlak panel + alt kontroller için ortak hover / tıklama
        private static void Bagla(YuvarlakPanel p, Action tik)
        {
            EventHandler gir = (s, e) => p.Hover = true;
            EventHandler cik = (s, e) => p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));
            EventHandler bas = (s, e) => tik();

            p.MouseEnter += gir; p.MouseLeave += cik; p.Click += bas;
            foreach (Control c in p.Controls)
            {
                c.MouseEnter += gir; c.MouseLeave += cik; c.Click += bas;
            }
        }

        // =================================================================
        //  SOL: RENK PANELİ
        // =================================================================
        private void RenkPaneliOlustur()
        {
            solKart = Kart(this, ox, oy, 324, 500);
            solKart.MouseDown += SurukleBaslat;

            lblRenkBaslik = Yazi(solKart, "Renk", 14, 12, 10.5f, true, Lacivert);

            // --- Üst satır: önizleme + renk kodu (tek renk) ya da akışın 3 rengi ---
            onizleme = new YuvarlakPanel
            {
                Location = new Point(14, 40),
                Size = new Size(56, 36),
                Yaricap = 10,
                KenarRengi = Kenar
            };
            solKart.Controls.Add(onizleme);

            hexKutu = new YuvarlakPanel
            {
                Location = new Point(78, 40),
                Size = new Size(116, 36),
                Yaricap = 10,
                DolguRengi = Tema.Kart,
                KenarRengi = Kenar
            };
            txtHex = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Font = new Font("Consolas", 11f, FontStyle.Bold),
                Location = new Point(12, 9),
                Width = 94,
                MaxLength = 7,
                CharacterCasing = CharacterCasing.Upper
            };
            txtHex.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                HexUygula();
            };
            txtHex.Leave += (s, e) => HexUygula();
            hexKutu.Controls.Add(txtHex);
            hexKutu.Click += (s, e) => txtHex.Focus();
            solKart.Controls.Add(hexKutu);

            for (int i = 0; i < 3; i++)
            {
                int sira = i;
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(14 + i * 64, 40),
                    Size = new Size(56, 36),
                    Yaricap = 10,
                    Cursor = Cursors.Hand,
                    Visible = false
                };
                p.Click += (s, e) =>
                {
                    akisSirasi = sira;
                    RenkPaneliniGuncelle(true);
                };
                akisPaneller[i] = p;
                solKart.Controls.Add(p);
            }

            lblRenkUyari = new Label
            {
                Text = "Bu efekt kendi renklerini kullanır.\nRenk seçmek için Özel renk, Nefes, Kuyruklu yıldız gibi bir efekt seç.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                Location = new Point(14, 38),
                Size = new Size(296, 40),
                Visible = false
            };
            solKart.Controls.Add(lblRenkUyari);

            // --- Windows renk penceresindeki 48 temel renk ---
            palet = new RenkIzgarasi
            {
                Location = new Point(14, 88),
                Renkler = TemelRenkler.Select(HexRenk).ToArray()
            };
            palet.BoyutuAyarla(6);
            palet.RenkSecildi += r => RenkUygula(r, true);
            solKart.Controls.Add(palet);

            // --- Son kullanılan renkler ---
            lblSonRenkler = Yazi(solKart, "SON KULLANILANLAR", 14, palet.Bottom + 10, 8f, true, SolukYazi);
            sonRenkler = new RenkIzgarasi
            {
                Location = new Point(14, palet.Bottom + 30),
                Renkler = SonRenkleriOku()
            };
            sonRenkler.BoyutuAyarla(1);
            sonRenkler.RenkSecildi += r => RenkUygula(r, true);
            solKart.Controls.Add(sonRenkler);

            // --- Renk spektrumu (ton x doygunluk) + parlaklık çubuğu ---
            spektrum = new RenkSpektrumu
            {
                Location = new Point(14, sonRenkler.Bottom + 12),
                Size = new Size(296, 84)
            };
            spektrum.Degisti += () =>
            {
                ton = spektrum.Ton;
                doygunluk = spektrum.Doygunluk;
                SpektrumdanRenk();
            };
            solKart.Controls.Add(spektrum);

            parlaklikCubugu = new Kaydirici
            {
                Location = new Point(14, spektrum.Bottom + 10),
                Size = new Size(296, 22),
                EnAz = 0,
                EnCok = 100,
                Deger = 100,
                Gradyan = true
            };
            parlaklikCubugu.Degisti += () => SpektrumdanRenk();
            solKart.Controls.Add(parlaklikCubugu);
        }

        private bool RenkAktif
        {
            get { return Array.IndexOf(RenkliModlar, Mod) >= 0; }
        }

        private bool AkisSecili
        {
            get { return Mod == AkisModu; }
        }

        // Şu an düzenlenen renk (tek renk ya da akışın seçili rengi)
        private string AktifHex
        {
            get { return AkisSecili ? akisRenkleri[akisSirasi] : RenkHex; }
            set
            {
                if (AkisSecili) akisRenkleri[akisSirasi] = value;
                else RenkHex = value;
            }
        }

        private static string Hex(Color c)
        {
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        // Paletten / son kullanılanlardan / renk kodundan gelen renk
        private void RenkUygula(Color renk, bool hemenBildir)
        {
            if (!RenkAktif && !AkisSecili) return;

            AktifHex = Hex(renk);
            RenkPaneliniGuncelle(true);

            if (hemenBildir) Bildir();
        }

        private void HexUygula()
        {
            if (guncelleniyor) return;

            string hex = HexTemizle(txtHex.Text, null);
            if (hex == null)
            {
                txtHex.Text = "#" + AktifHex;   // geçersiz: eskisine dön
                return;
            }
            if (hex != AktifHex) RenkUygula(HexRenk(hex), true);
        }

        // Spektrum ya da parlaklık çubuğu değişti
        private void SpektrumdanRenk()
        {
            if (guncelleniyor || (!RenkAktif && !AkisSecili)) return;

            AktifHex = Hex(HsvRenk(ton, doygunluk, parlaklikCubugu.Deger / 100f));
            RenkPaneliniGuncelle(false);

            bekle.Stop();
            bekle.Start();
        }

        // spektrumuGuncelle: renk dışarıdan geldiyse spektrum / çubuk da o renge gelsin
        private void RenkPaneliniGuncelle(bool spektrumuGuncelle)
        {
            guncelleniyor = true;
            try
            {
                bool tek = RenkAktif, akis = AkisSecili;
                bool etkin = tek || akis;

                lblRenkBaslik.Text = akis ? "Akışın renkleri" : "Renk";
                lblRenkBaslik.ForeColor = etkin ? Lacivert : Tema.Pasif;

                onizleme.Visible = hexKutu.Visible = tek;
                lblRenkUyari.Visible = !etkin;

                for (int i = 0; i < 3; i++)
                {
                    akisPaneller[i].Visible = akis;
                    akisPaneller[i].DolguRengi = HexRenk(akisRenkleri[i]);
                    akisPaneller[i].KenarRengi = i == akisSirasi ? Mavi : Kenar;
                    akisPaneller[i].KenarKalinligi = i == akisSirasi ? 3 : 1;
                    akisPaneller[i].Invalidate();
                }

                // Akışta seçili rengin kodu da düzenlenebilsin: kutu sağda durur
                hexKutu.Visible = etkin;
                hexKutu.Location = new Point(akis ? 14 + 3 * 64 : 78, 40);
                hexKutu.Width = akis ? 296 - 3 * 64 : 116;

                if (etkin)
                {
                    Color renk = HexRenk(AktifHex);
                    onizleme.DolguRengi = renk;
                    onizleme.Invalidate();
                    txtHex.Text = "#" + AktifHex;

                    palet.Secili = renk;
                    sonRenkler.Secili = renk;

                    if (spektrumuGuncelle)
                    {
                        float h, s, v;
                        HsvYap(renk, out h, out s, out v);
                        if (s > 0.01f && v > 0.01f) ton = h;          // gri / siyahta ton kaybolmasın
                        if (v > 0.01f) doygunluk = s;
                        spektrum.Ayarla(ton, doygunluk);
                        parlaklikCubugu.Deger = (int)Math.Round(v * 100);
                    }
                    parlaklikCubugu.SonRenk = HsvRenk(ton, doygunluk, 1f);
                }

                palet.Enabled = sonRenkler.Enabled = spektrum.Enabled = parlaklikCubugu.Enabled = etkin;
                lblSonRenkler.ForeColor = etkin ? SolukYazi : Tema.Pasif;
                palet.Invalidate();
                sonRenkler.Invalidate();
                spektrum.Invalidate();
                parlaklikCubugu.Invalidate();
            }
            finally
            {
                guncelleniyor = false;
            }

            OnizlemeGuncelle();
        }

        // ---------- Üst: canlı önizleme ----------
        private void OnizlemeOlustur()
        {
            YuvarlakPanel kart = Kart(this, 16, 56, 768, 60);
            kart.MouseDown += SurukleBaslat;

            Yazi(kart, "Önizleme", 14, 20, 9.5f, true, SolukYazi);

            ledSeridi = new LedOnizleme
            {
                Location = new Point(96, 8),
                Size = new Size(658, 44)
            };
            kart.Controls.Add(ledSeridi);
        }

        private void OnizlemeGuncelle()
        {
            if (ledSeridi == null) return;
            ledSeridi.Ayarla(Mod, HexRenk(RenkHex), akisRenkleri.Select(HexRenk).ToArray(), Parlaklik, Hiz);
        }

        // ---------- Son kullanılan renkler (ayarlar.ini) ----------
        private static Color[] SonRenkleriOku()
        {
            Color[] r = new Color[8];
            string[] liste = UygulamaAyarlari.SonLedRenkleri.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < r.Length; i++)
                r[i] = i < liste.Length && HexTemizle(liste[i], null) != null ? HexRenk(liste[i]) : Color.Empty;
            return r;
        }

        private void SonRenkleriKaydet()
        {
            List<string> yeni = new List<string>();
            if (RenkAktif) yeni.Add(RenkHex);
            if (AkisSecili) yeni.AddRange(akisRenkleri);

            if (yeni.Count == 0) return;

            List<string> eski = UygulamaAyarlari.SonLedRenkleri.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            List<string> birlesik = yeni.Concat(eski).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

            try { UygulamaAyarlari.SonLedRenkleri = string.Join(",", birlesik); }
            catch (Exception) { }
        }

        // =================================================================
        //  SAĞ: EFEKTLER, PARLAKLIK, HIZ, KAYDET
        // =================================================================
        private void SagPaneliOlustur()
        {
            YuvarlakPanel kart = Kart(this, ox + 336, oy, 432, 500);
            kart.MouseDown += SurukleBaslat;

            Yazi(kart, "Efekt", 14, 12, 10.5f, true, Lacivert);

            for (int i = 0; i < Modlar.GetLength(0); i++)
            {
                int indeks = i;

                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(14 + (i % 3) * 138, 40 + (i / 3) * 40),
                    Size = new Size(130, 34),
                    Yaricap = 12,
                    Cursor = Cursors.Hand
                };
                Label l = new Label
                {
                    Text = Modlar[i, 1],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                p.Controls.Add(l);

                Bagla(p, () =>
                {
                    Mod = Modlar[indeks, 0];
                    ModStilleri();
                    RenkPaneliniGuncelle(true);
                    Bildir();
                });

                modPaneller[i] = p;
                modYazilar[i] = l;
                kart.Controls.Add(p);
            }

            // --- Parlaklık (sol) ve hız (sağ) ---
            int y = 40 + 8 * 40 + 8;
            Yazi(kart, "Parlaklık", 14, y, 10.5f, true, Lacivert);
            lblHizBaslik = Yazi(kart, "Hız", 222, y, 10.5f, true, Lacivert);

            ledParlaklik = KaydiriciKarti(kart, 14, y + 28, 5, 100, Parlaklik, out lblParlaklik);
            lblParlaklik.Text = Parlaklik + "%";
            ledParlaklik.Degisti += () =>
            {
                Parlaklik = ledParlaklik.Deger;
                lblParlaklik.Text = Parlaklik + "%";
                OnizlemeGuncelle();
                bekle.Stop();
                bekle.Start();
            };

            ledHiz = KaydiriciKarti(kart, 222, y + 28, HizEnAz, HizEnCok, Hiz, out lblHiz);
            lblHiz.Text = HizYazisi(Hiz);
            ledHiz.Degisti += () =>
            {
                // 100'e yakınken "normal"e otursun
                int v = ledHiz.Deger;
                if (Math.Abs(v - 100) <= 6 && v != 100) { v = 100; ledHiz.Deger = 100; }

                Hiz = v;
                lblHiz.Text = HizYazisi(Hiz);
                OnizlemeGuncelle();
                bekle.Stop();
                bekle.Start();
            };

            // --- İPTAL / KAYDET ---
            kart.Controls.Add(Dugme(gomulu ? "GERİ AL" : "İPTAL", 204, 448, 104, 40, false, () => Sonuc(false)));
            kart.Controls.Add(Dugme("KAYDET", 314, 448, 104, 40, true, () =>
            {
                SonRenkleriKaydet();
                Sonuc(true);
            }));
        }

        private static string HizYazisi(int hiz)
        {
            return hiz == 100 ? "Normal" : "%" + hiz;
        }

        private Kaydirici KaydiriciKarti(Control ebeveyn, int x, int y, int enAz, int enCok, int deger, out Label etiket)
        {
            YuvarlakPanel kart = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(196, 42),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Kenar
            };

            Kaydirici k = new Kaydirici
            {
                Location = new Point(10, 10),
                Size = new Size(122, 22),
                EnAz = enAz,
                EnCok = enCok,
                Deger = deger,
                ZeminRengi = Tema.Kart
            };

            etiket = new Label
            {
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Lacivert,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(132, 0),
                Size = new Size(62, 42)
            };

            kart.Controls.Add(k);
            kart.Controls.Add(etiket);
            ebeveyn.Controls.Add(kart);
            return k;
        }

        private void ModStilleri()
        {
            for (int i = 0; i < Modlar.GetLength(0); i++)
            {
                bool aktif = (Modlar[i, 0] == Mod);
                modPaneller[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                modPaneller[i].KenarRengi = aktif ? Mavi : Kenar;
                modPaneller[i].HoverRengi = aktif ? Color.Empty : Tema.Hover;
                modYazilar[i].ForeColor = aktif ? Color.White : Lacivert;
                modPaneller[i].Invalidate(true);
            }

            // Hız: hareketsiz modlarda kullanılmaz
            if (ledHiz != null)
            {
                bool hareketli = Array.IndexOf(SabitModlar, Mod) < 0;
                ledHiz.Enabled = hareketli;
                lblHiz.ForeColor = hareketli ? Lacivert : Tema.Pasif;
                lblHizBaslik.ForeColor = hareketli ? Lacivert : Tema.Pasif;
                ledHiz.Invalidate();
            }
        }

        private YuvarlakPanel Dugme(string yazi, int x, int y, int w, int h, bool ana, Action tik)
        {
            YuvarlakPanel p = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                Yaricap = 12,
                DolguRengi = ana ? Mavi : Tema.Kart,
                KenarRengi = ana ? Color.Transparent : Tema.Kenar2,
                HoverRengi = ana ? MaviKoyu : Tema.Hover,
                Cursor = Cursors.Hand
            };
            Label l = new Label
            {
                Text = yazi,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = ana ? Color.White : Lacivert,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            p.Controls.Add(l);
            Bagla(p, tik);
            return p;
        }

        private void Bildir()
        {
            Action h = Degisti;
            if (h != null) h();
        }

        // =================================================================
        //  RENK DÖNÜŞÜMLERİ
        // =================================================================
        private static Color HexRenk(string hex)
        {
            try
            {
                return Color.FromArgb(
                    Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16));
            }
            catch (Exception)
            {
                return Mavi;
            }
        }

        // ton 0-360, doygunluk ve parlaklık 0-1
        internal static Color HsvRenk(float h, float s, float v)
        {
            h = ((h % 360) + 360) % 360;
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = v - c;
            float r, g, b;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(
                (int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
        }

        internal static void HsvYap(Color renk, out float h, out float s, out float v)
        {
            float r = renk.R / 255f, g = renk.G / 255f, b = renk.B / 255f;
            float enCok = Math.Max(r, Math.Max(g, b)), enAz = Math.Min(r, Math.Min(g, b));
            float fark = enCok - enAz;

            v = enCok;
            s = enCok <= 0 ? 0 : fark / enCok;

            if (fark <= 0) h = 0;
            else if (enCok == r) h = 60 * (((g - b) / fark) % 6);
            else if (enCok == g) h = 60 * ((b - r) / fark + 2);
            else h = 60 * ((r - g) / fark + 4);

            if (h < 0) h += 360;
        }
    }

    // =====================================================================
    //  Renk ızgarası: yuvarlak köşeli renk kutuları (palet / son kullanılanlar)
    // =====================================================================
    internal class RenkIzgarasi : Control
    {
        public Color[] Renkler = new Color[0];
        public Color Secili = Color.Empty;
        public int Sutun = 8, Hucre = 32, Ara = 5;
        public event Action<Color> RenkSecildi;

        private int hover = -1;

        public RenkIzgarasi()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public void BoyutuAyarla(int satir)
        {
            Size = new Size(Sutun * Hucre + (Sutun - 1) * Ara, satir * Hucre + (satir - 1) * Ara);
        }

        private Rectangle Kutu(int i)
        {
            return new Rectangle((i % Sutun) * (Hucre + Ara), (i / Sutun) * (Hucre + Ara), Hucre - 1, Hucre - 1);
        }

        private int Bul(Point p)
        {
            for (int i = 0; i < Renkler.Length; i++)
                if (Kutu(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = Bul(e.Location);
            if (i != hover) { hover = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = Bul(e.Location);
            if (i < 0 || Renkler[i].IsEmpty) return;

            Action<Color> h = RenkSecildi;
            if (h != null) h(Renkler[i]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < Renkler.Length; i++)
            {
                Rectangle r = Kutu(i);
                Color c = Renkler[i];

                using (GraphicsPath yol = YuvarlakPanel.Yol(r, 6))
                {
                    if (c.IsEmpty)
                    {
                        // Boş yuva: kesikli çerçeve
                        using (Pen k = new Pen(Tema.Kenar, 1f) { DashStyle = DashStyle.Dash })
                            g.DrawPath(k, yol);
                        continue;
                    }

                    using (SolidBrush b = new SolidBrush(c))
                        g.FillPath(b, yol);
                    using (Pen k = new Pen(Color.FromArgb(70, 0, 0, 0)))
                        g.DrawPath(k, yol);
                }

                bool secili = !Secili.IsEmpty && c.ToArgb() == Secili.ToArgb();
                if (Enabled && (secili || i == hover))
                {
                    Rectangle halka = Rectangle.Inflate(r, 2, 2);
                    using (GraphicsPath yol = YuvarlakPanel.Yol(halka, 7))
                    using (Pen k = new Pen(secili ? Tema.Yazi : Tema.Pasif, 2f))
                        g.DrawPath(k, yol);
                }
            }

            if (!Enabled)
                using (SolidBrush ort = new SolidBrush(Color.FromArgb(170, Tema.Panel)))
                    g.FillRectangle(ort, ClientRectangle);
        }
    }

    // =====================================================================
    //  Renk spektrumu: yatayda renk tonu, dikeyde doygunluk (üst = canlı, alt = beyaza yakın)
    // =====================================================================
    internal class RenkSpektrumu : Control
    {
        public float Ton { get; private set; }
        public float Doygunluk { get; private set; }
        public event Action Degisti;

        private Bitmap zemin;
        private bool suruklen;

        public RenkSpektrumu()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Cross;
            Doygunluk = 1f;
        }

        public void Ayarla(float ton, float doygunluk)
        {
            Ton = ton;
            Doygunluk = doygunluk;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (zemin != null) { zemin.Dispose(); zemin = null; }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && zemin != null) zemin.Dispose();
            base.Dispose(disposing);
        }

        private void ZeminOlustur()
        {
            int w = Math.Max(1, Width), h = Math.Max(1, Height);
            zemin = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            BitmapData veri = zemin.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int[] pikseller = new int[w * h];

            for (int y = 0; y < h; y++)
            {
                float s = 1f - y / (float)Math.Max(1, h - 1);
                for (int x = 0; x < w; x++)
                    pikseller[y * w + x] = LedAyarForm.HsvRenk(x * 360f / w, s, 1f).ToArgb();
            }

            Marshal.Copy(pikseller, 0, veri.Scan0, pikseller.Length);
            zemin.UnlockBits(veri);
        }

        private void Sec(Point p)
        {
            float x = Math.Max(0, Math.Min(Width - 1, p.X));
            float y = Math.Max(0, Math.Min(Height - 1, p.Y));

            Ton = x * 360f / Width;
            Doygunluk = 1f - y / Math.Max(1, Height - 1);
            Invalidate();

            Action h = Degisti;
            if (h != null) h();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            suruklen = true;
            Capture = true;
            Sec(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (suruklen) Sec(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            suruklen = false;
            Capture = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            if (zemin == null) ZeminOlustur();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath yol = YuvarlakPanel.Yol(new RectangleF(0, 0, Width - 1, Height - 1), 8))
            {
                g.SetClip(yol);
                g.DrawImageUnscaled(zemin, 0, 0);
                g.ResetClip();
                using (Pen k = new Pen(Tema.Kenar))
                    g.DrawPath(k, yol);
            }

            // Seçili nokta
            float mx = Ton / 360f * Width, my = (1f - Doygunluk) * (Height - 1);
            using (Pen dis = new Pen(Color.Black, 3f))
            using (Pen ic = new Pen(Color.White, 1.5f))
            {
                g.DrawEllipse(dis, mx - 6, my - 6, 12, 12);
                g.DrawEllipse(ic, mx - 6, my - 6, 12, 12);
            }

            if (!Enabled)
                using (SolidBrush ort = new SolidBrush(Color.FromArgb(170, Tema.Panel)))
                    g.FillRectangle(ort, ClientRectangle);
        }
    }

    // =====================================================================
    //  Kaydırıcı: temaya uygun, kendi çizdiğimiz (TrackBar yerine).
    //  Gradyan = true ise iz siyahtan SonRenk'e geçer (rengin parlaklığı için).
    // =====================================================================
    internal class Kaydirici : Control
    {
        private int deger;
        public int EnAz = 0, EnCok = 100;
        public bool Gradyan;
        public Color SonRenk = Color.White;
        public Color ZeminRengi = Color.Transparent;
        public event Action Degisti;

        private bool suruklen;

        public int Deger
        {
            get { return deger; }
            set { deger = Math.Max(EnAz, Math.Min(EnCok, value)); Invalidate(); }
        }

        public Kaydirici()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        private const int Pay = 9;   // tutamağın yarıçapı kadar kenar boşluğu

        private void Sec(int x)
        {
            float oran = (x - Pay) / (float)Math.Max(1, Width - 2 * Pay);
            int yeni = EnAz + (int)Math.Round(Math.Max(0, Math.Min(1, oran)) * (EnCok - EnAz));
            if (yeni == deger) return;

            Deger = yeni;
            Action h = Degisti;
            if (h != null) h();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            suruklen = true;
            Capture = true;
            Sec(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (suruklen) Sec(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            suruklen = false;
            Capture = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled) return;

            int adim = Math.Max(1, (EnCok - EnAz) / 20);
            int eski = deger;
            Deger = deger + (e.Delta > 0 ? adim : -adim);
            if (deger != eski)
            {
                Action h = Degisti;
                if (h != null) h();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (ZeminRengi != Color.Transparent)
                using (SolidBrush z = new SolidBrush(ZeminRengi))
                    g.FillRectangle(z, ClientRectangle);

            float oran = (deger - EnAz) / (float)Math.Max(1, EnCok - EnAz);
            float izY = Height / 2f;
            float izH = Gradyan ? 12 : 6;
            RectangleF iz = new RectangleF(Pay, izY - izH / 2, Width - 2 * Pay, izH);
            float tx = Pay + oran * iz.Width;

            using (GraphicsPath yol = YuvarlakPanel.Yol(iz, izH / 2))
            {
                if (Gradyan)
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(
                               new RectangleF(iz.X - 1, iz.Y, iz.Width + 2, iz.Height), Color.Black, SonRenk, 0f))
                        g.FillPath(b, yol);
                    using (Pen k = new Pen(Tema.Kenar))
                        g.DrawPath(k, yol);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Tema.Hover))
                        g.FillPath(b, yol);

                    if (tx - iz.X >= izH)
                        using (GraphicsPath dolu = YuvarlakPanel.Yol(new RectangleF(iz.X, iz.Y, tx - iz.X, izH), izH / 2))
                        using (SolidBrush b = new SolidBrush(Enabled ? Color.FromArgb(16, 104, 232) : Tema.Pasif))
                            g.FillPath(b, dolu);
                }
            }

            // Tutamak
            float r = 8;
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, tx - r, izY - r, 2 * r, 2 * r);
            using (Pen k = new Pen(Enabled ? Color.FromArgb(16, 104, 232) : Tema.Pasif, 2.5f))
                g.DrawEllipse(k, tx - r, izY - r, 2 * r, 2 * r);

            if (!Enabled && Gradyan)
                using (SolidBrush ort = new SolidBrush(Color.FromArgb(170, Tema.Panel)))
                    g.FillRectangle(ort, ClientRectangle);
        }
    }

    // =====================================================================
    //  LED önizlemesi: kartın led_animations.py animasyonlarının birebir C# karşılığı.
    //  13 LED çizer; hız %100'de kareler 20 ms arayla ilerler (kartla aynı).
    // =====================================================================
    internal class LedOnizleme : Control
    {
        private const int Adet = 13;

        private static readonly Color Kirmizi = Color.FromArgb(255, 0, 0);
        private static readonly Color Mor = Color.FromArgb(140, 0, 255);
        private static readonly Color Beyaz = Color.FromArgb(255, 255, 255);
        private static readonly Color Siyah = Color.FromArgb(0, 0, 0);

        private static readonly string[] SabitModlar =
            { "white", "red", "green", "blue", "purple", "off", "color", "random" };

        protected const int LedSayisi = Adet;
        protected readonly Color[] led = new Color[Adet];
        private readonly float[] duzey = new float[Adet];     // pırıltı / tarayıcı
        private readonly Color[] tampon = new Color[Adet];    // konfeti

        // Akım sınırı (mA, 0 = sınırsız): kart 2.8+ gibi, toplam akım aşılırsa parlaklık kısılır.
        // Bir LED tam parlaklıkta renk başına ~20 mA (beyaz ~60 mA), kapalıyken ~1 mA çeker.
        public const int LedMa = 20, BostaMa = 1;
        public int AkimSiniri = 400;

        // Şu anki karenin tahmini akımı (sınır uygulanmadan, kullanıcının parlaklığıyla)
        public int HamAkim
        {
            get
            {
                int toplam = 0;
                for (int i = 0; i < Adet; i++) toplam += led[i].R + led[i].G + led[i].B;
                return (int)Math.Round(Adet * BostaMa + toplam * LedMa / 255.0 * parlaklik);
            }
        }

        // Sınır uygulanınca kartın kullanacağı parlaklık (0-1)
        public float EtkinParlaklik
        {
            get
            {
                if (AkimSiniri <= 0) return parlaklik;
                int toplam = 0;
                for (int i = 0; i < Adet; i++) toplam += led[i].R + led[i].G + led[i].B;
                double ledMa = toplam * LedMa / 255.0;
                double kalan = AkimSiniri - Adet * BostaMa;
                if (ledMa * parlaklik <= kalan) return parlaklik;
                return kalan > 0 ? (float)Math.Max(0.02, kalan / ledMa) : 0.02f;
            }
        }

        // Sınır uygulandıktan sonraki tahmini akım
        public int Akim
        {
            get
            {
                int toplam = 0;
                for (int i = 0; i < Adet; i++) toplam += led[i].R + led[i].G + led[i].B;
                return (int)Math.Round(Adet * BostaMa + toplam * LedMa / 255.0 * EtkinParlaklik);
            }
        }
        private readonly Random rnd = new Random();
        private readonly Timer zaman = new Timer { Interval = 15 };
        private readonly Stopwatch saat = Stopwatch.StartNew();

        private string mod = "";
        private Color renk = Color.FromArgb(0, 102, 255);
        private Color[] akis = { Kirmizi, Mor, Beyaz };
        protected float parlaklik = 1f;
        private double aralik = 20;        // ms / kare
        private double birikim;
        private int kare, gokOfset;

        public LedOnizleme()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(12, 15, 22);
            for (int i = 0; i < Adet; i++) led[i] = tampon[i] = Siyah;

            zaman.Tick += (s, e) => Ilerle();
            zaman.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                zaman.Stop();
                zaman.Dispose();
            }
            base.Dispose(disposing);
        }

        public void Ayarla(string yeniMod, Color yeniRenk, Color[] yeniAkis, int parlaklikYuzde, int hiz)
        {
            // Eski "red_comet" / "red_solid" adları: o efekt o renkle
            string efekt = yeniMod ?? "off";
            string[] parca = efekt.Split(new[] { '_' }, 2);
            if (parca.Length == 2 && (parca[0] == "red" || parca[0] == "purple" || parca[0] == "white") &&
                efekt != "red_purple_white")
            {
                yeniRenk = parca[0] == "red" ? Kirmizi : parca[0] == "purple" ? Mor : Beyaz;
                efekt = parca[1] == "solid" ? parca[0] : parca[1];
            }

            renk = yeniRenk;
            if (yeniAkis != null && yeniAkis.Length >= 2) akis = yeniAkis;
            parlaklik = Math.Max(0.02f, Math.Min(1f, parlaklikYuzde / 100f));
            aralik = 20.0 * 100 / Math.Max(25, Math.Min(300, hiz));

            if (efekt != mod)
            {
                mod = efekt;
                Gir();
            }
            else if (mod == "color")
            {
                Doldur(renk);
            }
            Invalidate();
        }

        // Moda girerken (kartta set_mode)
        private void Gir()
        {
            kare = 0;
            birikim = 0;
            for (int i = 0; i < Adet; i++) { duzey[i] = 0; tampon[i] = Siyah; }

            switch (mod)
            {
                case "white": Doldur(Beyaz); break;
                case "red": Doldur(Kirmizi); break;
                case "green": Doldur(Color.FromArgb(0, 255, 0)); break;
                case "blue": Doldur(Color.FromArgb(0, 0, 255)); break;
                case "purple": Doldur(Mor); break;
                case "color": Doldur(renk); break;
                case "random":
                    for (int i = 0; i < Adet; i++)
                        led[i] = Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256));
                    break;
                default: Doldur(Siyah); break;
            }
        }

        private void Doldur(Color c)
        {
            for (int i = 0; i < Adet; i++) led[i] = c;
        }

        // Geçen süre kadar kare ilerlet (zamanlayıcı 15 ms'den sık çalışamadığı için biriktirilir)
        private void Ilerle()
        {
            double gecen = saat.Elapsed.TotalMilliseconds;
            saat.Restart();

            if (Array.IndexOf(SabitModlar, mod) >= 0) return;

            birikim += gecen;
            int n = 0;
            while (birikim >= aralik && n < 12)
            {
                birikim -= aralik;
                Kare();
                n++;
            }
            if (birikim > aralik * 12) birikim = 0;   // pencere bir süre donduysa yetişmeye çalışma

            if (n > 0) Invalidate();
        }

        protected static Color Olcek(Color c, double k)
        {
            if (k <= 0) return Siyah;
            if (k >= 1) return c;
            return Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
        }

        private static Color Karistir(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Color Tekerlek(int pos)
        {
            pos &= 255;
            if (pos < 85) return Color.FromArgb(255 - pos * 3, pos * 3, 0);
            if (pos < 170) { pos -= 85; return Color.FromArgb(0, 255 - pos * 3, pos * 3); }
            pos -= 170;
            return Color.FromArgb(pos * 3, 0, 255 - pos * 3);
        }

        private static int Sinirla(int v)
        {
            return Math.Max(0, Math.Min(255, v));
        }

        // Kartta update() içindeki bir kare
        private void Kare()
        {
            kare++;

            switch (mod)
            {
                case "breathe":
                    Doldur(Olcek(renk, 0.03 + 0.97 * (Math.Sin(kare / 25.0) + 1) / 2));
                    break;

                case "rainbow":
                    for (int i = 0; i < Adet; i++) led[i] = Tekerlek(i * 256 / Adet + gokOfset);
                    gokOfset = (gokOfset + 1) % 256;
                    break;

                case "wave":
                    for (int i = 0; i < Adet; i++)
                    {
                        double w = (Math.Sin((i + kare / 3.0) * 0.5) + 1) / 2;
                        led[i] = Color.FromArgb(0, (int)(255 * w), (int)(255 * (1 - w)));
                    }
                    break;

                case "fire":
                    for (int i = 0; i < Adet; i++)
                    {
                        int f = rnd.Next(-30, 31);
                        led[i] = Color.FromArgb(Sinirla(200 + f), Sinirla(80 + (int)Math.Floor(f / 2.0)), 0);
                    }
                    break;

                case "comet":
                    {
                        int kuyruk = Math.Min(6, Adet), bas = (kare / 2) % Adet;
                        for (int i = 0; i < Adet; i++)
                        {
                            int d = ((bas - i) % Adet + Adet) % Adet;
                            led[i] = d < kuyruk ? Olcek(renk, Math.Pow(1 - d / (double)kuyruk, 2)) : Siyah;
                        }
                        break;
                    }

                case "scanner":
                    {
                        for (int i = 0; i < Adet; i++) duzey[i] *= 0.6f;
                        int aralikUc = Adet - 1, p = (kare / 3) % (2 * aralikUc);
                        if (p > aralikUc) p = 2 * aralikUc - p;
                        duzey[p] = 1f;
                        for (int i = 0; i < Adet; i++) led[i] = Olcek(renk, duzey[i]);
                        break;
                    }

                case "chase":
                    {
                        int adim = kare / 6;
                        Color sonuk = Olcek(renk, 0.04);
                        for (int i = 0; i < Adet; i++) led[i] = (i + adim) % 3 == 0 ? renk : sonuk;
                        break;
                    }

                case "wipe":
                    {
                        int pos = (kare / 4) % (2 * Adet);
                        for (int i = 0; i < Adet; i++)
                        {
                            bool yanik = pos < Adet ? i <= pos : i > pos - Adet;
                            led[i] = yanik ? renk : Siyah;
                        }
                        break;
                    }

                case "sparkle":
                    for (int i = 0; i < Adet; i++) duzey[i] *= 0.88f;
                    if (rnd.NextDouble() < 0.3) duzey[rnd.Next(Adet)] = 1f;
                    for (int i = 0; i < Adet; i++) led[i] = Olcek(renk, duzey[i]);
                    break;

                case "confetti":
                    for (int i = 0; i < Adet; i++) tampon[i] = Olcek(tampon[i], 0.9);
                    if (rnd.NextDouble() < 0.35) tampon[rnd.Next(Adet)] = Tekerlek(rnd.Next(256));
                    for (int i = 0; i < Adet; i++) led[i] = tampon[i];
                    break;

                case "colorcycle":
                    Doldur(Tekerlek(kare / 2));
                    break;

                case "red_purple_white":
                    {
                        int n = akis.Length;
                        double ofset = kare * 0.02;
                        for (int i = 0; i < Adet; i++)
                        {
                            double pos = (i * n / (double)Adet + ofset) % n;
                            int idx = (int)pos;
                            led[i] = Karistir(akis[idx], akis[(idx + 1) % n], pos - idx);
                        }
                        break;
                    }

                case "police":
                    {
                        int t = kare % 40, yari = Adet / 2;
                        bool kirmiziYanik = t < 20 && (t % 10) < 5;
                        bool maviYanik = t >= 20 && (t % 10) < 5;
                        for (int i = 0; i < Adet; i++)
                            led[i] = i < yari ? (kirmiziYanik ? Kirmizi : Siyah)
                                              : (maviYanik ? Color.FromArgb(0, 0, 255) : Siyah);
                        break;
                    }

                case "heartbeat":
                    {
                        int t = kare % 60;
                        double v1 = Math.Exp(-Math.Pow((t - 4) / 2.5, 2));
                        double v2 = 0.6 * Math.Exp(-Math.Pow((t - 16) / 2.5, 2));
                        Doldur(Olcek(renk, 0.03 + 0.97 * Math.Max(v1, v2)));
                        break;
                    }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath zemin = YuvarlakPanel.Yol(new RectangleF(0, 0, Width - 1, Height - 1), 10))
            using (SolidBrush b = new SolidBrush(BackColor))
                g.FillPath(b, zemin);

            float bosluk = Width / (float)Adet;
            float cap = Math.Min(Height - 16, bosluk - 14);
            float y = (Height - cap) / 2f;

            for (int i = 0; i < Adet; i++)
            {
                Color c = Olcek(led[i], parlaklik);
                float x = i * bosluk + (bosluk - cap) / 2f;
                int guc = Math.Max(c.R, Math.Max(c.G, c.B));

                if (guc < 8)
                {
                    // Sönük LED
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 36, 46)))
                        g.FillEllipse(b, x, y, cap, cap);
                    continue;
                }

                // Hale (parlaklığa göre)
                float hale = cap * 0.55f;
                RectangleF dis = new RectangleF(x - hale, y - hale, cap + 2 * hale, cap + 2 * hale);
                using (GraphicsPath yol = new GraphicsPath())
                {
                    yol.AddEllipse(dis);
                    using (PathGradientBrush pb = new PathGradientBrush(yol))
                    {
                        pb.CenterColor = Color.FromArgb(Math.Min(170, guc * 2 / 3), c);
                        pb.SurroundColors = new[] { Color.FromArgb(0, c) };
                        g.FillEllipse(pb, dis);
                    }
                }

                using (SolidBrush b = new SolidBrush(c))
                    g.FillEllipse(b, x, y, cap, cap);

                // Ortada hafif parlama
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(110, guc / 2), 255, 255, 255)))
                    g.FillEllipse(b, x + cap * 0.28f, y + cap * 0.22f, cap * 0.32f, cap * 0.28f);
            }
        }
    }
}