using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Ayarlar penceresinin ana formdan istedikleri
    public interface IAyarlarSahibi
    {
        bool KlavyeBagli { get; }
        string ProfilMetni();                                   // karttaki ayarlar (bağlı değilse null)
        Task<string> ProfilUygula(string metin);                // null = başarılı
        Task<string> KartSurumu();                              // eski yazılımda null
        Task<string> KartiGuncelle(Dictionary<string, byte[]> paket, Action<string, int> ilerleme);   // null = başarılı
        List<IstatistikSatiri> IstatistikSatirlari();
        List<string> ProfilFarki(string yedekMetni);             // şimdiki hâl -> yedek farkları (bağlı değilse null)
        void IstatistikSifirla();
        Task<string> FabrikaAyarlari();                         // null = başarılı
        string KartAyari(string anahtar);                       // karttaki tek ayar (bağlı değilse / yoksa null)
        Task<string> KartAyarlariniYaz(Dictionary<string, string> degerler, string aciklama, string ayrinti);   // null = başarılı
        Task<Dictionary<string, string>> KartTanilama();        // eski yazılımda / bağlı değilse null
        Task<string> KartYedekSurumu();                         // önceki yazılımın sürümü; yedek yoksa "", eski kartta null
        Task<string> KartiGeriAl(string beklenen, Action<string, int> ilerleme);   // null = başarılı
    }

    // Başlık çubuğundaki dişli ikonuyla açılan ayarlar penceresi.
    // Sekmeler: Profiller (profil + yedek + hazır profiller), Genel (tepsi, Windows ile başlat, tema, her zaman üstte),
    //          Kart (yazılım), İstatistik, Geçmiş (otomatik yedekler), Günlük (olay günlüğü)
    public class AyarlarForm : Form
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

        // ---------- Renkler ----------
        private static readonly Color Mavi = Color.FromArgb(16, 104, 232);
        private static readonly Color MaviKoyu = Color.FromArgb(10, 82, 190);

        private static readonly string[] KartDosyalari = KartPaketi.Dosyalar;

        private readonly IAyarlarSahibi sahip;
        private bool islemde;

        // "Tanıtımı yeniden göster" seçildi: ana pencere bu pencere kapanınca tanıtımı başlatır
        public bool RehberIstendi { get; private set; }

        // Gömülü: ana penceredeki Ayarlar sekmesinde (ayrı pencere değil). Kapatınca KapatIstendi olayı.
        private bool gomulu;
        private Label lblKapatX;
        private YuvarlakPanel btnKapat;
        public event Action KapatIstendi;

        public bool Islemde
        {
            get { return islemde; }
        }

        public void GomuluYap()
        {
            gomulu = true;
            TopLevel = false;
            if (lblKapatX != null) lblKapatX.Visible = false;
            if (btnKapat != null) btnKapat.Visible = false;
            GomuluHazirla();
            OnResize(EventArgs.Empty);
        }

        private void Kapat()
        {
            if (!gomulu) { Close(); return; }
            Action h = KapatIstendi;
            if (h != null) h();
        }

        // ---------- Sekmeler ----------
        private readonly string[] sekmeAdlari = { "Profiller", "Genel", "Kart", "İstatistik", "Geçmiş", "Günlük", "Uygulama" };
        private readonly YuvarlakPanel[] sekmeDugmeleri = new YuvarlakPanel[7];
        private readonly Label[] sekmeYazilari = new Label[7];
        private readonly Panel[] sayfalar = new Panel[7];
        private int aktifSekme = -1;

        // ---------- Profiller ----------
        private TextBox txtAd;
        private Label lblAdIpucu, lblBos;
        private AkiciListe liste;
        private List<string> profiller = new List<string>();

        // ---------- Kart ----------
        private Label lblKartSurum, lblKlasorSurum, lblKlasor, lblInternetSurum;
        private YuvarlakPanel btnInternet;
        private UzaktakiSurum internetSurumu;
        private Panel ilerlemeDolgu;
        private YuvarlakPanel ilerlemeCubugu;

        // ---------- İstatistik ----------
        private CiftTamponPanel istatistikAlani;
        private List<IstatistikSatiri> istatistik = new List<IstatistikSatiri>();

        // ---------- Geçmiş ----------
        private AkiciListe gecmisListe;
        private Label lblGecmisBos, lblFarkBaslik;
        private TextBox txtFark;
        private List<GecmisOgesi> gecmis = new List<GecmisOgesi>();

        // ---------- Günlük ----------
        private AkiciListe gunlukListe;
        private Label lblGunlukBos;
        private List<GunlukOgesi> gunluk = new List<GunlukOgesi>();
        private readonly ToolTip gunlukIpucu = new ToolTip { InitialDelay = 400, ReshowDelay = 100 };
        private int gunlukIpucuSatiri = -1;

        private Label lblDurum;

        private readonly Font adFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        private readonly Font etiketFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private readonly Font kucukFont = new Font("Segoe UI", 8.5f);

        private class AkiciListe : ListBox
        {
            public AkiciListe() { DoubleBuffered = true; }
        }

        private class CiftTamponPanel : Panel
        {
            public CiftTamponPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        // =================================================================
        public AyarlarForm(IAyarlarSahibi sahibi, bool gomuluAcilsin = false)
        {
            sahip = sahibi;
            gomulu = gomuluAcilsin;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 660);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            KeyPreview = true;

            BaslikOlustur();
            SekmeleriOlustur();

            ProfilSayfasiOlustur(sayfalar[0]);
            GenelSayfasiOlustur(sayfalar[1]);
            KartSayfasiOlustur(sayfalar[2]);
            IstatistikSayfasiOlustur(sayfalar[3]);
            GecmisSayfasiOlustur(sayfalar[4]);
            GunlukSayfasiOlustur(sayfalar[5]);
            UygulamaSayfasiOlustur(sayfalar[6]);

            AltOlustur();
            if (gomulu) ProfilSekmesiniKaldir();   // profiller, yedek, hazır profiller: ana penceredeki soldaki panelde
            AramaDiziniOlustur();

            ListeyiYenile();
            SekmeSec(gomulu ? 1 : 0);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F && e.Control)
                {
                    txtAra.Focus();
                    txtAra.SelectAll();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode != Keys.Escape || islemde) return;
                if (txtAra.Text.Length > 0 || txtAra.Focused)
                {
                    txtAra.Text = "";           // önce arama kapanır, ikinci Esc pencereyi kapatır
                    ActiveControl = null;
                    e.SuppressKeyPress = true;
                    return;
                }
                Kapat();
            };
            MouseDown += SurukleBaslat;
            FormClosing += (s, e) => { if (islemde) e.Cancel = true; };   // karta yazarken kapanmasın
        }

        // ---------- Zemin ve köşeler ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (gomulu)
            {
                // ana penceredeki kart: panel renginde, kenarı yuvarlak (Region)
                using (SolidBrush b = new SolidBrush(Tema.Panel)) e.Graphics.FillRectangle(b, ClientRectangle);
                return;
            }
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

            if (gomulu && sayfaYerlesimi != null) GomuluYerlestir();

            if ((!win11 || gomulu) && Width > 0 && Height > 0)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(0, 0, Width, Height), gomulu ? 20 : 14))
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
                Text = "\uE713",
                Font = new Font("Segoe MDL2 Assets", 13f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };
            Label yazi = new Label
            {
                Text = "Ayarlar",
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
                Location = new Point(460 - 44, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11f),
                Cursor = Cursors.Hand
            };
            kapat.MouseEnter += (s, e) => kapat.BackColor = Color.FromArgb(232, 17, 35);
            kapat.MouseLeave += (s, e) => kapat.BackColor = Color.Transparent;
            kapat.Click += (s, e) => Kapat();
            lblKapatX = kapat;

            baslik.Controls.Add(ikon);
            baslik.Controls.Add(yazi);
            baslik.Controls.Add(kapat);
            AramaKutusuOlustur(baslik);

            baslik.MouseDown += SurukleBaslat;
            ikon.MouseDown += SurukleBaslat;
            yazi.MouseDown += SurukleBaslat;

            Controls.Add(baslik);
        }

        // =================================================================
        //  AYARLARDA ARAMA: başlıktaki kutuya yaz, sonuca tıkla -> o sekme açılır, ayar kısa süre parlar.
        //  Dizin sayfalardaki kalın yazılardan (başlıklar, ayar adları, düğmeler) kendiliğinden çıkarılır;
        //  pencerelerin içindeki ayarlar (ör. uyku engelleyici) için ek kelimeler aşağıda.
        // =================================================================
        private class AramaOgesi
        {
            public string Ad, Baglam, Kelimeler, Ipucu;
            public int Sekme;
            public Control Hedef;   // parlatılacak denetim
            public Label Kaynak;    // adı veren yazı (yazı değişirse güncel adı kullanılır)
        }

        private readonly List<AramaOgesi> aramaDizini = new List<AramaOgesi>();
        private readonly List<AramaOgesi> aramaSonuclari = new List<AramaOgesi>();
        private TextBox txtAra;
        private Label lblAraIpucu;
        private YuvarlakPanel araSonucKutusu;
        private const int AraSatir = 34, AraEnCok = 8;

        // { ad, kelimeler, hangi düğmenin içinde, ipucu }
        private static readonly string[][] EkAramalar =
        {
            new[] { "Klavye düzeni (Türkçe Q / Türkçe F / İngilizce)", "turkce q f ingilizce us harf yazma duzen", "KLAVYE AYARLARI", "Klavye düzeni: KLAVYE AYARLARI'nın içinde." },
            new[] { "Uzun basış süresi", "uzun basis basili tutma sure", "KLAVYE AYARLARI", "Uzun basış süresi: KLAVYE AYARLARI'nın içinde." },
            new[] { "Çift basış aralığı", "cift basis iki kez aralik sure", "KLAVYE AYARLARI", "Çift basış aralığı: KLAVYE AYARLARI'nın içinde." },
            new[] { "Harfler arası bekleme (yazma hızı)", "metin yazma hiz harf kaciriyor bekleme", "KLAVYE AYARLARI", "Harfler arası bekleme: KLAVYE AYARLARI'nın içinde." },
            new[] { "Encoder yönü ve hassasiyeti", "encoder ters yon hassasiyet adim tik", "KLAVYE AYARLARI", "Encoder ayarları: KLAVYE AYARLARI'nın içinde." },
            new[] { "Uyku engelleyici", "uyku uyuma kilitlenme ekran koruyucu fare kipirdat", "KLAVYE AYARLARI", "Uyku engelleyici: KLAVYE AYARLARI'nın içinde." },
            new[] { "Tuş titreşim filtresi", "titresim cift algilama iki kez basiyor debounce tus", "KLAVYE AYARLARI", "Tuş titreşim filtresi: KLAVYE AYARLARI'nın içinde." },
            new[] { "Encoder tık başına tekrar", "encoder tik basina tekrar hizli ses kaydirma kez", "KLAVYE AYARLARI", "Encoder tık başına: KLAVYE AYARLARI'nın içinde." },
            new[] { "Kart sağlığı (sıcaklık, boş alan, hatalar)", "tanilama saglik sicaklik bos alan bellek hata", "TANILAMA", "Kartın sağlığı: TANILAMA'ya tıkla." },
            new[] { "Kart yazılımını güncelle", "kart yazilim guncelle firmware surum", "GÜNCELLE", null },
            new[] { "Tam yedek (her şeyi yedekle / geri yükle)", "yedek yedekle geri yukle zip tasima bilgisayar", "YEDEKLE", null },
            new[] { "Hazır profiller", "hazir profil sablon ornek", "HAZIR PROFİLLER", null },
            new[] { "Uygulamayı kaldır", "kaldir sil uninstall", "KALDIR", null },
            new[] { "Kart yazılımını geri al", "geri al eski surum onceki dusur kart yazilim", "ÖNCEKİ SÜRÜME DÖN", null },
        };

        private void AramaKutusuOlustur(Panel baslik)
        {
            YuvarlakPanel kutu = new YuvarlakPanel
            {
                Location = new Point(128, 8),
                Size = new Size(272, 28),
                Yaricap = 14,
                DolguRengi = Tema.KartAcik,
                KenarRengi = Color.Transparent
            };
            kutu.Controls.Add(new Label
            {
                Text = "\uE721",
                Font = new Font("Segoe MDL2 Assets", 9f),
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(6, 0),
                Size = new Size(22, 28)
            });
            txtAra = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.KartAcik,
                ForeColor = Tema.Yazi,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(30, 6),
                Width = 232
            };
            lblAraIpucu = new Label
            {
                Text = "Ayar ara… (Ctrl+F)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Tema.Pasif,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(29, 5),
                Cursor = Cursors.IBeam
            };
            lblAraIpucu.Click += (s, e) => txtAra.Focus();
            kutu.Click += (s, e) => txtAra.Focus();
            txtAra.TextChanged += (s, e) =>
            {
                lblAraIpucu.Visible = txtAra.Text.Length == 0;
                AramaSonuclariniGoster();
            };
            txtAra.PreviewKeyDown += (s, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down) e.IsInputKey = true; };
            txtAra.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && aramaSonuclari.Count > 0)
                {
                    e.SuppressKeyPress = true;
                    AramaSonucunaGit(aramaSonuclari[0]);
                }
            };
            kutu.Controls.Add(txtAra);
            kutu.Controls.Add(lblAraIpucu);
            lblAraIpucu.BringToFront();
            baslik.Controls.Add(kutu);
        }

        // Sayfalardaki kalın yazılardan arama dizini
        private void AramaDiziniOlustur()
        {
            for (int s = 0; s < sayfalar.Length; s++)
            {
                Panel sayfa = sayfalar[s];

                // Bölüm başlıkları (Baslik(): sayfanın doğrudan çocuğu, 10.5 kalın)
                List<Label> basliklar = sayfa.Controls.OfType<Label>()
                    .Where(l => l.Font.Bold && l.Font.Size >= 10.5f).OrderBy(l => l.Top).ToList();

                foreach (Label l in TumAltDenetimler(sayfa).OfType<Label>())
                {
                    if (!l.Font.Bold || l.Font.Name == "Segoe MDL2 Assets") continue;
                    string ad = AramaAdi(l.Text);
                    if (ad.Length < 3 || !ad.Any(char.IsLetter)) continue;

                    // Parlatılacak: düğme ya da kart (yazının içinde durduğu yuvarlak kutu), yoksa yazının kendisi
                    Control hedef = l;
                    for (Control c = l.Parent; c != null && c != sayfa; c = c.Parent)
                        if (c is YuvarlakPanel) { hedef = c; break; }

                    // Yakındaki açıklama (anahtar satırındaki soluk yazı) da aranabilsin
                    string kelimeler = hedef != l
                        ? string.Join(" ", TumAltDenetimler(hedef).OfType<Label>().Where(x => !x.Font.Bold).Select(x => x.Text))
                        : "";

                    int y = sayfa.PointToClient(hedef.PointToScreen(Point.Empty)).Y;
                    Label baglam = basliklar.LastOrDefault(b => b.Top <= y + 2 && b != l);

                    if (aramaDizini.Any(o => o.Sekme == s && o.Ad == ad && o.Hedef == hedef)) continue;
                    aramaDizini.Add(new AramaOgesi
                    {
                        Ad = ad, Sekme = s, Hedef = hedef, Kaynak = l, Kelimeler = kelimeler,
                        Baglam = baglam != null && baglam != l ? AramaAdi(baglam.Text) : null
                    });
                }
            }

            // Pencerelerin içindeki ayarlar: ilgili düğmeye götürür
            foreach (string[] ek in EkAramalar)
            {
                AramaOgesi dugme = aramaDizini.FirstOrDefault(o => AramaAdi(o.Ad).StartsWith(ek[2], StringComparison.OrdinalIgnoreCase));
                if (dugme == null) continue;
                aramaDizini.Add(new AramaOgesi
                {
                    Ad = ek[0], Kelimeler = ek[1], Sekme = dugme.Sekme, Hedef = dugme.Hedef,
                    Baglam = dugme.Baglam, Ipucu = ek[3]
                });
            }
        }

        private static IEnumerable<Control> TumAltDenetimler(Control c)
        {
            foreach (Control alt in c.Controls)
            {
                yield return alt;
                foreach (Control torun in TumAltDenetimler(alt)) yield return torun;
            }
        }

        // "KLAVYE AYARLARI…" -> "KLAVYE AYARLARI"; çok satırlıysa ilk satır
        private static string AramaAdi(string metin)
        {
            string s = (metin ?? "").Replace("\r", "").Split('\n')[0].Trim();
            return s.TrimEnd('…', '.', ':').Trim();
        }

        private static string AramaNorm(string s)
        {
            s = (s ?? "").ToLower(new System.Globalization.CultureInfo("tr-TR"));
            return s.Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g')
                    .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c').Replace('â', 'a');
        }

        // Denetim kendi sayfasında görünüyor mu (gizli düğmeler sonuçta çıkmasın).
        // Sayfa o an gizli olabileceği için Visible yerine denetimin kendi görünürlük bayrağına bakılır.
        private static readonly System.Reflection.MethodInfo DurumOku =
            typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private bool Gorunur(Control c, Panel sayfa)
        {
            for (; c != null && c != sayfa; c = c.Parent)
            {
                try { if (DurumOku != null && !(bool)DurumOku.Invoke(c, new object[] { 2 })) return false; }   // STATE_VISIBLE
                catch (Exception) { return true; }
            }
            return true;
        }

        private void AramaSonuclariniGoster()
        {
            aramaSonuclari.Clear();
            string ara = AramaNorm(txtAra.Text.Trim());

            if (ara.Length > 0)
            {
                string[] kelimeler = ara.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (AramaOgesi o in aramaDizini)
                {
                    string ad = o.Kaynak != null ? AramaAdi(o.Kaynak.Text) : o.Ad;
                    string metin = AramaNorm(ad + " " + o.Kelimeler + " " + o.Baglam + " " + sekmeAdlari[o.Sekme]);
                    if (kelimeler.All(k => metin.Contains(k)) && Gorunur(o.Hedef, sayfalar[o.Sekme]))
                        aramaSonuclari.Add(o);
                }

                // Adı aranan kelimeyle başlayanlar önce
                aramaSonuclari.Sort((x, y) =>
                {
                    bool bx = AramaNorm(x.Ad).StartsWith(kelimeler[0]), by = AramaNorm(y.Ad).StartsWith(kelimeler[0]);
                    return bx == by ? 0 : bx ? -1 : 1;
                });
            }

            if (araSonucKutusu != null)
            {
                Controls.Remove(araSonucKutusu);
                araSonucKutusu.Dispose();
                araSonucKutusu = null;
            }
            if (ara.Length == 0) return;

            int adet = Math.Min(AraEnCok, aramaSonuclari.Count);
            araSonucKutusu = new YuvarlakPanel
            {
                Location = new Point(110, 46),
                Size = new Size(330, Math.Max(1, adet) * AraSatir + 12),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar2
            };

            if (adet == 0)
            {
                araSonucKutusu.Controls.Add(new Label
                {
                    Text = "Sonuç yok",
                    Font = new Font("Segoe UI", 9.5f),
                    ForeColor = Tema.SolukYazi,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(0, 6),
                    Size = new Size(330, AraSatir)
                });
            }

            for (int i = 0; i < adet; i++)
            {
                AramaOgesi o = aramaSonuclari[i];
                YuvarlakPanel satir = new YuvarlakPanel
                {
                    Location = new Point(6, 6 + i * AraSatir),
                    Size = new Size(318, AraSatir - 2),
                    Yaricap = 8,
                    DolguRengi = Tema.Kart,
                    HoverRengi = Tema.Hover,
                    KenarRengi = i == 0 ? Mavi : Color.Transparent,
                    Cursor = Cursors.Hand
                };
                string ad = o.Kaynak != null ? AramaAdi(o.Kaynak.Text) : o.Ad;
                Label lAd = new Label
                {
                    Text = ad,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = Tema.Yazi,
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(10, 0),
                    Size = new Size(196, AraSatir - 2),
                    Cursor = Cursors.Hand
                };
                Label lYer = new Label
                {
                    Text = sekmeAdlari[o.Sekme] + (o.Baglam != null && o.Baglam != ad ? " › " + o.Baglam : ""),
                    Font = new Font("Segoe UI", 8f),
                    ForeColor = Tema.SolukYazi,
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleRight,
                    Location = new Point(206, 0),
                    Size = new Size(104, AraSatir - 2),
                    Cursor = Cursors.Hand
                };
                satir.Controls.Add(lAd);
                satir.Controls.Add(lYer);
                Bagla(satir, () => AramaSonucunaGit(o));
                araSonucKutusu.Controls.Add(satir);
            }

            Controls.Add(araSonucKutusu);
            araSonucKutusu.BringToFront();
        }

        private void AramaSonucunaGit(AramaOgesi o)
        {
            if (islemde) return;
            txtAra.Text = "";            // sonuç kutusu kapanır
            ActiveControl = null;
            SekmeSec(o.Sekme);
            if (o.Ipucu != null) Durum(o.Ipucu, false);
            BeginInvoke(new Action(() => Parlat(o.Hedef)));
        }

        // Bulunan ayarın çevresinde kısa süre turuncu çerçeve
        private void Parlat(Control hedef)
        {
            Control kap = hedef.Parent;
            if (kap == null) return;

            Color renk = Color.FromArgb(234, 88, 12);
            Rectangle r = Rectangle.Inflate(hedef.Bounds, 3, 3);
            Panel[] cizgiler =
            {
                new Panel { Bounds = new Rectangle(r.Left, r.Top, r.Width, 2), BackColor = renk },
                new Panel { Bounds = new Rectangle(r.Left, r.Bottom - 2, r.Width, 2), BackColor = renk },
                new Panel { Bounds = new Rectangle(r.Left, r.Top, 2, r.Height), BackColor = renk },
                new Panel { Bounds = new Rectangle(r.Right - 2, r.Top, 2, r.Height), BackColor = renk }
            };
            foreach (Panel c in cizgiler)
            {
                kap.Controls.Add(c);
                c.BringToFront();
            }

            Timer t = new Timer { Interval = 1800 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                foreach (Panel c in cizgiler)
                {
                    kap.Controls.Remove(c);
                    c.Dispose();
                }
            };
            t.Start();
        }

        // ---------- Sekme çubuğu + sayfalar ----------
        private void SekmeleriOlustur()
        {
            for (int i = 0; i < sekmeAdlari.Length; i++)
            {
                int indeks = i;

                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(20 + i * 61, 56),
                    Size = new Size(58, 32),
                    Yaricap = 16,
                    Cursor = Cursors.Hand
                };
                Label l = new Label
                {
                    Text = sekmeAdlari[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                p.Controls.Add(l);
                Bagla(p, () => SekmeSec(indeks));

                sekmeDugmeleri[i] = p;
                sekmeYazilari[i] = l;
                Controls.Add(p);

                sayfalar[i] = new Panel
                {
                    Location = new Point(0, 100),
                    Size = new Size(460, 488),
                    BackColor = Color.Transparent,
                    Visible = false
                };
                sayfalar[i].MouseDown += SurukleBaslat;
                Controls.Add(sayfalar[i]);
            }
        }

        private void SekmeSec(int i)
        {
            if (islemde) return;

            aktifSekme = i;

            for (int k = 0; k < sekmeAdlari.Length; k++)
            {
                bool aktif = (k == i);
                if (gomulu && sekmeSimgeleri[k] != null)
                {
                    // soldaki dikey liste: seçili satır açık mavi zemin + mavi çerçeve (profil listesi gibi)
                    sekmeDugmeleri[k].Yaricap = 10;
                    sekmeDugmeleri[k].DolguRengi = aktif ? Tema.Secili : Color.Transparent;
                    sekmeDugmeleri[k].KenarRengi = aktif ? Mavi : Color.Transparent;
                    sekmeDugmeleri[k].HoverRengi = aktif ? Color.Empty : Tema.Hover;
                    sekmeYazilari[k].ForeColor = sekmeSimgeleri[k].ForeColor = aktif ? Mavi : Tema.Yazi;
                }
                else
                {
                    sekmeDugmeleri[k].DolguRengi = aktif ? Mavi : Tema.Kart;
                    sekmeDugmeleri[k].KenarRengi = aktif ? Mavi : Tema.Kenar;
                    sekmeDugmeleri[k].HoverRengi = aktif ? Color.Empty : Tema.Hover;
                    sekmeYazilari[k].ForeColor = aktif ? Color.White : Tema.Yazi;
                }
                sekmeDugmeleri[k].Invalidate(true);
                sayfalar[k].Visible = aktif;
            }

            if (i == 6) { Task t = UygulamaSurumunuGoster(); }
            else if (i == 2) KartBilgisiniYenile();
            else if (i == 3) IstatistigiYenile();
            else if (i == 4) GecmisiYenile();
            else if (i == 5) GunluguYenile();

            string[] ipucu =
            {
                "Profiller bu bilgisayarda saklanır.",
                "Bu sayfadaki ayarlar hemen uygulanır.",
                "Kart yazılımını internetten ya da zip dosyasından günceller.",
                "Sayım, uygulama açık ve klavye bağlıyken yapılır.",
                "Klavyedeki ayarlar her değiştiğinde otomatik yedek alınır.",
                "Günlük bu bilgisayarda saklanır, klavyeye gitmez.",
                "Uygulamanın sürümü, güncellemeleri ve kurulumu."
            };
            Durum(ipucu[i], false);
        }

        private Label Baslik(Control kap, string metin, int y)
        {
            Label l = new Label
            {
                Text = metin,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Tema.Yazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(22, y)
            };
            kap.Controls.Add(l);
            basliklar.Add(l);
            return l;
        }

        // =================================================================
        //  GÖMÜLÜ YERLEŞİM: ana penceredeki Ayarlar sekmesi geniş. Sayfalar 460 piksellik tek sütun
        //  olarak kurulur; burada bölümleri ("Sistem tepsisi", "Görünüm"...) iki sütuna dağıtıp
        //  pencerenin genişliğine yayarız. Tek bölümlü sayfalar (istatistik, günlük) tüm genişliği kaplar.
        // =================================================================
        private readonly List<Label> basliklar = new List<Label>();
        private YuvarlakPanel btnHazirProfiller;
        private bool profilSekmesiYok;
        private readonly Label[] sekmeSimgeleri = new Label[7];
        private Panel bolucu;

        // Ana penceredeki Ayarlar sekmesinde Profiller sekmesi yok: profiller, hazır profiller ve yedek
        // soldaki PROFİLLER panelinde
        private void ProfilSekmesiniKaldir()
        {
            foreach (Control c in sayfalar[0].Controls.Cast<Control>().ToList()) sayfalar[0].Controls.Remove(c);
            sekmeDugmeleri[0].Visible = false;
            profilSekmesiYok = true;
        }

        private class Yerlesim
        {
            public Rectangle Ilk;      // tek sütundaki yeri
            public int Sutun;          // 0 sol, 1 sağ
            public int YukariKaydir;   // sağ sütuna geçen bölüm ne kadar yukarı çıkar
        }

        private Dictionary<Control, Yerlesim>[] sayfaYerlesimi;
        private int[] sayfaSutunSayisi;
        private int[] sekmeGenislikleri;

        private void GomuluHazirla()
        {
            sayfaYerlesimi = new Dictionary<Control, Yerlesim>[sayfalar.Length];
            sayfaSutunSayisi = new int[sayfalar.Length];

            for (int p = 0; p < sayfalar.Length; p++)
            {
                Panel kap = sayfalar[p];
                List<Control> cocuklar = kap.Controls.Cast<Control>().ToList();
                List<int> ustler = basliklar.Where(b => b.Parent == kap).Select(b => b.Top).Distinct().OrderBy(t => t).ToList();
                int alt = cocuklar.Count == 0 ? 0 : cocuklar.Max(c => c.Bottom);

                // İki sütunun yüksekliği en yakın olacak bölüm sınırı
                int bol = -1, enIyi = int.MaxValue;
                for (int s = 1; s < ustler.Count; s++)
                {
                    int enUzun = Math.Max(ustler[s] - ustler[0], alt - ustler[s]);
                    if (enUzun < enIyi) { enIyi = enUzun; bol = s; }
                }
                sayfaSutunSayisi[p] = bol < 0 ? 1 : 2;

                Dictionary<Control, Yerlesim> yer = new Dictionary<Control, Yerlesim>();
                foreach (Control c in cocuklar)
                {
                    bool sag = bol >= 0 && c.Top >= ustler[bol] - 2;
                    yer[c] = new Yerlesim
                    {
                        Ilk = c.Bounds,
                        Sutun = sag ? 1 : 0,
                        YukariKaydir = sag ? ustler[bol] - ustler[0] : 0
                    };
                    IcCapalari(c);
                }
                sayfaYerlesimi[p] = yer;
            }

            sekmeGenislikleri = new int[sekmeDugmeleri.Length];
            string[] simgeler = { "\uE8F1", "\uE713", "\uE950", "\uE9D2", "\uE81C", "\uE8A5", "\uE946" };
            for (int i = 0; i < sekmeDugmeleri.Length; i++)
            {
                int indeks = i;
                sekmeYazilari[i].Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                sekmeGenislikleri[i] = TextRenderer.MeasureText(sekmeAdlari[i], sekmeYazilari[i].Font).Width + 34;

                Label simge = sekmeSimgeleri[i] = new Label
                {
                    Text = simgeler[i],
                    Font = new Font("Segoe MDL2 Assets", 11f),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Cursor = Cursors.Hand
                };
                simge.Click += (s, e) => SekmeSec(indeks);
                simge.MouseEnter += (s, e) => sekmeDugmeleri[indeks].Hover = true;
                simge.MouseLeave += (s, e) => sekmeDugmeleri[indeks].Hover = false;
                sekmeDugmeleri[i].Controls.Add(simge);
            }

            // Başlık şeridi kartla aynı renkte (ayrı bir pencere gibi durmasın), altında ince çizgi
            foreach (Panel ust in Controls.OfType<Panel>().Where(p => p.Dock == DockStyle.Top))
            {
                ust.BackColor = Tema.Panel;
                foreach (Label l in ust.Controls.OfType<Label>()) l.ForeColor = Tema.Yazi;
                ust.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Tema.Kenar });
            }

            bolucu = new Panel { BackColor = Tema.Kenar };
            Controls.Add(bolucu);

            SekmeSec(aktifSekme);
        }

        // Genişleyen kutunun içindekiler: sağdaki küçükler (düğme, aç-kapa) sağa yapışır, genişler uzar
        private static void IcCapalari(Control kutu)
        {
            int w = kutu.Width;
            foreach (Control c in kutu.Controls)
            {
                if (c.Dock != DockStyle.None) continue;
                bool genis = c.Width >= w / 2;
                bool sagda = !genis && c.Left > w / 2 && c.Right >= w - 50;

                if (genis) c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                else if (sagda) c.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                else c.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                if (genis && c.Controls.Count > 0) IcCapalari(c);
            }
        }

        private void GomuluYerlestir()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W < 200 || H < 200) return;

            // Solda dikey sekme listesi (ana penceredeki profil listesi gibi), sağda içerik
            const int yanX = 16, yanG = 200, ustY = 60;
            int y = ustY;
            for (int i = 0; i < sekmeDugmeleri.Length; i++)
            {
                if (i == 0 && profilSekmesiYok) continue;
                sekmeDugmeleri[i].SetBounds(yanX, y, yanG, 38);
                sekmeYazilari[i].Dock = DockStyle.None;
                sekmeYazilari[i].AutoSize = false;
                sekmeYazilari[i].TextAlign = ContentAlignment.MiddleLeft;
                sekmeYazilari[i].SetBounds(40, 0, yanG - 44, 38);
                sekmeSimgeleri[i].SetBounds(10, 0, 26, 38);
                sekmeDugmeleri[i].Invalidate(true);
                y += 44;
            }

            int icX = yanX + yanG + 24;          // içerik sayfasının solu
            if (bolucu != null) bolucu.SetBounds(icX - 12, ustY, 1, Math.Max(10, H - ustY - 20));

            const int ara = 32;
            int sayfaH = Math.Max(200, H - ustY - 58);
            int icG = W - icX - 20;              // içeriğin genişliği

            for (int p = 0; p < sayfalar.Length; p++)
            {
                sayfalar[p].SuspendLayout();
                sayfalar[p].SetBounds(icX, ustY - 4, W - icX, sayfaH);

                int sutun = sayfaSutunSayisi[p];
                int sutunG = sutun == 2 ? (icG - ara) / 2 : icG;
                double k = Math.Max(1.0, sutunG / 420.0);

                foreach (KeyValuePair<Control, Yerlesim> kv in sayfaYerlesimi[p])
                {
                    if (kv.Key.IsDisposed) continue;
                    Rectangle r = kv.Value.Ilk;
                    int sutunX = kv.Value.Sutun * (sutunG + ara);
                    int nx, nw;
                    if (r.Width <= 60)
                    {
                        // küçük öğe: sağ kenardaysa sağda kalır, değilse orantılı yerine
                        nw = r.Width;
                        nx = r.Right >= 420 ? sutunX + sutunG - (440 - r.Right) - r.Width
                                            : sutunX + (int)Math.Round((r.X - 20) * k);
                    }
                    else
                    {
                        nx = sutunX + (int)Math.Round((r.X - 20) * k);
                        nw = (int)Math.Round(r.Width * k);
                    }
                    kv.Key.SetBounds(nx, r.Y - kv.Value.YukariKaydir, nw, r.Height);
                }
                sayfalar[p].ResumeLayout();
            }

            if (lblDurum != null) lblDurum.SetBounds(icX + 2, H - 50, W - icX - 24, 42);
        }

        private YuvarlakPanel KartKutusu(Control kap, int x, int y, int w, int h)
        {
            YuvarlakPanel k = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                Yaricap = 14,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };
            kap.Controls.Add(k);
            return k;
        }

        // =================================================================
        //  PROFİLLER (+ yedek)
        // =================================================================
        private void ProfilSayfasiOlustur(Panel kap)
        {
            Baslik(kap, "Profiller", 4);

            YuvarlakPanel kutu = new YuvarlakPanel
            {
                Location = new Point(20, 32),
                Size = new Size(296, 42),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };
            txtAd = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Tema.Yazi,
                Font = new Font("Segoe UI", 11f),
                Location = new Point(14, 11),
                Width = 270,
                MaxLength = 40
            };
            lblAdIpucu = new Label
            {
                Text = "Yeni profil adı (örn. Oyun)",
                Font = new Font("Segoe UI", 11f),
                ForeColor = Tema.Pasif,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(14, 10),
                Cursor = Cursors.IBeam
            };
            lblAdIpucu.Click += (s, e) => txtAd.Focus();
            txtAd.TextChanged += (s, e) => lblAdIpucu.Visible = txtAd.Text.Length == 0;
            txtAd.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ProfilKaydet();
                }
            };
            kutu.Controls.Add(txtAd);
            kutu.Controls.Add(lblAdIpucu);
            lblAdIpucu.BringToFront();
            kutu.Click += (s, e) => txtAd.Focus();
            kap.Controls.Add(kutu);

            kap.Controls.Add(Dugme("\uE74E", "KAYDET", 326, 32, 114, 42, true, ProfilKaydet));

            YuvarlakPanel kart = KartKutusu(kap, 20, 84, 420, 172);

            liste = new AkiciListe
            {
                Location = new Point(6, 8),
                Size = new Size(408, 156),
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44,
                IntegralHeight = false
            };
            liste.DrawItem += ProfilCiz;
            liste.SelectedIndexChanged += (s, e) => liste.Invalidate();
            liste.DoubleClick += (s, e) => ProfilYukle();

            lblBos = new Label
            {
                Text = "Henüz profil yok.\nKlavyeye bağlanıp mevcut ayarlarını bir adla kaydet.",
                Location = new Point(6, 8),
                Size = new Size(408, 156),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Tema.SolukYazi,
                BackColor = Tema.Kart,
                Font = new Font("Segoe UI", 10f)
            };

            kart.Controls.Add(liste);
            kart.Controls.Add(lblBos);
            lblBos.BringToFront();

            kap.Controls.Add(Dugme("\uE896", "YÜKLE", 20, 264, 120, 36, false, ProfilYukle));
            kap.Controls.Add(Dugme("\uE74D", "SİL", 148, 264, 96, 36, false, ProfilSil));
            btnHazirProfiller = Dugme("\uE8F1", "HAZIR PROFİLLER…", 252, 264, 188, 36, false, HazirProfilMenusu);
            kap.Controls.Add(btnHazirProfiller);

            Baslik(kap, "Yedekle / Geri yükle", 318);
            kap.Controls.Add(Dugme("\uE78C", "YEDEKLE…", 20, 346, 205, 42, false, Yedekle));
            kap.Controls.Add(Dugme("\uE777", "GERİ YÜKLE…", 235, 346, 205, 42, false, GeriYukle));
            kap.Controls.Add(new Label
            {
                Text = "Tam yedek (.zip): profiller, Makrolarım, geçmiş, tuş adları / simgeleri, " +
                       "uygulama ayarları ve klavyedeki ayarlar tek dosyada. Yeni bilgisayara taşımak için de kullanılır.",
                Font = kucukFont,
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                Location = new Point(22, 396),
                Size = new Size(416, 36)
            });
        }

        private void ProfilCiz(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= profiller.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var zemin = new SolidBrush(Tema.Kart))
                g.FillRectangle(zemin, e.Bounds);

            string ad = profiller[e.Index];
            bool secili = (e.State & DrawItemState.Selected) != 0;
            bool aktif = string.Equals(ad, UygulamaAyarlari.AktifProfil, StringComparison.CurrentCultureIgnoreCase);

            Rectangle kutu = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4);

            if (secili)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(kutu.X, kutu.Y, kutu.Width - 1, kutu.Height - 1), 10))
                using (var dolgu = new SolidBrush(Tema.Secili))
                using (var kalem = new Pen(Mavi, 1.5f))
                {
                    g.FillPath(dolgu, yol);
                    g.DrawPath(kalem, yol);
                }
            }

            StringFormat ortala = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using (Font ikonFont = new Font("Segoe MDL2 Assets", 13f))
            using (var b = new SolidBrush(aktif ? Mavi : Tema.SolukYazi))
                g.DrawString("\uE77B", ikonFont, b, new RectangleF(kutu.X + 8, kutu.Y, 32, kutu.Height), ortala);

            StringFormat sol = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using (var b = new SolidBrush(Tema.Yazi))
                g.DrawString(ad, adFont, b, new RectangleF(kutu.X + 46, kutu.Y, kutu.Width - 150, kutu.Height), sol);

            if (aktif)
            {
                RectangleF rozet = new RectangleF(kutu.Right - 78, kutu.Y + (kutu.Height - 22) / 2f, 66, 22);
                using (var yol = YuvarlakPanel.Yol(rozet, 11))
                using (var b = new SolidBrush(Tema.OkDolgu))
                    g.FillPath(b, yol);

                using (var b = new SolidBrush(Tema.OkYazi))
                    g.DrawString("AKTİF", etiketFont, b, rozet, ortala);
            }
        }

        private void ListeyiYenile(string secilecek = null)
        {
            profiller = UygulamaAyarlari.Profiller();

            liste.BeginUpdate();
            liste.Items.Clear();
            foreach (string p in profiller) liste.Items.Add(p);
            liste.EndUpdate();

            if (secilecek != null)
            {
                int i = profiller.FindIndex(p => string.Equals(p, secilecek, StringComparison.CurrentCultureIgnoreCase));
                if (i >= 0) liste.SelectedIndex = i;
            }

            lblBos.Visible = profiller.Count == 0;
        }

        private string SeciliProfil()
        {
            int i = liste.SelectedIndex;
            return (i >= 0 && i < profiller.Count) ? profiller[i] : null;
        }

        private void ProfilKaydet()
        {
            if (islemde) return;

            string ad = UygulamaAyarlari.AdTemizle(txtAd.Text);
            if (ad.Length == 0)
            {
                Durum("Profil için bir ad yaz.", true);
                txtAd.Focus();
                return;
            }

            string metin = sahip.ProfilMetni();
            if (metin == null)
            {
                Durum("Önce klavyeye bağlan. Profil, karttaki ayarlardan oluşturulur.", true);
                return;
            }

            if (File.Exists(UygulamaAyarlari.ProfilYolu(ad)) &&
                MessageBox.Show(this, "\"" + ad + "\" profili zaten var. Üzerine yazılsın mı?", "Profil",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                UygulamaAyarlari.ProfilYaz(ad, metin);
                UygulamaAyarlari.AktifProfil = ad;
                txtAd.Text = "";
                ListeyiYenile(ad);
                Durum("\"" + ad + "\" profili kaydedildi.", false);
            }
            catch (Exception ex)
            {
                Durum("Kaydedilemedi: " + ex.Message, true);
            }
        }

        private async void ProfilYukle()
        {
            if (islemde) return;

            string ad = SeciliProfil();
            if (ad == null)
            {
                Durum("Yüklemek için listeden bir profil seç.", true);
                return;
            }

            string metin;
            try { metin = UygulamaAyarlari.ProfilOku(ad); }
            catch (Exception ex)
            {
                Durum("Profil okunamadı: " + ex.Message, true);
                return;
            }

            if (await KartaUygula(metin, "\"" + ad + "\" profili yükleniyor..."))
            {
                UygulamaAyarlari.AktifProfil = ad;
                ListeyiYenile(ad);
                Durum("\"" + ad + "\" profili karta yüklendi.", false);
            }
        }

        private void ProfilSil()
        {
            if (islemde) return;

            string ad = SeciliProfil();
            if (ad == null)
            {
                Durum("Silmek için listeden bir profil seç.", true);
                return;
            }

            if (MessageBox.Show(this, "\"" + ad + "\" profili silinsin mi?\nKarttaki ayarlar değişmez.", "Profil",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                File.Delete(UygulamaAyarlari.ProfilYolu(ad));
                if (string.Equals(UygulamaAyarlari.AktifProfil, ad, StringComparison.CurrentCultureIgnoreCase))
                    UygulamaAyarlari.AktifProfil = "";

                ListeyiYenile();
                Durum("\"" + ad + "\" profili silindi.", false);
            }
            catch (Exception ex)
            {
                Durum("Silinemedi: " + ex.Message, true);
            }
        }

        // .zip: tam yedek (bu bilgisayardaki tüm veriler + klavyedeki ayarlar), .txt: yalnızca klavyedeki ayarlar
        private void Yedekle()
        {
            if (islemde) return;

            string metin = sahip.ProfilMetni();   // bağlı değilse null
            string yol;

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Yedekle";
                d.Filter = "Tam yedek: uygulama + klavye (*.zip)|*.zip|Yalnızca klavyedeki ayarlar (*.txt)|*.txt";
                d.FilterIndex = 1;
                d.AddExtension = true;
                d.DefaultExt = "zip";
                d.FileName = "makro-klavye-yedek-" + DateTime.Now.ToString("yyyy-MM-dd");

                if (d.ShowDialog(this) != DialogResult.OK) return;
                yol = d.FileName;
            }

            if (yol.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                if (metin == null)
                {
                    Durum("Önce klavyeye bağlan. Bu yedek, klavyedeki ayarlardan alınır.", true);
                    return;
                }

                try
                {
                    UygulamaAyarlari.DosyaYaz(yol, metin);
                    UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi, "Klavye ayarları yedeklendi: " + Path.GetFileName(yol));
                    Durum("Yedek kaydedildi: " + Path.GetFileName(yol), false);
                }
                catch (Exception ex)
                {
                    Durum("Yedeklenemedi: " + ex.Message, true);
                }
                return;
            }

            if (!yol.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) yol += ".zip";

            if (metin == null &&
                MessageBox.Show(this, "Klavye bağlı değil, yedekte klavyedeki ayarlar olmayacak; " +
                                      "yalnızca bu bilgisayardaki veriler (profiller, makrolar, geçmiş, tuş adları, ayarlar) yedeklenecek.\n\n" +
                                      "Devam edilsin mi?", "Tam yedek", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi, "Tam yedek alındı: " + Path.GetFileName(yol));
                TamYedek y = UygulamaAyarlari.TamYedekOlustur(yol, metin);
                Durum("Tam yedek kaydedildi (" + y.Ozet() + ").", false);
            }
            catch (Exception ex)
            {
                Durum("Yedeklenemedi: " + ex.Message, true);
            }
        }

        private async void GeriYukle()
        {
            if (islemde) return;

            string yol;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Yedekten geri yükle";
                d.Filter = "Makro Klavye yedeği (*.zip;*.txt)|*.zip;*.txt|Tüm dosyalar (*.*)|*.*";

                if (d.ShowDialog(this) != DialogResult.OK) return;
                yol = d.FileName;
            }

            if (yol.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await TamGeriYukle(yol);
                return;
            }

            if (MessageBox.Show(this, "Karttaki tüm ayarlar bu yedekle değiştirilecek. Devam edilsin mi?",
                                "Geri yükle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string metin;
            try { metin = UygulamaAyarlari.DosyaOku(yol); }
            catch (Exception ex)
            {
                Durum("Dosya okunamadı: " + ex.Message, true);
                return;
            }

            if (await KartaUygula(metin, "Yedek karta yükleniyor..."))
            {
                UygulamaAyarlari.AktifProfil = "";
                ListeyiYenile();
                Durum("Yedek karta yüklendi.", false);
            }
        }

        // Tam yedekten: önce şimdiki hâl saklanır, istenirse klavye geri yüklenir,
        // sonra bu bilgisayardaki veriler değiştirilip uygulama yeniden başlatılır.
        private async Task TamGeriYukle(string yol)
        {
            TamYedek y;
            try { y = UygulamaAyarlari.TamYedekOku(yol); }
            catch (Exception ex)
            {
                Durum("Yedek okunamadı: " + ex.Message, true);
                return;
            }

            string kaynak = "";
            if (y.Tarih.HasValue) kaynak += ZamanYazisi(y.Tarih.Value);
            if (y.Bilgisayar.Length > 0) kaynak += (kaynak.Length > 0 ? " · " : "") + y.Bilgisayar;

            string soru = (kaynak.Length > 0 ? "Yedek: " + kaynak + "\n" : "") +
                          "İçinde: " + y.Ozet() + "\n\n" +
                          "Bu bilgisayardaki profiller, makrolar, geçmiş, tuş adları ve uygulama ayarları " +
                          "yedektekiyle değiştirilecek. Şimdiki hâl önce \"" + UygulamaAyarlari.OncekiHalDosyasi +
                          "\" olarak saklanır. Sonra uygulama yeniden başlar.";

            bool klavyeye = false;
            if (y.KlavyeMetni != null && sahip.KlavyeBagli)
            {
                DialogResult c = MessageBox.Show(this, soru + "\n\nKlavyedeki ayarlar da yedektekine döndürülsün mü?\n\n" +
                                                       "Evet: uygulama + klavye\nHayır: yalnızca uygulama verileri",
                                                 "Tam yedeği geri yükle", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (c == DialogResult.Cancel) return;
                klavyeye = c == DialogResult.Yes;
            }
            else
            {
                if (y.KlavyeMetni != null)
                    soru += "\n\nKlavye bağlı olmadığı için klavyedeki ayarlar geri yüklenmeyecek " +
                            "(bağlanıp aynı yedeği tekrar seçersen yüklenir).";

                if (MessageBox.Show(this, soru + "\n\nDevam edilsin mi?", "Tam yedeği geri yükle",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            // Şimdiki hâl (klavye dahil) bozulmadan saklansın; saklanamazsa hiçbir şeye dokunma
            try { UygulamaAyarlari.OncekiHaliSakla(sahip.ProfilMetni()); }
            catch (Exception ex)
            {
                Durum("Şimdiki hâl saklanamadığı için geri yükleme yapılmadı: " + ex.Message, true);
                return;
            }

            if (klavyeye && !await KartaUygula(y.KlavyeMetni, "Klavyedeki ayarlar geri yükleniyor..."))
                return;   // hata mesajı KartaUygula'da gösterildi, uygulama verilerine dokunulmadı

            try { UygulamaAyarlari.TamYedekGeriYukle(y); }
            catch (Exception ex)
            {
                Durum("Geri yüklenemedi: " + ex.Message + " Önceki hâl: " + UygulamaAyarlari.OncekiHalYolu, true);
                return;
            }

            MessageBox.Show(this, "Yedek geri yüklendi" + (klavyeye ? " (klavye dahil)" : "") +
                                  ". Uygulama şimdi yeniden başlatılacak.",
                            "Tam yedek", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Program.YenidenBaslat();
        }

        // Ofis, Oyun, Tarayıcı ... gibi hazır düzenler
        private void HazirProfilMenusu()
        {
            if (islemde) return;

            ContextMenuStrip m = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new MenuRenkleri()) { RoundedEdges = false },
                Font = new Font("Segoe UI", 9.5f),
                ShowImageMargin = false,
                ShowItemToolTips = true
            };

            foreach (HazirProfiller.Sablon s in HazirProfiller.Liste)
            {
                HazirProfiller.Sablon sablon = s;
                ToolStripMenuItem o = new ToolStripMenuItem(s.Ad)
                {
                    ForeColor = Tema.Yazi,
                    ToolTipText = s.Aciklama,
                    Padding = new Padding(4, 3, 4, 3)
                };
                o.Click += (x, e) => HazirProfilYukle(sablon);
                m.Items.Add(o);
            }

            m.Closed += (x, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(Cursor.Position);
        }

        private async void HazirProfilYukle(HazirProfiller.Sablon s)
        {
            if (islemde) return;

            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return;
            }

            if (MessageBox.Show(this,
                    "\"" + s.Ad + "\" hazır profili yüklensin mi?\n\n" + s.Aciklama + "\n\n" +
                    "Tüm tuş atamaların değişir, tuşlara atanmış uygulama kısayolları kaldırılır. LED ayarın ve basış sürelerin korunur.\n" +
                    "Eski hâline Geçmiş sekmesinden ya da ana penceredeki Geri al ile dönebilirsin.",
                    "Hazır profil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // Karttaki LED ayarlarını koru, tuş atamalarını şablondan al
            StringBuilder sb = new StringBuilder("# Hazır profil: " + s.Ad + "\n");
            string mevcut = sahip.ProfilMetni() ?? "";
            foreach (string satir in mevcut.Split('\n'))
                if (satir.StartsWith("LED_") || satir.StartsWith("LONG_PRESS_MS=") || satir.StartsWith("DOUBLE_PRESS_MS=") ||
                    satir.StartsWith("KLAVYE_DUZENI=") || satir.StartsWith("TEXT_DELAY_MS=") ||
                    satir.StartsWith("ENCODER_TERS=") || satir.StartsWith("ENCODER_ADIM=") || satir.StartsWith("UYKU_") ||
                    satir.StartsWith("TITRESIM_MS=") || satir.StartsWith("ENCODER_TEKRAR="))
                    sb.Append(satir.TrimEnd('\r')).Append('\n');
            sb.Append(s.Metin);

            if (await KartaUygula(sb.ToString(), "\"" + s.Ad + "\" yükleniyor..."))
            {
                UygulamaAyarlari.AktifProfil = "";
                ListeyiYenile();
                txtAd.Text = s.Ad;
                Durum("\"" + s.Ad + "\" yüklendi. Beğendiysen KAYDET ile profil olarak sakla.", false);
            }
        }

        private class MenuRenkleri : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Tema.Kart; } }
            public override Color ImageMarginGradientBegin { get { return Tema.Kart; } }
            public override Color ImageMarginGradientMiddle { get { return Tema.Kart; } }
            public override Color ImageMarginGradientEnd { get { return Tema.Kart; } }
            public override Color MenuBorder { get { return Tema.Kenar; } }
            public override Color MenuItemBorder { get { return Tema.Hover; } }
            public override Color MenuItemSelected { get { return Tema.Hover; } }
            public override Color SeparatorDark { get { return Tema.Kenar; } }
            public override Color SeparatorLight { get { return Tema.Kart; } }
        }

        private async Task<bool> KartaUygula(string metin, string bekleMesaji)
        {
            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return false;
            }

            IslemBasla(bekleMesaji);

            string hata;
            try { hata = await sahip.ProfilUygula(metin); }
            catch (Exception ex) { hata = ex.Message; }
            finally { IslemBitti(); }

            if (hata != null)
            {
                Durum(hata, true);
                return false;
            }
            return true;
        }

        // =================================================================
        //  GENEL: sistem tepsisi, Windows ile başlat, tema
        // =================================================================
        private void GenelSayfasiOlustur(Panel kap)
        {
            Baslik(kap, "Sistem tepsisi", 4);

            AcKapa tepsi = AnahtarSatiri(kap, 32, "Kapatınca sistem tepsisinde çalışsın",
                                         "Pencere kapanır, uygulama saatin yanında açık kalır.",
                                         UygulamaAyarlari.Tepsi);
            tepsi.Degisti += acik =>
            {
                UygulamaAyarlari.Tepsi = acik;
                Durum(acik ? "Kapatınca sistem tepsisine küçülecek." : "Kapatınca uygulama tamamen kapanacak.", false);
            };

            AcKapa baslat = AnahtarSatiri(kap, 84, "Windows açılınca başlat",
                                          "Tepsi açıksa simge olarak, kapalıysa pencere olarak başlar.",
                                          UygulamaAyarlari.WindowsIleBaslat);
            baslat.Degisti += acik =>
            {
                try
                {
                    UygulamaAyarlari.WindowsIleBaslat = acik;
                    Durum(acik ? "Windows açılınca başlayacak." : "Windows ile başlama kapatıldı.", false);
                }
                catch (Exception ex)
                {
                    baslat.Acik = !acik;
                    Durum("Ayarlanamadı: " + ex.Message, true);
                }
            };

            AcKapa bildirim = AnahtarSatiri(kap, 136, "Bağlantı bildirimi",
                                            "Tepsideyken klavye takılınca ya da çıkarılınca haber ver.",
                                            UygulamaAyarlari.BaglantiBildirimi);
            bildirim.Degisti += acik =>
            {
                UygulamaAyarlari.BaglantiBildirimi = acik;
                Durum(acik ? "Bağlantı bildirimleri açık." : "Bağlantı bildirimleri kapalı.", false);
            };

            Baslik(kap, "Görünüm", 202);

            AcKapa koyu = AnahtarSatiri(kap, 230, "Koyu tema",
                                        "Uygulama yeniden başlayınca uygulanır.",
                                        UygulamaAyarlari.KoyuTema);
            koyu.Degisti += acik =>
            {
                UygulamaAyarlari.KoyuTema = acik;

                if (MessageBox.Show(this, "Tema, uygulama yeniden başlayınca değişir. Şimdi yeniden başlatılsın mı?",
                                    "Tema", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Program.YenidenBaslat();
                    return;
                }

                Durum((acik ? "Koyu" : "Açık") + " tema bir sonraki açılışta uygulanacak.", false);
            };

            AcKapa ustte = AnahtarSatiri(kap, 282, "Her zaman üstte",
                                         "Ana pencere diğer pencerelerin üstünde durur.",
                                         UygulamaAyarlari.HerZamanUstte);
            ustte.Degisti += acik =>
            {
                try { UygulamaAyarlari.HerZamanUstte = acik; }
                catch (Exception) { }

                Form ana = Owner ?? TopLevelControl as Form;
                if (ana != null && ana != this) ana.TopMost = acik;
                Durum(acik ? "Ana pencere her zaman üstte duracak." : "Ana pencere normal davranacak.", false);
            };

        }

        // =================================================================
        //  UYGULAMA: sürüm, güncellemeler, kurulum, kaldırma
        // =================================================================
        private void UygulamaSayfasiOlustur(Panel kap)
        {
            Baslik(kap, "Sürüm", 4);

            YuvarlakPanel surumKarti = KartKutusu(kap, 20, 32, 420, 44);
            lblUygulamaSurum = new Label
            {
                Text = "Sürüm " + UygulamaGuncelleme.Surum,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Tema.Yazi,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Location = new Point(14, 12),
                Size = new Size(270, 22)
            };
            surumKarti.Controls.Add(lblUygulamaSurum);
            btnUygulamaGuncelle = Dugme("\uE896", "GÜNCELLE", 296, 7, 110, 30, true, UygulamayiGuncelle);
            btnUygulamaGuncelle.Visible = false;
            surumKarti.Controls.Add(btnUygulamaGuncelle);

            Baslik(kap, "Güncellemeler", 86);

            AcKapa otoUyg = AnahtarSatiri(kap, 114, "Uygulama güncellemelerini sormadan kur",
                                          "Açılışta yeni sürüm varsa indirip kurar, sonra yenilikleri gösterir.",
                                          UygulamaAyarlari.OtomatikUygulamaGuncelle);
            otoUyg.Degisti += acik =>
            {
                UygulamaAyarlari.OtomatikUygulamaGuncelle = acik;
                Durum(acik ? "Uygulama güncellemeleri sormadan kurulacak." : "Uygulama güncellemelerinde sorulacak.", false);
            };

            AcKapa otoKart = AnahtarSatiri(kap, 166, "Kart güncellemelerini sormadan kur",
                                           "Bağlanınca kart yazılımı yeniyse klavyeye kendiliğinden yükler.",
                                           UygulamaAyarlari.OtomatikKartGuncelle);
            otoKart.Degisti += acik =>
            {
                UygulamaAyarlari.OtomatikKartGuncelle = acik;
                Durum(acik ? "Kart güncellemeleri sormadan yüklenecek." : "Kart güncellemelerinde sorulacak.", false);
            };

            Baslik(kap, "Kurulum", 226);

            bool kurulu = Kurulum.KuruluMu;
            kap.Controls.Add(new Label
            {
                Text = kurulu
                    ? "Bu bilgisayara kurulu: " + Kurulum.Klasor
                    : "Kurulu değil, şu dosyadan çalışıyor: " + Application.ExecutablePath,
                Font = kucukFont,
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Location = new Point(22, 252),
                Size = new Size(416, 34)
            });

            if (kurulu)
                kap.Controls.Add(Dugme("\uE71B", "KISAYOLLARI YENİLE", 20, 290, 205, 40, false, KisayollariYenile));
            else
                kap.Controls.Add(Dugme("\uE896", "BİLGİSAYARA KUR…", 20, 290, 205, 40, false, BilgisayaraKur));
            kap.Controls.Add(Dugme("\uE74D", "KALDIR…", 235, 290, 205, 40, false, Kaldir));

            // YAYIN HAZIRLA yalnızca geliştiricide görünür: yayın anahtarı bu bilgisayardaysa
            // ya da uygulama Visual Studio projesinin içinden (bin\Debug, bin\Release) çalışıyorsa.
            // (Görünse de gizli anahtar olmadan geçerli bir güncelleme hazırlanamaz.)
            bool gelistirici = File.Exists(UygulamaGuncelleme.AnahtarDosyasi) || UygulamaGuncelleme.ProjeKlasoru() != null;

            Baslik(kap, "Yardım", 348);
            kap.Controls.Add(Dugme("\uE897", gelistirici ? "TANITIMI GÖSTER" : "TANITIMI YENİDEN GÖSTER",
                                   20, 376, gelistirici ? 205 : 420, 40, false, () =>
            {
                RehberIstendi = true;
                Kapat();
            }));
            if (gelistirici)
                kap.Controls.Add(Dugme("\uE8A1", "YAYIN HAZIRLA…", 235, 376, 205, 40, false, YayinHazirla));
        }

        private void KisayollariYenile()
        {
            try
            {
                Kurulum.KisayollariOlustur();
                Kurulum.KaydiGuncelle();
                Durum("Masaüstü ve Başlat menüsü kısayolları yeniden oluşturuldu.", false);
            }
            catch (Exception ex)
            {
                Durum("Oluşturulamadı: " + ex.Message, true);
            }
        }

        private void BilgisayaraKur()
        {
            if (islemde) return;
            if (MessageBox.Show(this, "Makro Klavye bilgisayarına kurulsun mu?\n\n" +
                                      "Kendi klasörüne kopyalanır, masaüstüne ve Başlat menüsüne kısayol konur, " +
                                      "kurulu olan açılır ve bu dosya silinir.",
                                "Kurulum", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try { Kurulum.KurVeYenidenAc(); }
            catch (Exception ex) { Durum("Kurulamadı: " + ex.Message, true); }
        }

        private void Kaldir()
        {
            if (islemde) return;

            bool verileriSil;
            using (KaldirForm f = new KaldirForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                verileriSil = f.VerileriSil;
            }

            try { Kurulum.Kaldir(verileriSil); }
            catch (Exception ex) { Durum("Kaldırılamadı: " + ex.Message, true); }
        }

        // ---------- uygulama sürümü / güncelleme ----------
        private Label lblUygulamaSurum;
        private YuvarlakPanel btnUygulamaGuncelle;
        private UzaktakiUygulama uygulamaSurumu;

        private async Task UygulamaSurumunuGoster()
        {
            lblUygulamaSurum.Text = "Sürüm " + UygulamaGuncelleme.Surum;
            btnUygulamaGuncelle.Visible = false;
            if (!UygulamaGuncelleme.Ayarli) return;

            uygulamaSurumu = await UygulamaGuncelleme.DenetleAsync();
            if (IsDisposed || uygulamaSurumu == null) return;

            bool yeni = UygulamaGuncelleme.DahaYeni(uygulamaSurumu.Surum);
            lblUygulamaSurum.Text = "Sürüm " + UygulamaGuncelleme.Surum +
                                    (yeni ? "  ·  internette " + uygulamaSurumu.Surum : "  ·  güncel");
            lblUygulamaSurum.ForeColor = yeni ? Mavi : Tema.Yazi;
            btnUygulamaGuncelle.Visible = yeni;
        }

        private async void UygulamayiGuncelle()
        {
            if (islemde || uygulamaSurumu == null) return;

            if (MessageBox.Show(this,
                    "Makro Klavye " + uygulamaSurumu.Surum + " indirilip kurulacak, uygulama yeniden açılacak.\n\n" +
                    (string.IsNullOrEmpty(uygulamaSurumu.Not) ? "" : "Yenilikler: " + uygulamaSurumu.Not + "\n\n") +
                    "Ayarların, profillerin ve makroların korunur. Devam edilsin mi?",
                    "Uygulama güncellemesi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            if (UygulamaGuncelleme.KartGuncelleniyor)
            {
                Durum("Kart yazılımı güncelleniyor; bitince tekrar dene.", true);
                return;
            }
            if (UygulamaGuncelleme.UygulamaGuncelleniyor) return;

            IslemBasla("İndiriliyor…");
            UygulamaGuncelleme.UygulamaGuncelleniyor = true;
            try
            {
                byte[] exe = await UygulamaGuncelleme.IndirAsync(uygulamaSurumu);
                IslemBitti();
                if (!await UygulamaGuncelleme.KartGuncellemesiniBekle(() => Durum("Kart güncellemesinin bitmesi bekleniyor…", false)))
                    throw new InvalidOperationException("Kart güncellemesi bitmedi, sonra tekrar dene.");
                UygulamaGuncelleme.Kur(exe, uygulamaSurumu.Surum, uygulamaSurumu.Not);
            }
            catch (Exception ex)
            {
                UygulamaGuncelleme.UygulamaGuncelleniyor = false;
                IslemBitti();
                Durum("Güncellenemedi: " + ex.Message, true);
            }
        }

        // Geliştirici: exe'yi imzalayıp GitHub'a yüklenecek dosyaları hazırlar
        private void YayinHazirla()
        {
            if (islemde) return;

            try
            {
                if (!File.Exists(UygulamaGuncelleme.AnahtarDosyasi))
                {
                    if (MessageBox.Show(this,
                            "Güncellemeleri imzalamak için bu bilgisayarda bir anahtar oluşturulacak.\n\n" +
                            "Gizli anahtar yalnızca burada durur:\n" + UygulamaGuncelleme.AnahtarDosyasi + "\n" +
                            "Bunu kimseyle paylaşma ve bir kopyasını güvenli bir yerde (USB bellek gibi) sakla; " +
                            "kaybolursa yeni anahtarla uygulamayı herkese elle yeniden vermen gerekir.\n\nDevam edilsin mi?",
                            "Yayın anahtarı", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                        return;

                    bool yazildi;
                    string acik = UygulamaGuncelleme.AnahtarOlustur(out yazildi);
                    if (!yazildi)
                    {
                        try { Clipboard.SetText(acik); } catch (Exception) { }
                        MessageBox.Show(this, "Anahtar oluşturuldu ama proje klasörü bulunamadı. Açık anahtar panoya kopyalandı; " +
                                              "projedeki GuncellemeAnahtari.cs dosyasında Acik = \"\" içine yapıştır.",
                                        "Yayın anahtarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    MessageBox.Show(this, "Anahtar oluşturuldu ve projedeki GuncellemeAnahtari.cs dosyasına yazıldı.\n\n" +
                                          "Şimdi Visual Studio'da dosyayı yeniden yükle (Evet), Release seçip Build al. " +
                                          "Sonra o yeni sürümü açıp tekrar YAYIN HAZIRLA'ya bas.",
                                    "Yayın anahtarı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (GuncellemeAnahtari.Acik != UygulamaGuncelleme.AcikAnahtar())
                {
                    Durum("Bu uygulamada yayın anahtarı yok ya da farklı. Visual Studio'da Build alıp yeni sürümü aç.", true);
                    return;
                }

                string proje = UygulamaGuncelleme.ProjeKlasoru();
                string exeYolu;
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "Yayınlanacak exe'yi seç (Release)";
                    d.Filter = "Makro Klavye (MakroKeyboard.exe)|*.exe";
                    string release = proje != null ? Path.Combine(proje, "bin", "Release") : null;
                    if (release != null && Directory.Exists(release)) d.InitialDirectory = release;
                    d.FileName = "MakroKeyboard.exe";
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    exeYolu = d.FileName;
                }

                string surum = UygulamaGuncelleme.ExeSurumu(exeYolu);
                if (uygulamaSurumu != null && !UygulamaGuncelleme.Buyuk(surum, uygulamaSurumu.Surum) &&
                    MessageBox.Show(this, "Seçtiğin exe'nin sürümü (" + surum + ") GitHub'dakinden (" + uygulamaSurumu.Surum +
                                          ") yeni değil; kimseye güncelleme sorulmaz. Yine de hazırlansın mı?",
                                    "Yayın", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                string not = GirisKutusu.Sor(this, "Yayın notu", "Bu sürümde neler var? (güncelleme sorulurken gösterilir)",
                                             "Örn. Yeni simge, hata düzeltmeleri", "");
                if (not == null) return;

                string klasor = Path.Combine(proje != null ? Path.GetDirectoryName(proje) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                             "UygulamaYayini");
                string yayinSurumu = UygulamaGuncelleme.YayinHazirla(exeYolu, klasor, not);
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi, "Uygulama " + yayinSurumu + " yayını hazırlandı");

                System.Diagnostics.Process.Start("explorer.exe", "\"" + klasor + "\"");
                Durum("Hazır: " + klasor + " içindeki iki dosyayı GitHub'a yükle.", false);
            }
            catch (Exception ex)
            {
                Durum("Yayın hazırlanamadı: " + ex.Message, true);
            }
        }

        private AcKapa AnahtarSatiri(Control kap, int y, string baslik, string aciklama, bool acik)
        {
            YuvarlakPanel kart = new YuvarlakPanel
            {
                Location = new Point(20, y),
                Size = new Size(420, 44),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };

            kart.Controls.Add(new Label
            {
                Text = baslik,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Tema.Yazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(14, 4)
            });
            kart.Controls.Add(new Label
            {
                Text = aciklama,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(14, 24)
            });

            AcKapa a = new AcKapa { Location = new Point(420 - 62, 11), Acik = acik };
            kart.Controls.Add(a);
            kap.Controls.Add(kart);
            return a;
        }

        // =================================================================
        //  KART YAZILIMI
        // =================================================================
        private void KartSayfasiOlustur(Panel kap)
        {
            Baslik(kap, "Kart yazılımı", 4);

            YuvarlakPanel bilgi = KartKutusu(kap, 20, 32, 420, 148);

            bilgi.Controls.Add(BilgiEtiketi("Karttaki sürüm", 14, 12, false));
            lblKartSurum = BilgiEtiketi("-", 180, 12, true);
            bilgi.Controls.Add(lblKartSurum);

            bilgi.Controls.Add(BilgiEtiketi("İnternetteki sürüm", 14, 42, false));
            lblInternetSurum = BilgiEtiketi("-", 180, 42, true);
            bilgi.Controls.Add(lblInternetSurum);

            btnInternet = Dugme("\uE896", "GÜNCELLE", 296, 37, 110, 30, true, InternettenGuncelle);
            btnInternet.Visible = false;
            bilgi.Controls.Add(btnInternet);

            bilgi.Controls.Add(BilgiEtiketi("Seçilen dosyadaki", 14, 72, false));
            lblKlasorSurum = BilgiEtiketi("-", 180, 72, true);
            bilgi.Controls.Add(lblKlasorSurum);

            bilgi.Controls.Add(BilgiEtiketi("Dosya", 14, 102, false));
            lblKlasor = new Label
            {
                Font = kucukFont,
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Location = new Point(180, 104),
                Size = new Size(226, 36)
            };
            bilgi.Controls.Add(lblKlasor);

            kap.Controls.Add(Dugme("\uE8B7", "ZIP SEÇ…", 20, 192, 160, 42, false, KaynakSec));
            kap.Controls.Add(Dugme("\uE896", "ZIP'TEN YÜKLE", 190, 192, 250, 42, false, KartiGuncelle));

            ilerlemeCubugu = new YuvarlakPanel
            {
                Location = new Point(20, 246),
                Size = new Size(420, 10),
                Yaricap = 5,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar,
                Visible = false
            };
            ilerlemeDolgu = new Panel
            {
                Location = new Point(2, 2),
                Size = new Size(0, 6),
                BackColor = Mavi
            };
            ilerlemeCubugu.Controls.Add(ilerlemeDolgu);
            kap.Controls.Add(ilerlemeCubugu);

            kap.Controls.Add(new Label
            {
                Text = "Yeni sürüm internette çıkınca uygulama bağlanırken sorar. Elle yüklemek için zip seç; " +
                       "boot.py'ye ve tuş ayarlarına dokunulmaz. Sürüm görünmüyorsa dosyaları bir kez elle kopyala: " +
                       "klavyeyi 9 numaralı tuşa basılı tutarak tak, dosyaları CIRCUITPY sürücüsüne at.",
                Font = kucukFont,
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                Location = new Point(22, 262),
                Size = new Size(416, 64)
            });

            Baslik(kap, "Klavye", 330);
            kap.Controls.Add(Dugme("\uE9D9", "TANILAMA…", 20, 358, 205, 40, false, TanilamaAc));
            kap.Controls.Add(Dugme("\uE916", "KLAVYE AYARLARI…", 235, 358, 205, 40, false, BasisSureleriAc));
            kap.Controls.Add(Dugme("\uE72C", "FABRİKA AYARLARI…", 20, 406, 205, 40, false, FabrikaAyarlari));
            kap.Controls.Add(Dugme("\uE7A7", "ÖNCEKİ SÜRÜME DÖN…", 235, 406, 205, 40, false, OncekiSurumeDon));
        }

        // Kart 1.8 ve sonrası: TANI komutu, LONG_PRESS_MS / DOUBLE_PRESS_MS ayarları
        private const string GerekenKartSurumu = "1.8";

        private static bool SurumYeterli(string surum)
        {
            Version v, gereken = new Version(GerekenKartSurumu);
            return surum != null && Version.TryParse(surum, out v) && v >= gereken;
        }

        private async void TanilamaAc()
        {
            if (islemde) return;
            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return;
            }

            IslemBasla("Kart bilgileri okunuyor…");
            Dictionary<string, string> bilgi;
            try { bilgi = await sahip.KartTanilama(); }
            catch (Exception) { bilgi = null; }
            finally { IslemBitti(); }

            if (bilgi == null)
            {
                Durum("Kart tanılamayı desteklemiyor. Kart yazılımını " + GerekenKartSurumu + " ya da sonrasına güncelle.", true);
                return;
            }

            using (KartTanilamaForm f = new KartTanilamaForm(bilgi, () => sahip.KartTanilama()))
                f.ShowDialog(this);
            Durum("", false);
        }

        private async void BasisSureleriAc()
        {
            if (islemde) return;
            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan. Süreler klavyede saklanır.", true);
                return;
            }

            IslemBasla("Kart sürümü okunuyor…");
            string surum;
            try { surum = await sahip.KartSurumu(); }
            catch (Exception) { surum = null; }
            finally { IslemBitti(); }
            Durum("", false);

            if (!SurumYeterli(surum) &&
                MessageBox.Show(this, "Karttaki yazılım (" + (surum ?? "eski") + ") bu ayarları bilmiyor; " +
                                      "kaydedersen ancak kart yazılımını güncelleyince etkili olur.\n\n" +
                                      "Yine de açılsın mı?", "Klavye ayarları", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            Version kv;
            bool duzenDestekli = surum != null && Version.TryParse(surum, out kv) && kv >= new Version(2, 0);

            int uzun, cift;
            if (!int.TryParse(sahip.KartAyari("LONG_PRESS_MS"), out uzun)) uzun = BasisSureleriForm.VarsayilanUzun;
            if (!int.TryParse(sahip.KartAyari("DOUBLE_PRESS_MS"), out cift)) cift = BasisSureleriForm.VarsayilanCift;
            string duzen = KlavyeDuzeni.Coz(sahip.KartAyari("KLAVYE_DUZENI"));
            bool kart24 = surum != null && Version.TryParse(surum, out kv) && kv >= new Version(2, 4);   // titreşim, tık başına
            int titresim, tekrar;
            if (!int.TryParse(sahip.KartAyari("TITRESIM_MS"), out titresim)) titresim = 0;
            if (!int.TryParse(sahip.KartAyari("ENCODER_TEKRAR"), out tekrar)) tekrar = 1;
            bool yeniKart = surum != null && Version.TryParse(surum, out kv) && kv >= new Version(2, 3);   // Türkçe F, uyku
            bool harfDestekli = surum != null && Version.TryParse(surum, out kv) && kv >= new Version(2, 1);
            int harf;
            if (!int.TryParse(sahip.KartAyari("TEXT_DELAY_MS"), out harf)) harf = 0;

            bool encoderDestekli = surum != null && Version.TryParse(surum, out kv) && kv >= new Version(2, 2);
            bool ters = sahip.KartAyari("ENCODER_TERS") == "1";
            int adim;
            if (!int.TryParse(sahip.KartAyari("ENCODER_ADIM"), out adim)) adim = 1;

            bool uyku = sahip.KartAyari("UYKU_ENGEL") == "1";
            int uykuAralik;
            if (!int.TryParse(sahip.KartAyari("UYKU_ARALIK"), out uykuAralik)) uykuAralik = BasisSureleriForm.VarsayilanUykuAralik;

            int yeniUzun, yeniCift, yeniHarf, yeniAdim, yeniUykuAralik, yeniTitresim, yeniTekrar;
            string yeniDuzen;
            bool yeniTers, yeniUyku;
            using (BasisSureleriForm f = new BasisSureleriForm(uzun, cift, duzen, duzenDestekli, yeniKart, harf, harfDestekli,
                                                               ters, adim, encoderDestekli, uyku, uykuAralik, yeniKart,
                                                               titresim, tekrar, kart24))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                yeniUzun = f.Uzun;
                yeniCift = f.Cift;
                yeniDuzen = f.Duzen;
                yeniHarf = f.Harf;
                yeniTers = f.EncoderTers;
                yeniAdim = f.EncoderAdim;
                yeniUyku = f.Uyku;
                yeniUykuAralik = f.UykuAralik;
                yeniTitresim = f.Titresim;
                yeniTekrar = f.EncoderTekrar;
            }
            if (yeniUzun == uzun && yeniCift == cift && yeniDuzen == duzen && yeniHarf == harf &&
                yeniTers == ters && yeniAdim == adim && yeniUyku == uyku && yeniUykuAralik == uykuAralik &&
                yeniTitresim == titresim && yeniTekrar == tekrar) return;

            Dictionary<string, string> degerler = new Dictionary<string, string>
            {
                { "LONG_PRESS_MS", yeniUzun.ToString() },
                { "DOUBLE_PRESS_MS", yeniCift.ToString() }
            };
            if (duzenDestekli) degerler["KLAVYE_DUZENI"] = yeniDuzen;
            if (yeniKart)
            {
                degerler["UYKU_ENGEL"] = yeniUyku ? "1" : "0";
                degerler["UYKU_ARALIK"] = yeniUykuAralik.ToString();
            }
            if (kart24)
            {
                degerler["TITRESIM_MS"] = yeniTitresim.ToString();
                degerler["ENCODER_TEKRAR"] = yeniTekrar.ToString();
            }
            if (harfDestekli) degerler["TEXT_DELAY_MS"] = yeniHarf.ToString();
            if (encoderDestekli)
            {
                degerler["ENCODER_TERS"] = yeniTers ? "1" : "0";
                degerler["ENCODER_ADIM"] = yeniAdim.ToString();
            }

            string ozet = "uzun " + yeniUzun + " ms, çift " + yeniCift + " ms" +
                          (duzenDestekli ? ", düzen " + KlavyeDuzeni.Ad(yeniDuzen) : "") +
                          (harfDestekli ? ", harf arası " + yeniHarf + " ms" : "") +
                          (encoderDestekli ? ", encoder " + (yeniTers ? "ters, " : "") + yeniAdim + " tıkta bir" : "") +
                          (yeniKart ? ", uyku engelleyici " + (yeniUyku ? "açık (" + yeniUykuAralik + " sn)" : "kapalı") : "") +
                          (kart24 ? ", titreşim filtresi " + (yeniTitresim == 0 ? "kapalı" : yeniTitresim + " ms") +
                                    ", tık başına " + yeniTekrar + " kez" : "");

            IslemBasla("Kaydediliyor…");
            string hata;
            try { hata = await sahip.KartAyarlariniYaz(degerler, "Klavye ayarları", ozet); }
            catch (Exception ex) { hata = ex.Message; }
            finally { IslemBitti(); }

            if (hata != null) Durum(hata, true);
            else Durum("Klavye ayarları kaydedildi: " + ozet + ".", false);
        }

        private async void FabrikaAyarlari()
        {
            if (islemde) return;

            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return;
            }

            if (MessageBox.Show(this,
                    "Tüm tuş, encoder ve LED ayarları varsayılana dönecek, tuşlara atanmış uygulama kısayolları silinecek.\n\n" +
                    "Ana penceredeki Geri al ile bu işlemi geri alabilirsin. Devam edilsin mi?",
                    "Fabrika ayarları", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            IslemBasla("Fabrika ayarları yükleniyor…");

            string hata;
            try { hata = await sahip.FabrikaAyarlari(); }
            catch (Exception ex) { hata = ex.Message; }
            finally { IslemBitti(); }

            if (hata != null) Durum(hata, true);
            else
            {
                ListeyiYenile();
                Durum("Kart fabrika ayarlarına döndü.", false);
            }
        }

        private Label BilgiEtiketi(string metin, int x, int y, bool kalin)
        {
            return new Label
            {
                Text = metin,
                Font = kalin ? new Font("Segoe UI", 10f, FontStyle.Bold) : new Font("Segoe UI", 10f),
                ForeColor = kalin ? Tema.Yazi : Tema.SolukYazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(x, y)
            };
        }

        // Seçili zip / klasördeki kart dosyaları; okunamazsa null ve hata metni
        private static Dictionary<string, byte[]> PaketOku(out string hata)
        {
            hata = null;
            try { return KartPaketi.Oku(UygulamaAyarlari.KartKlasoru); }
            catch (Exception ex)
            {
                hata = ex.Message;
                return null;
            }
        }

        private async void KartBilgisiniYenile()
        {
            string kaynak = UygulamaAyarlari.KartKlasoru;
            lblKlasor.Text = kaynak;

            // code.py varsa sürümü; yoksa bulunan diğer kart dosyaları gösterilir
            // (yalnızca led_animations.py'yi güncellemek de mümkün)
            string okumaHatasi;
            Dictionary<string, byte[]> paket = PaketOku(out okumaHatasi) ?? new Dictionary<string, byte[]>();
            string yerel = KartPaketi.Surum(paket);
            List<string> bulunan = KartDosyalari.Where(paket.ContainsKey).ToList();

            if (okumaHatasi != null)
            {
                lblKlasorSurum.Text = "zip okunamadı";
                lblKlasorSurum.ForeColor = Tema.HataYazi;
                Durum(okumaHatasi, true);
            }
            else if (yerel != null)
            {
                lblKlasorSurum.Text = yerel + (bulunan.Count < KartDosyalari.Length
                    ? "  (" + bulunan.Count + "/" + KartDosyalari.Length + " dosya)" : "");
                lblKlasorSurum.ForeColor = Tema.Yazi;
            }
            else if (bulunan.Count == 1)
            {
                lblKlasorSurum.Text = "yalnızca " + bulunan[0];
                lblKlasorSurum.ForeColor = Tema.Yazi;
            }
            else if (bulunan.Count > 1)
            {
                lblKlasorSurum.Text = bulunan.Count + " dosya (code.py yok)";
                lblKlasorSurum.ForeColor = Tema.Yazi;
            }
            else
            {
                lblKlasorSurum.Text = "dosya seçilmedi";
                lblKlasorSurum.ForeColor = Tema.HataYazi;
            }

            if (!sahip.KlavyeBagli)
            {
                lblKartSurum.Text = "klavye bağlı değil";
                lblKartSurum.ForeColor = Tema.SolukYazi;
                await InternetSurumunuGoster(null);
                return;
            }

            lblKartSurum.Text = "okunuyor…";
            string kart = null;
            try { kart = await sahip.KartSurumu(); }
            catch (Exception) { }

            if (IsDisposed) return;

            lblKartSurum.Text = kart ?? "bilinmiyor (eski yazılım)";
            bool guncel = kart != null && yerel != null && kart == yerel;
            lblKartSurum.ForeColor = kart == null ? Tema.HataYazi : (guncel ? Tema.OkYazi : Tema.Yazi);

            await InternetSurumunuGoster(kart);
        }

        private async Task InternetSurumunuGoster(string kart)
        {
            btnInternet.Visible = false;

            if (!KartGuncelleme.Ayarli)
            {
                lblInternetSurum.Text = "ayarlanmadı";
                lblInternetSurum.ForeColor = Tema.SolukYazi;
                return;
            }

            lblInternetSurum.Text = "denetleniyor…";
            lblInternetSurum.ForeColor = Tema.SolukYazi;

            internetSurumu = await KartGuncelleme.DenetleAsync();
            if (IsDisposed) return;

            if (internetSurumu == null)
            {
                lblInternetSurum.Text = "ulaşılamadı";
                lblInternetSurum.ForeColor = Tema.SolukYazi;
                return;
            }

            bool bagli = sahip.KlavyeBagli;
            bool yeni = bagli && KartGuncelleme.DahaYeni(internetSurumu.Surum, kart);
            lblInternetSurum.Text = internetSurumu.Surum + (!bagli ? "" : yeni ? "  (yeni)" : "  (güncel)");
            lblInternetSurum.ForeColor = yeni ? Mavi : Tema.OkYazi;
            btnInternet.Visible = yeni && sahip.KlavyeBagli;
        }

        private async void InternettenGuncelle()
        {
            if (islemde || internetSurumu == null) return;

            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return;
            }

            if (MessageBox.Show(this,
                    "Kart yazılımı " + internetSurumu.Surum + " indirilip klavyeye yüklenecek, sonra kart yeniden başlayacak.\n\n" +
                    (string.IsNullOrEmpty(internetSurumu.Not) ? "" : "Yenilikler: " + internetSurumu.Not + "\n\n") +
                    "Tuş ayarların korunur. İşlem bitene kadar USB kablosunu çıkarma. Devam edilsin mi?",
                    "İnternetten güncelle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            IslemBasla("İndiriliyor…");
            Dictionary<string, byte[]> paket;
            try { paket = await KartGuncelleme.IndirAsync(internetSurumu); }
            catch (Exception ex)
            {
                IslemBitti();
                Durum(ex.Message, true);
                return;
            }
            IslemBitti();

            await PaketiYukle(paket, "Kart " + internetSurumu.Surum + " sürümüne güncellendi ve yeniden başladı.");
        }

        // Zip (ya da tek bir .py) seç; .py seçilirse bulunduğu klasördeki kart dosyaları kullanılır
        private void KaynakSec()
        {
            if (islemde) return;

            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Kart yazılımının zip dosyasını seç";
                d.Filter = "Kart yazılımı (*.zip;*.py)|*.zip;*.py|Zip dosyası (*.zip)|*.zip|Python dosyası (*.py)|*.py";

                string simdiki = UygulamaAyarlari.KartKlasoru;
                if (File.Exists(simdiki)) d.InitialDirectory = Path.GetDirectoryName(simdiki);
                else if (Directory.Exists(simdiki)) d.InitialDirectory = simdiki;

                if (d.ShowDialog(this) != DialogResult.OK) return;

                UygulamaAyarlari.KartKlasoru = d.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? d.FileName
                    : Path.GetDirectoryName(d.FileName);
                KartBilgisiniYenile();
            }
        }

        private async void KartiGuncelle()
        {
            if (islemde) return;

            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return;
            }

            string okumaHatasi;
            Dictionary<string, byte[]> paket = PaketOku(out okumaHatasi);
            if (okumaHatasi != null)
            {
                Durum(okumaHatasi, true);
                return;
            }

            List<string> mevcut = KartDosyalari.Where(paket.ContainsKey).ToList();
            if (mevcut.Count == 0)
            {
                Durum("Seçilen dosyada kart dosyası yok (code.py, macro_keys.py, led_animations.py, settings_server.py). " +
                      "Önce ZIP SEÇ ile kart yazılımının zip'ini seç.", true);
                return;
            }

            if (MessageBox.Show(this,
                    "Karta şu dosyalar gönderilecek ve kart yeniden başlatılacak:\n\n  " +
                    string.Join("\n  ", mevcut) + "\n\nİşlem bitene kadar USB kablosunu çıkarma. Devam edilsin mi?",
                    "Kartı güncelle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            await PaketiYukle(paket, "Kart güncellendi ve yeniden başladı.");
        }

        // Kart dosyalarını ilerleme çubuğuyla karta yükler
        private async Task PaketiYukle(Dictionary<string, byte[]> paket, string basariMesaji)
        {
            IslemBasla("Güncelleme başlıyor…");
            ilerlemeCubugu.Visible = true;
            Ilerleme(0);

            string hata;
            try
            {
                hata = await sahip.KartiGuncelle(paket, (metin, yuzde) =>
                {
                    if (IsDisposed) return;
                    Durum(metin, false);
                    Ilerleme(yuzde);
                });
            }
            catch (Exception ex) { hata = ex.Message; }
            finally { IslemBitti(); }

            ilerlemeCubugu.Visible = false;

            if (hata != null)
            {
                Durum(hata, true);
            }
            else
            {
                KartBilgisiniYenile();
                Durum(basariMesaji, false);
            }
        }

        // Son güncellemeden önceki kart yazılımına döner (kart güncellemeden önce eskisini saklar, 2.3+)
        private async void OncekiSurumeDon()
        {
            if (islemde) return;
            if (!sahip.KlavyeBagli)
            {
                Durum("Önce klavyeye bağlan.", true);
                return;
            }

            string yedek, simdiki;
            IslemBasla("Kartta önceki sürüm aranıyor…");
            try
            {
                simdiki = await sahip.KartSurumu();
                yedek = await sahip.KartYedekSurumu();
            }
            catch (Exception ex)
            {
                IslemBitti();
                Durum(ex.Message, true);
                return;
            }
            IslemBitti();

            if (yedek == null)
            {
                Durum("Bunun için kart yazılımı 2.4 gerekir. Önceki sürüme dönmek istersen ZIP SEÇ ile eski zip'i yükle.", true);
                return;
            }
            if (yedek.Length == 0)
            {
                Durum("Kartta önceki sürüm yok: yedek, kart uygulamadan güncellenince alınır (bir kez geri dönülünce silinir).", true);
                return;
            }

            if (MessageBox.Show(this,
                    "Kart yazılımı " + (simdiki ?? "?") + " sürümünden önceki " + yedek + " sürümüne dönsün mü?\n\n" +
                    "Tuş atamaların ve ayarların değişmez. Yeni sürümle gelen özellikler (ör. yeni ayarlar) çalışmaz.\n" +
                    "İstersen sonra yeniden güncelleyebilirsin.",
                    "Önceki sürüme dön", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            IslemBasla("Önceki sürüme dönülüyor…");
            ilerlemeCubugu.Visible = true;
            Ilerleme(50);
            string hata;
            try
            {
                hata = await sahip.KartiGeriAl(yedek, (metin, yuzde) =>
                {
                    if (IsDisposed) return;
                    Durum(metin, false);
                    Ilerleme(yuzde);
                });
            }
            catch (Exception ex) { hata = ex.Message; }
            finally { IslemBitti(); }
            ilerlemeCubugu.Visible = false;

            if (hata != null) Durum(hata, true);
            else
            {
                KartBilgisiniYenile();
                Durum("Kart yazılımı " + yedek + " sürümüne döndü.", false);
            }
        }

        private void Ilerleme(int yuzde)
        {
            yuzde = Math.Max(0, Math.Min(100, yuzde));
            ilerlemeDolgu.Width = (ilerlemeCubugu.Width - 4) * yuzde / 100;
        }

        // =================================================================
        //  İSTATİSTİK
        // =================================================================
        private void IstatistikSayfasiOlustur(Panel kap)
        {
            Baslik(kap, "Tuş kullanımı", 4);
            kap.Controls.Add(Dugme("\uE72C", "SIFIRLA", 334, 0, 106, 32, false, IstatistikSifirla));

            YuvarlakPanel kart = KartKutusu(kap, 20, 40, 420, 420);

            istatistikAlani = new CiftTamponPanel
            {
                Location = new Point(8, 8),
                Size = new Size(404, 404),
                BackColor = Tema.Kart
            };
            istatistikAlani.Paint += IstatistikCiz;
            kart.Controls.Add(istatistikAlani);
        }

        private void IstatistigiYenile()
        {
            istatistik = sahip.IstatistikSatirlari() ?? new List<IstatistikSatiri>();
            istatistikAlani.Invalidate();
        }

        private void IstatistikCiz(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int toplam = istatistik.Sum(x => x.Sayi);
            if (toplam == 0)
            {
                using (var b = new SolidBrush(Tema.SolukYazi))
                {
                    StringFormat ortala = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("Henüz sayım yok.\nUygulama açıkken tuşlara bastıkça burası dolacak.",
                                 new Font("Segoe UI", 10f), b, istatistikAlani.ClientRectangle, ortala);
                }
                return;
            }

            int enCok = Math.Max(1, istatistik.Max(x => x.Sayi));
            int satirH = 36;
            int w = istatistikAlani.Width;

            StringFormat sol = new StringFormat
            {
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            StringFormat sag = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Near
            };

            using (Font ad = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (Font aciklama = new Font("Segoe UI", 8f))
            using (Font sayi = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var yazi = new SolidBrush(Tema.Yazi))
            using (var soluk = new SolidBrush(Tema.SolukYazi))
            using (var iz = new SolidBrush(Tema.Hover))
            using (var cubuk = new SolidBrush(Mavi))
            {
                for (int i = 0; i < istatistik.Count; i++)
                {
                    IstatistikSatiri s = istatistik[i];
                    int y = 2 + i * satirH;

                    g.DrawString(s.Ad, ad, yazi, new RectangleF(6, y, 110, 18), sol);
                    g.DrawString(s.Aciklama, aciklama, soluk, new RectangleF(118, y + 2, w - 190, 16), sol);
                    g.DrawString(s.Sayi.ToString(), sayi, yazi, new RectangleF(w - 70, y - 1, 64, 20), sag);

                    RectangleF zemin = new RectangleF(6, y + 20, w - 12, 6);
                    using (var p = YuvarlakPanel.Yol(zemin, 3))
                        g.FillPath(iz, p);

                    float dolu = (w - 12) * s.Sayi / (float)enCok;
                    if (dolu >= 6)
                    {
                        using (var p = YuvarlakPanel.Yol(new RectangleF(6, y + 20, dolu, 6), 3))
                            g.FillPath(cubuk, p);
                    }

                    if (!string.IsNullOrEmpty(s.Detay))
                        g.DrawString(s.Detay, aciklama, soluk, new RectangleF(w - 190, y + 2, 116, 16), sag);
                }
            }
        }

        private void IstatistikSifirla()
        {
            if (MessageBox.Show(this, "Tüm tuş sayaçları sıfırlansın mı?", "İstatistik",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            sahip.IstatistikSifirla();
            IstatistigiYenile();
            Durum("Sayaçlar sıfırlandı.", false);
        }

        // =================================================================
        //  GEÇMİŞ (otomatik yedekler)
        // =================================================================
        private void GecmisSayfasiOlustur(Panel kap)
        {
            // Birden çok klavye varsa hangisinin yedekleri olduğu başlıkta yazar
            bool cokKlavye = UygulamaAyarlari.Klavyeler().Count > 1 && UygulamaAyarlari.AktifKlavye.Length > 0;
            Label gecmisBaslik = Baslik(kap, cokKlavye ? "Otomatik yedekler · " + UygulamaAyarlari.KlavyeAdi(UygulamaAyarlari.AktifKlavye)
                                                       : "Otomatik yedekler", 4);
            if (cokKlavye)
            {
                // Başka bir klavyenin son hâlini bu klavyeye aktar
                gecmisBaslik.AutoSize = false;
                gecmisBaslik.AutoEllipsis = true;
                gecmisBaslik.Size = new Size(250, 24);
                YuvarlakPanel aktar = null;
                aktar = Dugme("\uE8AB", "DİĞER KLAVYEDEN AL…", 276, 0, 164, 28, false, () => DigerKlavyedenMenu(aktar));
                kap.Controls.Add(aktar);
            }

            kap.Controls.Add(new Label
            {
                Text = "Klavyedeki ayarlar her değiştiğinde (ve bağlanınca) o anki durum saklanır. " +
                       "Son " + UygulamaAyarlari.GecmisSiniri + " tanesi tutulur" +
                       (cokKlavye ? "; her klavyenin yedekleri ayrıdır (bağlı / son bağlanan gösterilir)." : "."),
                Font = kucukFont,
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                Location = new Point(22, 30),
                Size = new Size(416, 34)
            });

            YuvarlakPanel kart = KartKutusu(kap, 20, 68, 420, 178);

            gecmisListe = new AkiciListe
            {
                Location = new Point(6, 8),
                Size = new Size(408, 162),
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 50,
                IntegralHeight = false
            };
            gecmisListe.DrawItem += GecmisCiz;
            gecmisListe.SelectedIndexChanged += (s, e) =>
            {
                gecmisListe.Invalidate();
                FarkiGoster();
            };
            gecmisListe.DoubleClick += (s, e) => GecmiseDon();

            lblGecmisBos = new Label
            {
                Text = "Henüz otomatik yedek yok.\nKlavyeye bağlanıp bir ayar kaydedince burada görünür.",
                Location = new Point(6, 8),
                Size = new Size(408, 162),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Tema.SolukYazi,
                BackColor = Tema.Kart,
                Font = new Font("Segoe UI", 10f)
            };

            kart.Controls.Add(gecmisListe);
            kart.Controls.Add(lblGecmisBos);
            lblGecmisBos.BringToFront();

            // Seçili yedeğe dönülürse neyin değişeceği
            lblFarkBaslik = Baslik(kap, "Bu yedeğe dönersen değişecekler", 254);
            lblFarkBaslik.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            YuvarlakPanel farkKart = KartKutusu(kap, 20, 278, 420, 114);
            txtFark = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Tema.Kart,
                ForeColor = Tema.Yazi,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(12, 8),
                Size = new Size(400, 98),
                TabStop = false,
                Text = "Bir yedek seç; şimdiki hâlden farkı burada görünür."
            };
            farkKart.Controls.Add(txtFark);

            kap.Controls.Add(Dugme("\uE777", "BU HÂLE DÖN", 20, 400, 205, 42, true, GecmiseDon));
            kap.Controls.Add(Dugme("\uE74E", "PROFİL OLARAK SAKLA…", 235, 400, 205, 42, false, GecmistenProfil));
        }

        private void FarkiGoster()
        {
            GecmisOgesi o = SeciliGecmis();
            if (o == null)
            {
                txtFark.Text = "Bir yedek seç; şimdiki hâlden farkı burada görünür.";
                return;
            }

            List<string> fark = null;
            try { fark = sahip.ProfilFarki(UygulamaAyarlari.DosyaOku(o.Yol)); }
            catch (Exception ex)
            {
                txtFark.Text = "Yedek okunamadı: " + ex.Message;
                return;
            }

            if (fark == null)
                txtFark.Text = "Farkı görmek için klavyeye bağlan.";
            else if (fark.Count == 0)
                txtFark.Text = "Hiçbir şey değişmez: bu yedek şimdiki hâlle aynı.";
            else
                txtFark.Text = string.Join("\r\n", fark.Select(s => "•  " + s));

            txtFark.SelectionStart = 0;
            txtFark.SelectionLength = 0;
        }

        private void GecmisiYenile()
        {
            gecmis = UygulamaAyarlari.GecmisListesi();

            gecmisListe.BeginUpdate();
            gecmisListe.Items.Clear();
            foreach (GecmisOgesi g in gecmis) gecmisListe.Items.Add(g.Yol);
            gecmisListe.EndUpdate();

            lblGecmisBos.Visible = gecmis.Count == 0;
            FarkiGoster();
        }

        internal static string ZamanYazisi(DateTime z)
        {
            System.Globalization.CultureInfo tr = new System.Globalization.CultureInfo("tr-TR");
            if (z.Date == DateTime.Today) return "Bugün " + z.ToString("HH:mm", tr);
            if (z.Date == DateTime.Today.AddDays(-1)) return "Dün " + z.ToString("HH:mm", tr);
            return z.ToString("d MMMM yyyy, HH:mm", tr);
        }

        private void GecmisCiz(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= gecmis.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var zemin = new SolidBrush(Tema.Kart))
                g.FillRectangle(zemin, e.Bounds);

            GecmisOgesi o = gecmis[e.Index];
            bool secili = (e.State & DrawItemState.Selected) != 0;
            Rectangle kutu = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4);

            if (secili)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(kutu.X, kutu.Y, kutu.Width - 1, kutu.Height - 1), 10))
                using (var dolgu = new SolidBrush(Tema.Secili))
                using (var kalem = new Pen(Mavi, 1.5f))
                {
                    g.FillPath(dolgu, yol);
                    g.DrawPath(kalem, yol);
                }
            }

            StringFormat ortala = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using (Font ikonFont = new Font("Segoe MDL2 Assets", 13f))
            using (var b = new SolidBrush(e.Index == 0 ? Mavi : Tema.SolukYazi))
                g.DrawString("\uE81C", ikonFont, b, new RectangleF(kutu.X + 8, kutu.Y, 32, kutu.Height), ortala);

            StringFormat sol = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            int sagBosluk = e.Index == 0 ? 96 : 14;

            using (var b = new SolidBrush(Tema.Yazi))
                g.DrawString(o.Aciklama.Length > 0 ? o.Aciklama : "Kayıt", adFont, b,
                             new RectangleF(kutu.X + 46, kutu.Y + 6, kutu.Width - 46 - sagBosluk, 20), sol);
            using (var b = new SolidBrush(Tema.SolukYazi))
                g.DrawString(ZamanYazisi(o.Zaman), kucukFont, b,
                             new RectangleF(kutu.X + 46, kutu.Y + 26, kutu.Width - 46 - sagBosluk, 18), sol);

            if (e.Index == 0)
            {
                RectangleF rozet = new RectangleF(kutu.Right - 86, kutu.Y + (kutu.Height - 22) / 2f, 74, 22);
                using (var yol = YuvarlakPanel.Yol(rozet, 11))
                using (var b = new SolidBrush(Tema.OkDolgu))
                    g.FillPath(b, yol);
                using (var b = new SolidBrush(Tema.OkYazi))
                    g.DrawString("EN SON", etiketFont, b, rozet, ortala);
            }
        }

        // =================================================================
        //  GÜNLÜK: klavyeye yazılanlar, bağlantı, uyarılar
        // =================================================================
        private void GunlukSayfasiOlustur(Panel kap)
        {
            Baslik(kap, "Olay günlüğü", 4);

            kap.Controls.Add(new Label
            {
                Text = "Klavyeye ne zaman ne yazıldığı, bağlantı olayları ve kaydedilmeden atılan değişiklikler. " +
                       "Son " + UygulamaAyarlari.GunlukSiniri + " olay tutulur.",
                Font = kucukFont,
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent,
                Location = new Point(22, 30),
                Size = new Size(416, 34)
            });

            YuvarlakPanel kart = KartKutusu(kap, 20, 68, 420, 322);

            gunlukListe = new AkiciListe
            {
                Location = new Point(6, 8),
                Size = new Size(408, 306),
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44,
                IntegralHeight = false
            };
            gunlukListe.DrawItem += GunlukCiz;
            gunlukListe.SelectedIndexChanged += (s, e) => gunlukListe.Invalidate();

            // Sığmayan uzun satırın tamamı, üstüne gelince görünsün
            gunlukListe.MouseMove += (s, e) =>
            {
                int i = gunlukListe.IndexFromPoint(e.Location);
                if (i >= gunluk.Count) i = -1;
                if (i == gunlukIpucuSatiri) return;

                gunlukIpucuSatiri = i;
                gunlukIpucu.SetToolTip(gunlukListe, i >= 0 ? gunluk[i].Olay : "");
            };
            gunlukListe.MouseLeave += (s, e) => gunlukIpucuSatiri = -1;

            lblGunlukBos = new Label
            {
                Text = "Henüz kayıtlı olay yok.\nKlavyeye bağlanıp bir ayar kaydedince burada görünür.",
                Location = new Point(6, 8),
                Size = new Size(408, 306),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Tema.SolukYazi,
                BackColor = Tema.Kart,
                Font = new Font("Segoe UI", 10f)
            };

            kart.Controls.Add(gunlukListe);
            kart.Controls.Add(lblGunlukBos);
            lblGunlukBos.BringToFront();

            kap.Controls.Add(Dugme("\uE8C8", "KOPYALA", 20, 400, 205, 42, false, GunluguKopyala));
            kap.Controls.Add(Dugme("\uE74D", "TEMİZLE", 235, 400, 205, 42, false, GunluguTemizle));
        }

        private void GunluguYenile()
        {
            gunluk = UygulamaAyarlari.GunlukOku();

            gunlukListe.BeginUpdate();
            gunlukListe.Items.Clear();
            for (int i = 0; i < gunluk.Count; i++) gunlukListe.Items.Add(i);
            gunlukListe.EndUpdate();

            gunlukIpucuSatiri = -1;
            lblGunlukBos.Visible = gunluk.Count == 0;
        }

        private static string GunlukZamani(DateTime z)
        {
            System.Globalization.CultureInfo tr = new System.Globalization.CultureInfo("tr-TR");
            if (z.Date == DateTime.Today) return "Bugün " + z.ToString("HH:mm:ss", tr);
            if (z.Date == DateTime.Today.AddDays(-1)) return "Dün " + z.ToString("HH:mm:ss", tr);
            return z.ToString("d MMMM yyyy, HH:mm:ss", tr);
        }

        private void GunlukCiz(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= gunluk.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var zemin = new SolidBrush(Tema.Kart))
                g.FillRectangle(zemin, e.Bounds);

            GunlukOgesi o = gunluk[e.Index];
            bool secili = (e.State & DrawItemState.Selected) != 0;
            Rectangle kutu = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height - 4);

            if (secili)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(kutu.X, kutu.Y, kutu.Width - 1, kutu.Height - 1), 10))
                using (var dolgu = new SolidBrush(Tema.Secili))
                using (var kalem = new Pen(Mavi, 1.5f))
                {
                    g.FillPath(dolgu, yol);
                    g.DrawPath(kalem, yol);
                }
            }

            // Türüne göre simge ve renk
            string ikon;
            Color renk;
            if (o.Tur == UygulamaAyarlari.GunlukYazma) { ikon = "\uE74E"; renk = Mavi; }
            else if (o.Tur == UygulamaAyarlari.GunlukBaglanti)
            {
                ikon = "\uE88E";
                renk = o.Olay.IndexOf("koptu", StringComparison.OrdinalIgnoreCase) >= 0
                    ? Color.FromArgb(220, 60, 60) : Color.FromArgb(34, 160, 80);
            }
            else if (o.Tur == UygulamaAyarlari.GunlukUyari) { ikon = "\uE7BA"; renk = Color.FromArgb(217, 119, 6); }
            else { ikon = "\uE946"; renk = Tema.SolukYazi; }

            StringFormat ortala = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using (Font ikonFont = new Font("Segoe MDL2 Assets", 12f))
            using (var b = new SolidBrush(renk))
                g.DrawString(ikon, ikonFont, b, new RectangleF(kutu.X + 6, kutu.Y, 30, kutu.Height), ortala);

            StringFormat sol = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            using (Font olayFont = new Font("Segoe UI", 9.5f))
            using (var b = new SolidBrush(Tema.Yazi))
                g.DrawString(o.Olay, olayFont, b, new RectangleF(kutu.X + 42, kutu.Y + 4, kutu.Width - 52, 19), sol);
            using (var b = new SolidBrush(Tema.SolukYazi))
                g.DrawString(GunlukZamani(o.Zaman), kucukFont, b, new RectangleF(kutu.X + 42, kutu.Y + 22, kutu.Width - 52, 16), sol);
        }

        private void GunluguKopyala()
        {
            if (gunluk.Count == 0)
            {
                Durum("Günlük boş.", false);
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (GunlukOgesi o in gunluk)
                sb.Append(o.Zaman.ToString("dd.MM.yyyy HH:mm:ss")).Append("  ").Append(o.Olay).Append("\r\n");

            try
            {
                Clipboard.SetText(sb.ToString());
                Durum(gunluk.Count + " olay panoya kopyalandı.", false);
            }
            catch (Exception ex)
            {
                Durum("Kopyalanamadı: " + ex.Message, true);
            }
        }

        private void GunluguTemizle()
        {
            if (gunluk.Count == 0) return;

            if (MessageBox.Show(this, "Olay günlüğündeki " + gunluk.Count + " kayıt silinsin mi?", "Günlük",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                UygulamaAyarlari.GunlukTemizle();
                GunluguYenile();
                Durum("Günlük temizlendi.", false);
            }
            catch (Exception ex)
            {
                Durum("Temizlenemedi: " + ex.Message, true);
            }
        }

        private GecmisOgesi SeciliGecmis()
        {
            int i = gecmisListe.SelectedIndex;
            return (i >= 0 && i < gecmis.Count) ? gecmis[i] : null;
        }

        private async void GecmiseDon()
        {
            if (islemde) return;

            GecmisOgesi o = SeciliGecmis();
            if (o == null)
            {
                Durum("Listeden bir yedek seç.", true);
                return;
            }

            if (MessageBox.Show(this,
                    "Klavye, " + ZamanYazisi(o.Zaman) + " tarihindeki hâline dönsün mü?\n\n" +
                    (o.Aciklama.Length > 0 ? "(" + o.Aciklama + " sonrası)\n\n" : "") +
                    "Şimdiki hâli de Geçmiş'e eklenir, istersen ona da dönebilirsin.",
                    "Geçmişe dön", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string metin;
            try { metin = UygulamaAyarlari.DosyaOku(o.Yol); }
            catch (Exception ex)
            {
                Durum("Yedek okunamadı: " + ex.Message, true);
                return;
            }

            if (await KartaUygula(metin, "Yedek yükleniyor..."))
            {
                UygulamaAyarlari.AktifProfil = "";
                ListeyiYenile();
                GecmisiYenile();
                Durum("Klavye " + ZamanYazisi(o.Zaman) + " tarihindeki hâline döndü.", false);
            }
        }

        // Bilinen diğer klavyeler: adı ve son yedeğinin tarihi
        private void DigerKlavyedenMenu(Control kaynak)
        {
            if (islemde) return;

            ContextMenuStrip m = TemaMenuRenkleri.Menu();
            foreach (string kimlik in UygulamaAyarlari.Klavyeler())
            {
                if (kimlik == UygulamaAyarlari.AktifKlavye) continue;
                List<GecmisOgesi> g = UygulamaAyarlari.KlavyeGecmisi(kimlik);
                string ad = UygulamaAyarlari.KlavyeAdi(kimlik);
                if (g.Count == 0)
                {
                    ToolStripMenuItem bos = TemaMenuRenkleri.Oge(ad + "  (yedeği yok)", () => { });
                    bos.Enabled = false;
                    m.Items.Add(bos);
                    continue;
                }
                GecmisOgesi son = g[0];
                m.Items.Add(TemaMenuRenkleri.Oge(ad + "  ·  son hâli: " + ZamanYazisi(son.Zaman), () => DigerKlavyedenAl(ad, son)));
            }
            if (m.Items.Count == 0)
            {
                m.Dispose();
                Durum("Başka klavye yok.", true);
                return;
            }
            m.Closed += (x, y) => BeginInvoke(new Action(m.Dispose));
            m.Show(kaynak, new Point(0, kaynak.Height));
        }

        private async void DigerKlavyedenAl(string ad, GecmisOgesi o)
        {
            if (islemde) return;
            string buKlavye = UygulamaAyarlari.KlavyeAdi(UygulamaAyarlari.AktifKlavye);

            if (MessageBox.Show(this,
                    "\"" + ad + "\" klavyesinin son hâli (" + ZamanYazisi(o.Zaman) + ") bu klavyeye (\"" + buKlavye + "\") yazılsın mı?\n\n" +
                    "Tüm tuş atamaları, makrolar ve LED ayarı kopyalanır. Bu klavyenin şimdiki hâli Geçmiş'te kalır, " +
                    "istersen ona geri dönebilirsin.",
                    "Diğer klavyeden al", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string metin;
            try { metin = UygulamaAyarlari.DosyaOku(o.Yol); }
            catch (Exception ex)
            {
                Durum("Yedek okunamadı: " + ex.Message, true);
                return;
            }

            if (await KartaUygula(metin, "\"" + ad + "\" aktarılıyor..."))
            {
                UygulamaAyarlari.AktifProfil = "";
                ListeyiYenile();
                GecmisiYenile();
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukYazma, "\"" + ad + "\" klavyesinin ayarları bu klavyeye aktarıldı");
                Durum("\"" + ad + "\" klavyesinin ayarları bu klavyeye aktarıldı.", false);
            }
        }

        private void GecmistenProfil()
        {
            if (islemde) return;

            GecmisOgesi o = SeciliGecmis();
            if (o == null)
            {
                Durum("Listeden bir yedek seç.", true);
                return;
            }

            string ad = GirisKutusu.Sor(this, "Profil olarak sakla",
                "Bu yedeği bir profil olarak kaydet. Profiller sekmesinden istediğin zaman yükleyebilirsin.",
                "Profil adı", "",
                a => UygulamaAyarlari.AdTemizle(a).Length == 0 ? "Profil için bir ad yaz." : null);
            if (ad == null) return;

            ad = UygulamaAyarlari.AdTemizle(ad);

            if (File.Exists(UygulamaAyarlari.ProfilYolu(ad)) &&
                MessageBox.Show(this, "\"" + ad + "\" profili zaten var. Üzerine yazılsın mı?", "Profil",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                UygulamaAyarlari.ProfilYaz(ad, UygulamaAyarlari.DosyaOku(o.Yol));
                ListeyiYenile(ad);
                Durum("\"" + ad + "\" profili kaydedildi (Profiller sekmesinde).", false);
            }
            catch (Exception ex)
            {
                Durum("Kaydedilemedi: " + ex.Message, true);
            }
        }

        // =================================================================
        //  ALT
        // =================================================================
        private void AltOlustur()
        {
            lblDurum = new Label
            {
                Location = new Point(22, 596),
                Size = new Size(306, 54),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Tema.SolukYazi,
                BackColor = Color.Transparent
            };
            Controls.Add(lblDurum);

            btnKapat = Dugme(null, "KAPAT", 344, 604, 96, 42, true, () => Kapat());
            Controls.Add(btnKapat);
        }

        private void IslemBasla(string mesaj)
        {
            islemde = true;
            Cursor = Cursors.WaitCursor;
            Durum(mesaj, false);
        }

        private void IslemBitti()
        {
            islemde = false;
            Cursor = Cursors.Default;
        }

        private void Durum(string metin, bool hata)
        {
            if (lblDurum == null) return;
            lblDurum.Text = metin;
            lblDurum.ForeColor = hata ? Tema.HataYazi : Tema.SolukYazi;
        }

        // Bir panel ve tüm alt kontrollerine tıklama + hover bağlar
        private static void Bagla(YuvarlakPanel p, Action tik)
        {
            EventHandler gir = (s, e) => p.Hover = true;
            EventHandler cik = (s, e) =>
                p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));
            EventHandler bas = (s, e) => tik();

            Action<Control> bagla = null;
            bagla = c =>
            {
                c.MouseEnter += gir;
                c.MouseLeave += cik;
                c.Click += bas;
                foreach (Control alt in c.Controls) bagla(alt);
            };
            bagla(p);
        }

        // Simgeli yuvarlak düğme. ana: dolu mavi, değilse kart renginde kenarlıklı
        private YuvarlakPanel Dugme(string ikon, string yazi, int x, int y, int w, int h, bool ana, Action tik)
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

            Color renk = ana ? Color.White : Tema.Yazi;
            FlowLayoutPanel akis = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            if (ikon != null)
            {
                akis.Controls.Add(new Label
                {
                    Text = ikon,
                    Font = new Font("Segoe MDL2 Assets", 10f),
                    ForeColor = renk,
                    AutoSize = true,
                    Margin = new Padding(0, 3, 6, 0),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                });
            }
            akis.Controls.Add(new Label
            {
                Text = yazi,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = renk,
                AutoSize = true,
                Margin = new Padding(0),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            });
            p.Controls.Add(akis);

            Action ortala = () =>
                akis.Location = new Point((p.Width - akis.Width) / 2, (p.Height - akis.Height) / 2);
            p.Layout += (s, e) => ortala();
            akis.SizeChanged += (s, e) => ortala();

            Bagla(p, tik);
            return p;
        }

        // =================================================================
        //  Aç / kapa anahtarı (toggle switch)
        // =================================================================
        private class AcKapa : Control
        {
            private bool acik;
            public event Action<bool> Degisti;

            public bool Acik
            {
                get { return acik; }
                set { acik = value; Invalidate(); }
            }

            public AcKapa()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint |
                         ControlStyles.AllPaintingInWmPaint, true);
                BackColor = Color.Transparent;
                Size = new Size(46, 24);
                Cursor = Cursors.Hand;
            }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                Acik = !acik;

                Action<bool> h = Degisti;
                if (h != null) h(acik);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                RectangleF yol = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (var p = YuvarlakPanel.Yol(yol, (Height - 1.5f) / 2f))
                using (var b = new SolidBrush(acik ? Mavi : Tema.Pasif))
                    g.FillPath(b, p);

                float d = Height - 6;
                float x = acik ? Width - d - 3 : 3;
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, x, 3, d, d);
            }
        }
    }
}