using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Ana formla aynı görünümde küçük pencerelerin ortak tabanı:
    // lacivert başlık, yuvarlak köşeler, sürüklenebilir, Esc ile kapanır.
    public class TemaliPencere : Form
    {
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

        protected static readonly Color Mavi = Color.FromArgb(16, 104, 232);
        protected static readonly Color MaviKoyu = Color.FromArgb(10, 82, 190);

        public TemaliPencere() : this("", "", 420, 240) { }   // tasarımcı için

        protected TemaliPencere(string baslik, string ikon, int genislik, int yukseklik)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(genislik, yukseklik);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            KeyPreview = true;

            BaslikOlustur(baslik, ikon);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
            MouseDown += SurukleBaslat;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientRectangle.Width < 1 || ClientRectangle.Height < 1) return;
            using (var fr = new LinearGradientBrush(ClientRectangle, Tema.Zemin1, Tema.Zemin2, 50f))
                e.Graphics.FillRectangle(fr, ClientRectangle);
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

            if (!win11 && Width > 0 && Height > 0)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(0, 0, Width, Height), 14))
                    Region = new Region(yol);
            }
        }

        protected void SurukleBaslat(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        private void BaslikOlustur(string metin, string ikonYazi)
        {
            Panel baslik = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Tema.Baslik };

            Label ikon = new Label
            {
                Text = ikonYazi,
                Font = new Font("Segoe MDL2 Assets", 13f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };
            Label yazi = new Label
            {
                Text = metin,
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
            kapat.Click += (s, e) => DialogResult = DialogResult.Cancel;

            baslik.Controls.Add(ikon);
            baslik.Controls.Add(yazi);
            baslik.Controls.Add(kapat);

            baslik.MouseDown += SurukleBaslat;
            ikon.MouseDown += SurukleBaslat;
            yazi.MouseDown += SurukleBaslat;

            Controls.Add(baslik);
        }

        protected Label Yazi(string metin, int x, int y, int w, int h, float boyut, bool kalin, Color renk)
        {
            Label l = new Label
            {
                Text = metin,
                Font = new Font("Segoe UI", boyut, kalin ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = renk,
                BackColor = Color.Transparent,
                Location = new Point(x, y),
                Size = new Size(w, h)
            };
            Controls.Add(l);
            return l;
        }

        // İçinde tek satırlık metin kutusu olan yuvarlak kutu
        protected TextBox MetinKutusu(int x, int y, int w, string ipucu, out Label ipucuEtiketi)
        {
            YuvarlakPanel kutu = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, 44),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };
            TextBox t = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Tema.Yazi,
                Font = new Font("Segoe UI", 11.5f),
                Location = new Point(14, 11),
                Width = w - 28
            };
            Label l = new Label
            {
                Text = ipucu,
                Font = new Font("Segoe UI", 11.5f),
                ForeColor = Tema.Pasif,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(14, 10),
                Cursor = Cursors.IBeam
            };
            l.Click += (s, e) => t.Focus();
            t.TextChanged += (s, e) => l.Visible = t.Text.Length == 0;
            kutu.Click += (s, e) => t.Focus();

            kutu.Controls.Add(t);
            kutu.Controls.Add(l);
            l.BringToFront();
            Controls.Add(kutu);

            ipucuEtiketi = l;
            return t;
        }

        protected YuvarlakPanel Dugme(string yazi, int x, int y, int w, int h, bool ana, Action tik)
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
                ForeColor = ana ? Color.White : Tema.Yazi,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            p.Controls.Add(l);

            EventHandler gir = (s, e) => p.Hover = true;
            EventHandler cik = (s, e) => p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));
            EventHandler bas = (s, e) => tik();
            p.MouseEnter += gir; l.MouseEnter += gir;
            p.MouseLeave += cik; l.MouseLeave += cik;
            p.Click += bas; l.Click += bas;

            Controls.Add(p);
            return p;
        }
    }

    // =====================================================================
    //  Temaya uyan menü renkleri (ContextMenuStrip için)
    // =====================================================================
    public class TemaMenuRenkleri : ProfessionalColorTable
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

        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new TemaMenuRenkleri()) { RoundedEdges = false },
                Font = new Font("Segoe UI", 9.5f),
                ShowImageMargin = false,
                ShowItemToolTips = true,
                ForeColor = Tema.Yazi
            };
        }

        public static ToolStripMenuItem Oge(string yazi, Action tik)
        {
            ToolStripMenuItem o = new ToolStripMenuItem(yazi) { ForeColor = Tema.Yazi, Padding = new Padding(4, 3, 4, 3) };
            if (tik != null) o.Click += (s, e) => tik();
            return o;
        }
    }

    // =====================================================================
    //  Tek satırlık giriş penceresi (web sitesi adresi, profil adı ...)
    // =====================================================================
    public class GirisKutusu : TemaliPencere
    {
        private readonly TextBox txt;
        private readonly Label lblHata;
        private readonly Func<string, string> dogrula;   // null = uygun, aksi halde hata metni

        public string Deger { get; private set; }

        private GirisKutusu(string baslik, string aciklama, string ipucu, string varsayilan,
                            Func<string, string> dogrulama)
            : base(baslik, "", 420, 236)
        {
            dogrula = dogrulama;

            Yazi(aciklama, 22, 56, 376, 40, 9.5f, false, Tema.SolukYazi);

            Label ipucuEtiketi;
            txt = MetinKutusu(20, 100, 380, ipucu, out ipucuEtiketi);
            txt.Text = varsayilan ?? "";
            txt.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Tamam();
                }
            };

            lblHata = Yazi("", 22, 150, 376, 20, 9f, false, Tema.HataYazi);

            Dugme("İptal", 200, 178, 96, 42, false, () => DialogResult = DialogResult.Cancel);
            Dugme("TAMAM", 304, 178, 96, 42, true, Tamam);

            Shown += (s, e) => { txt.Focus(); txt.SelectAll(); };
        }

        private void Tamam()
        {
            string deger = txt.Text.Trim();
            string hata = dogrula != null ? dogrula(deger) : (deger.Length == 0 ? "Bir şey yaz." : null);

            if (hata != null)
            {
                lblHata.Text = hata;
                txt.Focus();
                return;
            }

            Deger = deger;
            DialogResult = DialogResult.OK;
        }

        // Vazgeçilirse null
        public static string Sor(IWin32Window sahip, string baslik, string aciklama, string ipucu,
                                 string varsayilan = "", Func<string, string> dogrulama = null)
        {
            using (GirisKutusu f = new GirisKutusu(baslik, aciklama, ipucu, varsayilan, dogrulama))
                return f.ShowDialog(sahip) == DialogResult.OK ? f.Deger : null;
        }
    }

    // =====================================================================
    //  Tuşa ad ve renk verme (yalnızca uygulamada görünür, karta gitmez)
    // =====================================================================
    public class TusAdiForm : TemaliPencere
    {
        // Seçilebilecek renkler (RRGGBB). Boş = renksiz.
        public static readonly string[] Renkler =
        {
            "1068E8", "0EA5A0", "7C3AED", "EA580C", "DB2777", "16A34A", "CA8A04", "DC2626"
        };

        private const int Cap = 30, Ara = 12;

        // Seçilebilecek simgeler (Segoe MDL2 Assets): {kod, açıklama}
        public static readonly string[,] Simgeler =
        {
            { "E720", "Mikrofon" }, { "E714", "Kamera (görüntülü)" }, { "E722", "Fotoğraf" }, { "E8D6", "Müzik" },
            { "E767", "Ses" }, { "E74F", "Sessiz" }, { "E768", "Oynat" }, { "E8B7", "Klasör" },
            { "E8A5", "Belge" }, { "E774", "Web" }, { "E715", "Posta" }, { "E8BD", "Mesaj" },
            { "E717", "Telefon" }, { "E72E", "Kilit" }, { "E713", "Ayarlar" }, { "E7FC", "Oyun" },
            { "E943", "Kod" }, { "E7F4", "Ekran" }, { "E734", "Yıldız" }, { "EB51", "Kalp" },
            { "E945", "Şimşek" }, { "E80F", "Ev" }, { "E8C8", "Kopyala" },

            // Düzenleme / dosya
            { "E8C6", "Kes" }, { "E77F", "Yapıştır" }, { "E7A7", "Geri al" }, { "E7A6", "Yinele" },
            { "E74E", "Kaydet" }, { "E749", "Yazdır" }, { "E74D", "Sil" }, { "E710", "Ekle" },
            { "E73E", "Onay" }, { "E723", "Ataç" }, { "E721", "Ara" }, { "E72C", "Yenile" },
            { "E896", "İndir" }, { "E898", "Yükle" }, { "E753", "Bulut" }, { "E71B", "Bağlantı" },
            { "E72D", "Paylaş" }, { "E8A3", "Yakınlaştır" }, { "E71F", "Uzaklaştır" }, { "E740", "Tam ekran" },

            // Medya
            { "E769", "Duraklat" }, { "E71A", "Durdur" }, { "E892", "Önceki" }, { "E893", "Sonraki" },
            { "E7C8", "Kayıt" }, { "E8B2", "Film" }, { "E8B9", "Resim" }, { "E7F6", "Kulaklık" },
            { "E7F5", "Hoparlör" },

            // Zaman / sistem
            { "E787", "Takvim" }, { "E823", "Saat" }, { "E916", "Kronometre" }, { "E81C", "Geçmiş" },
            { "E7E8", "Güç" }, { "E706", "Parlaklık" }, { "E708", "Gece" }, { "E701", "Wi-Fi" },
            { "E702", "Bluetooth" }, { "E765", "Klavye" }, { "E962", "Fare" }, { "E8EF", "Hesap makinesi" },

            // Kişiler / iletişim / diğer
            { "E77B", "Kişi" }, { "E716", "Kişiler" }, { "E8E1", "Beğen" }, { "E76E", "Gülen yüz" },
            { "E8EC", "Etiket" }, { "E7C1", "Bayrak" }, { "E7BA", "Uyarı" }, { "E946", "Bilgi" },
            { "E897", "Yardım" }, { "E909", "Dünya" }, { "E707", "Konum" }, { "E804", "Araba" },
            { "E719", "Alışveriş" }, { "E82F", "Ampul" }, { "E7BE", "Eğitim" }, { "E7B3", "Göz" },
            { "E8D2", "Yazı" }, { "E70F", "Kalem" }, { "E790", "Renk" }
        };

        // Simge satırı sayısı ("simge yok" dahil) ve 2 satıra göre fazladan yükseklik
        private static int SimgeSatiri { get { return (Simgeler.GetLength(0) + 1 + SimgeSutun - 1) / SimgeSutun; } }
        private static int SimgeEk { get { return (SimgeSatiri - 2) * (SimgeCap + SimgeAra); } }

        private const int SimgeCap = 28, SimgeAra = 4, SimgeSutun = 12;

        private readonly Panel simgeAlani;
        private string simge;
        private int hoverSimge = -2;
        private readonly ToolTip simgeIpucu = new ToolTip();

        private readonly TextBox txtAd;
        private readonly Panel renkAlani;
        private string renk;
        private int hoverRenk = -2;

        public string Ad { get; private set; }
        public string Renk { get; private set; }
        public string Simge { get; private set; }

        public TusAdiForm(string tusAdi, string mevcutAd, string mevcutRenk, string mevcutSimge)
            : base(tusAdi + " · ad, renk, simge", "", 420, 408 + SimgeEk)
        {
            renk = mevcutRenk ?? "";
            simge = mevcutSimge ?? "";

            Yazi("Ad", 22, 56, 200, 22, 10.5f, true, Tema.Yazi);

            Label ipucu;
            txtAd = MetinKutusu(20, 80, 380, "örn. Toplantı sesi", out ipucu);
            txtAd.MaxLength = 24;
            txtAd.Text = mevcutAd ?? "";
            txtAd.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Kaydet();
                }
            };

            Yazi("Renk", 22, 136, 200, 22, 10.5f, true, Tema.Yazi);

            renkAlani = new Panel
            {
                Location = new Point(20, 162),
                Size = new Size(380, Cap + 8),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(renkAlani, true, null);
            renkAlani.Paint += RenkleriCiz;
            renkAlani.MouseMove += (s, e) =>
            {
                int i = RenkBul(e.Location);
                if (i != hoverRenk) { hoverRenk = i; renkAlani.Invalidate(); }
            };
            renkAlani.MouseLeave += (s, e) => { hoverRenk = -2; renkAlani.Invalidate(); };
            renkAlani.MouseClick += (s, e) =>
            {
                int i = RenkBul(e.Location);
                if (i == -2) return;
                renk = i < 0 ? "" : Renkler[i];
                renkAlani.Invalidate();
            };
            Controls.Add(renkAlani);

            // --- Simge ---
            Yazi("Simge", 22, 210, 200, 22, 10.5f, true, Tema.Yazi);

            simgeAlani = new Panel
            {
                Location = new Point(20, 236),
                Size = new Size(SimgeSutun * (SimgeCap + SimgeAra) - SimgeAra, SimgeSatiri * (SimgeCap + SimgeAra) - SimgeAra),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(simgeAlani, true, null);
            simgeAlani.Paint += SimgeleriCiz;
            simgeAlani.MouseMove += (s, e) =>
            {
                int i = SimgeBul(e.Location);
                if (i == hoverSimge) return;
                hoverSimge = i;
                simgeAlani.Invalidate();
                simgeIpucu.SetToolTip(simgeAlani, i == -1 ? "Simge yok" : i >= 0 ? Simgeler[i, 1] : "");
            };
            simgeAlani.MouseLeave += (s, e) => { hoverSimge = -2; simgeAlani.Invalidate(); };
            simgeAlani.MouseClick += (s, e) =>
            {
                int i = SimgeBul(e.Location);
                if (i == -2) return;
                simge = i < 0 ? "" : Simgeler[i, 0];
                simgeAlani.Invalidate();
            };
            Controls.Add(simgeAlani);

            Yazi("Ad, renk ve simge yalnızca bu uygulamada görünür; klavyenin içinde de saklanır, " +
                 "başka bilgisayara takınca oraya da gelir.",
                 22, 304 + SimgeEk, 376, 36, 8.5f, false, Tema.SolukYazi);

            Dugme("Kaldır", 20, 348 + SimgeEk, 96, 42, false, () =>
            {
                Ad = "";
                Renk = "";
                Simge = "";
                DialogResult = DialogResult.OK;
            });
            Dugme("İptal", 200, 348 + SimgeEk, 96, 42, false, () => DialogResult = DialogResult.Cancel);
            Dugme("KAYDET", 304, 348 + SimgeEk, 96, 42, true, Kaydet);

            Shown += (s, e) => { txtAd.Focus(); txtAd.SelectAll(); };
        }

        private void Kaydet()
        {
            Ad = txtAd.Text.Trim();
            Renk = renk;
            Simge = simge;
            DialogResult = DialogResult.OK;
        }

        // -1 = simge yok, 0.. = Simgeler, -2 = boşluk
        private Rectangle SimgeKutusu(int i)
        {
            int s = i + 1;
            return new Rectangle((s % SimgeSutun) * (SimgeCap + SimgeAra), (s / SimgeSutun) * (SimgeCap + SimgeAra),
                                 SimgeCap, SimgeCap);
        }

        private int SimgeBul(Point p)
        {
            for (int i = -1; i < Simgeler.GetLength(0); i++)
                if (SimgeKutusu(i).Contains(p)) return i;
            return -2;
        }

        private void SimgeleriCiz(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            StringFormat orta = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Color renkli = HexRenk(renk);

            using (Font yazi = new Font("Segoe MDL2 Assets", 12f))
            {
                for (int i = -1; i < Simgeler.GetLength(0); i++)
                {
                    Rectangle r = SimgeKutusu(i);
                    string kod = i < 0 ? "" : Simgeler[i, 0];
                    bool secili = string.Equals(kod, simge, StringComparison.OrdinalIgnoreCase);

                    Color zemin = secili ? Mavi : (i == hoverSimge ? Tema.Hover : Tema.Kart);
                    using (GraphicsPath yol = YuvarlakPanel.Yol(new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), 7))
                    {
                        using (SolidBrush b = new SolidBrush(zemin))
                            g.FillPath(b, yol);
                        using (Pen k = new Pen(secili ? Mavi : Tema.Kenar))
                            g.DrawPath(k, yol);
                    }

                    if (i < 0)
                    {
                        // "Simge yok": çapraz çizgi
                        using (Pen k = new Pen(secili ? Color.White : Tema.Pasif, 1.5f))
                            g.DrawLine(k, r.Left + 8, r.Bottom - 8, r.Right - 8, r.Top + 8);
                        continue;
                    }

                    Color c = secili ? Color.White : (renkli.IsEmpty ? Tema.Yazi : renkli);
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(((char)Convert.ToInt32(kod, 16)).ToString(), yazi, b, r, orta);
                }
            }
        }

        // -1 = renksiz, 0.. = Renkler, -2 = boşluk
        private int RenkBul(Point p)
        {
            for (int i = -1; i < Renkler.Length; i++)
            {
                if (RenkKutusu(i).Contains(p)) return i;
            }
            return -2;
        }

        private Rectangle RenkKutusu(int i)
        {
            return new Rectangle(4 + (i + 1) * (Cap + Ara), 4, Cap, Cap);
        }

        private void RenkleriCiz(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = -1; i < Renkler.Length; i++)
            {
                Rectangle r = RenkKutusu(i);
                string kod = i < 0 ? "" : Renkler[i];
                bool secili = string.Equals(kod, renk, StringComparison.OrdinalIgnoreCase);

                if (i < 0)
                {
                    // Renksiz: çizgili boş daire
                    using (var b = new SolidBrush(Tema.Kart))
                        g.FillEllipse(b, r);
                    using (var k = new Pen(Tema.Pasif, 1.5f))
                    {
                        g.DrawEllipse(k, r);
                        g.DrawLine(k, r.Left + 7, r.Bottom - 7, r.Right - 7, r.Top + 7);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(HexRenk(kod)))
                        g.FillEllipse(b, r);
                }

                if (secili || i == hoverRenk)
                {
                    Rectangle halka = Rectangle.Inflate(r, 3, 3);
                    using (var k = new Pen(secili ? Tema.Yazi : Tema.Pasif, 2f))
                        g.DrawEllipse(k, halka);
                }
            }
        }

        // "DB2777" -> renk (hatalıysa boş renk)
        public static Color HexRenk(string hex)
        {
            int deger;
            if (string.IsNullOrEmpty(hex) || hex.Length != 6 ||
                !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out deger))
                return Color.Empty;

            return Color.FromArgb((deger >> 16) & 0xFF, (deger >> 8) & 0xFF, deger & 0xFF);
        }
    }
}

namespace MakroKeyboard
{
    // =====================================================================
    //  Uzun / çift basış süreleri (klavyede saklanır: LONG_PRESS_MS, DOUBLE_PRESS_MS)
    // =====================================================================
    public class BasisSureleriForm : TemaliPencere
    {
        public const int VarsayilanUzun = 600, VarsayilanCift = 300;

        private readonly Kaydirici uzunKaydirici, ciftKaydirici, harfKaydirici, titresimKaydirici;
        private readonly Label lblTitresim;
        private static readonly int[] TekrarDegerleri = { 1, 2, 3, 5 };
        private readonly YuvarlakPanel[] tekrarDugmeleri = new YuvarlakPanel[4];
        private readonly bool kart24;
        private readonly CheckBox chkTers;
        private readonly YuvarlakPanel[] adimDugmeleri = new YuvarlakPanel[3];
        private static readonly int[] AdimDegerleri = { 1, 2, 4 };
        private readonly bool encoderDestekli;
        private readonly Label lblUzun, lblCift, lblDuzenNot, lblHarf;
        private static readonly string[] DuzenKodlari = { "TR", "TRF", "US" };
        private readonly YuvarlakPanel[] duzenDugmeleri = new YuvarlakPanel[3];
        private readonly bool duzenDestekli, trfDestekli;
        private readonly CheckBox chkUyku;
        private static readonly int[] UykuDegerleri = { 30, 60, 240 };
        private readonly YuvarlakPanel[] uykuDugmeleri = new YuvarlakPanel[3];
        private readonly bool uykuDestekli;
        public const int VarsayilanUykuAralik = 60;

        public int Uzun { get { return uzunKaydirici.Deger; } }
        public int Cift { get { return ciftKaydirici.Deger; } }
        public string Duzen { get; private set; }   // TR, TRF ya da US
        public bool Uyku { get { return chkUyku.Checked; } }
        public int UykuAralik { get; private set; }   // saniye
        public int Harf { get { return harfKaydirici.Deger; } }
        public bool EncoderTers { get { return chkTers.Checked; } }
        public int EncoderAdim { get; private set; }
        public int EncoderTekrar { get; private set; }   // bir adımda komut kaç kez (kart 2.4+)
        public int Titresim { get { return titresimKaydirici.Deger; } }   // ms (kart 2.4+)

        // duzenDestekli: kart 2.0+, harfDestekli: kart 2.1+, encoderDestekli: kart 2.2+,
        // trfDestekli (Türkçe F) ve uykuDestekli: kart 2.3+ (eski kartta kapalı)
        public BasisSureleriForm(int uzun, int cift, string duzen, bool duzenDestekli, bool trfDestekli,
                                 int harf, bool harfDestekli, bool encoderTers, int encoderAdim, bool encoderDestekli,
                                 bool uyku, int uykuAralik, bool uykuDestekli,
                                 int titresim, int encoderTekrar, bool kart24)
            : base("Klavye ayarları", "\uE765", 840, 540)
        {
            this.kart24 = kart24;
            EncoderTekrar = Array.IndexOf(TekrarDegerleri, encoderTekrar) >= 0 ? encoderTekrar : 1;
            this.encoderDestekli = encoderDestekli;
            EncoderAdim = Array.IndexOf(AdimDegerleri, encoderAdim) >= 0 ? encoderAdim : 1;
            this.duzenDestekli = duzenDestekli;
            this.trfDestekli = trfDestekli;
            Duzen = KlavyeDuzeni.Coz(duzen);
            this.uykuDestekli = uykuDestekli;
            UykuAralik = Array.IndexOf(UykuDegerleri, uykuAralik) >= 0 ? uykuAralik : VarsayilanUykuAralik;

            // ---------- Metin yazma düzeni ----------
            Yazi("Metin yazma düzeni", 22, 58, 300, 22, 10.5f, true, Tema.Yazi);
            string[] duzenler = { "Türkçe Q", "Türkçe F", "İngilizce (US)" };
            for (int i = 0; i < 3; i++)
            {
                string kod = DuzenKodlari[i];
                bool etkin = duzenDestekli && (kod != "TRF" || trfDestekli);
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(20 + i * 128, 84),
                    Size = new Size(122, 36),
                    Yaricap = 12,
                    Cursor = etkin ? Cursors.Hand : Cursors.Default
                };
                Label l = new Label
                {
                    Text = duzenler[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = p.Cursor
                };
                p.Controls.Add(l);
                EventHandler sec = (s, e) =>
                {
                    if (!etkin) return;
                    Duzen = kod;
                    DuzenGorunumu();
                };
                p.Click += sec;
                l.Click += sec;
                duzenDugmeleri[i] = p;
                Controls.Add(p);
            }
            lblDuzenNot = Yazi("", 22, 124, 376, 34, 8.5f, false, Tema.SolukYazi);
            DuzenGorunumu();

            // Sağ sütun: basış süreleri ve encoder (sol sütun: düzen, yazma hızı, uyku engelleyici)
            Controls.Add(new Panel { Location = new Point(420, 62), Size = new Size(1, 410), BackColor = Tema.Kenar });
            Yazi("Uzun basış", 442, 58, 200, 22, 10.5f, true, Tema.Yazi);
            lblUzun = Yazi("", 670, 58, 148, 22, 10.5f, true, Mavi);
            lblUzun.TextAlign = ContentAlignment.TopRight;
            uzunKaydirici = new Kaydirici { EnAz = 300, EnCok = 1500, Location = new Point(434, 82), Size = new Size(392, 26) };
            Controls.Add(uzunKaydirici);
            Yazi("Tuş bu kadar basılı tutulunca uzun basış çalışır.", 442, 110, 376, 18, 8.5f, false, Tema.SolukYazi);

            Yazi("Çift basış aralığı", 442, 140, 200, 22, 10.5f, true, Tema.Yazi);
            lblCift = Yazi("", 670, 140, 148, 22, 10.5f, true, Mavi);
            lblCift.TextAlign = ContentAlignment.TopRight;
            ciftKaydirici = new Kaydirici { EnAz = 150, EnCok = 700, Location = new Point(434, 164), Size = new Size(392, 26) };
            Controls.Add(ciftKaydirici);
            Yazi("İki basış arası en fazla bu kadar olursa çift basış sayılır. Uzadıkça, çift basışı " +
                 "olan tuşların kısa basışı da o kadar geç çalışır.", 442, 192, 376, 34, 8.5f, false, Tema.SolukYazi);

            // ---------- Metin yazma hızı ----------
            Yazi("Harfler arası bekleme", 22, 176, 220, 22, 10.5f, true, Tema.Yazi);
            lblHarf = Yazi("", 230, 176, 168, 22, 10.5f, true, Mavi);
            lblHarf.TextAlign = ContentAlignment.TopRight;
            harfKaydirici = new Kaydirici { EnAz = 0, EnCok = 50, Location = new Point(14, 200), Size = new Size(392, 26), Enabled = harfDestekli };
            Controls.Add(harfKaydirici);
            Yazi(harfDestekli
                    ? "Metin yazarken bazı programlar (oyun, uzak masaüstü, bazı siteler) harf kaçırırsa artır."
                    : "Bu ayar için kart yazılımı 2.1 gerekir (Ayarlar > Kart'tan güncelle).",
                 22, 228, 376, 34, 8.5f, false, Tema.SolukYazi);

            // ---------- Tuş titreşim filtresi ----------
            Yazi("Tuş titreşim filtresi", 442, 236, 220, 22, 10.5f, true, Tema.Yazi);
            lblTitresim = Yazi("", 670, 236, 148, 22, 10.5f, true, Mavi);
            lblTitresim.TextAlign = ContentAlignment.TopRight;
            titresimKaydirici = new Kaydirici { EnAz = 0, EnCok = 30, Location = new Point(434, 260), Size = new Size(392, 26), Enabled = kart24 };
            Controls.Add(titresimKaydirici);
            Yazi(kart24 ? "Bir basış bazen iki kez algılanıyorsa (eski / yıpranmış tuş) artır."
                        : "Bu ayar için kart yazılımı 2.4 gerekir (Ayarlar > Kart'tan güncelle).",
                 442, 288, 376, 18, 8.5f, false, Tema.SolukYazi);

            // ---------- Encoder ----------
            const int E = 318;
            Yazi("Encoder", 442, E, 200, 22, 10.5f, true, Tema.Yazi);
            chkTers = new CheckBox
            {
                Text = "Yönü ters çevir (sağa çevirince sola çevirme komutu)",
                Checked = encoderTers,
                Enabled = encoderDestekli,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Tema.Yazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(442, E + 26),
                Cursor = Cursors.Hand
            };
            Controls.Add(chkTers);

            Yazi("Hassasiyet", 442, E + 58, 90, 22, 9.5f, false, Tema.SolukYazi);
            string[] adimAdlari = { "Her tık", "2 tıkta bir", "4 tıkta bir" };
            for (int i = 0; i < 3; i++)
            {
                int deger = AdimDegerleri[i];
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(532 + i * 98, E + 54),
                    Size = new Size(92, 30),
                    Yaricap = 10,
                    Cursor = encoderDestekli ? Cursors.Hand : Cursors.Default
                };
                Label l = new Label
                {
                    Text = adimAdlari[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = p.Cursor
                };
                p.Controls.Add(l);
                EventHandler sec = (s, e) =>
                {
                    if (!this.encoderDestekli) return;
                    EncoderAdim = deger;
                    AdimGorunumu();
                };
                p.Click += sec;
                l.Click += sec;
                adimDugmeleri[i] = p;
                Controls.Add(p);
            }
            // Tık başına: komut bir adımda kaç kez çalışsın (ses / kaydırma daha hızlı)
            Yazi("Tık başına", 442, E + 94, 90, 22, 9.5f, false, Tema.SolukYazi);
            for (int i = 0; i < 4; i++)
            {
                int deger = TekrarDegerleri[i];
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(532 + i * 74, E + 90),
                    Size = new Size(68, 30),
                    Yaricap = 10,
                    Cursor = kart24 ? Cursors.Hand : Cursors.Default
                };
                Label l = new Label
                {
                    Text = deger + " kez",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = p.Cursor
                };
                p.Controls.Add(l);
                EventHandler sec = (s, e) =>
                {
                    if (!this.kart24) return;
                    EncoderTekrar = deger;
                    TekrarGorunumu();
                };
                p.Click += sec;
                l.Click += sec;
                tekrarDugmeleri[i] = p;
                Controls.Add(p);
            }

            Yazi(!encoderDestekli
                    ? "Encoder ayarları için kart yazılımı 2.2 gerekir (Ayarlar > Kart'tan güncelle)."
                    : kart24 ? "Hassasiyet: bir tıkta iki adım sayıyorsa 2 tıkta bir. Tık başına: komut her tıkta kaç kez çalışsın."
                             : "Hassasiyet: bir tıkta iki adım sayıyorsa 2 tıkta bir. Tık başına için kart yazılımı 2.4 gerekir.",
                 442, E + 124, 376, 30, 8.5f, false, Tema.SolukYazi);
            AdimGorunumu();
            TekrarGorunumu();

            // ---------- Uyku engelleyici ----------
            const int U = 276;
            Yazi("Uyku engelleyici", 22, U, 200, 22, 10.5f, true, Tema.Yazi);
            chkUyku = new CheckBox
            {
                Text = "Bilgisayar uyumasın (fare arada bir 1 piksel kıpırdar)",
                Checked = uyku,
                Enabled = uykuDestekli,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Tema.Yazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(22, U + 26),
                Cursor = Cursors.Hand
            };
            Controls.Add(chkUyku);

            Yazi("Sıklık", 22, U + 58, 90, 22, 9.5f, false, Tema.SolukYazi);
            string[] uykuAdlari = { "30 sn'de bir", "1 dk'da bir", "4 dk'da bir" };
            for (int i = 0; i < 3; i++)
            {
                int deger = UykuDegerleri[i];
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(112 + i * 98, U + 54),
                    Size = new Size(92, 30),
                    Yaricap = 10,
                    Cursor = uykuDestekli ? Cursors.Hand : Cursors.Default
                };
                Label l = new Label
                {
                    Text = uykuAdlari[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = p.Cursor
                };
                p.Controls.Add(l);
                EventHandler sec = (s, e) =>
                {
                    if (!this.uykuDestekli) return;
                    UykuAralik = deger;
                    UykuGorunumu();
                };
                p.Click += sec;
                l.Click += sec;
                uykuDugmeleri[i] = p;
                Controls.Add(p);
            }
            Yazi(uykuDestekli
                    ? "Uygulama kapalıyken de çalışır. Tuşa atamak için: komut menüsü > Pencere ve Sistem > Uyku Engelleyici."
                    : "Uyku engelleyici için kart yazılımı 2.3 gerekir (Ayarlar > Kart'tan güncelle).",
                 22, U + 88, 376, 30, 8.5f, false, Tema.SolukYazi);
            UykuGorunumu();

            const int B = 482;   // düğmeler
            Dugme("Varsayılan", 20, B, 112, 42, false, () =>
            {
                uzunKaydirici.Deger = VarsayilanUzun;
                ciftKaydirici.Deger = VarsayilanCift;
                harfKaydirici.Deger = 0;
                if (this.encoderDestekli)
                {
                    chkTers.Checked = false;
                    EncoderAdim = 1;
                    AdimGorunumu();
                }
                if (this.uykuDestekli)
                {
                    chkUyku.Checked = false;
                    UykuAralik = VarsayilanUykuAralik;
                    UykuGorunumu();
                }
                if (this.kart24)
                {
                    titresimKaydirici.Deger = 0;
                    EncoderTekrar = 1;
                    TekrarGorunumu();
                }
                Yazilari();
            });
            Dugme("İptal", 620, B, 96, 42, false, () => DialogResult = DialogResult.Cancel);
            Dugme("KAYDET", 724, B, 96, 42, true, () => DialogResult = DialogResult.OK);

            uzunKaydirici.Deger = uzun;
            ciftKaydirici.Deger = cift;
            harfKaydirici.Deger = harf;
            titresimKaydirici.Deger = Math.Max(0, Math.Min(30, titresim));
            titresimKaydirici.Degisti += () => { titresimKaydirici.Deger = Yuvarla(titresimKaydirici.Deger, 5); Yazilari(); };
            harfKaydirici.Degisti += () => { harfKaydirici.Deger = Yuvarla(harfKaydirici.Deger, 5); Yazilari(); };
            uzunKaydirici.Degisti += () => { uzunKaydirici.Deger = Yuvarla(uzunKaydirici.Deger, 50); Yazilari(); };
            ciftKaydirici.Degisti += () => { ciftKaydirici.Deger = Yuvarla(ciftKaydirici.Deger, 25); Yazilari(); };
            Yazilari();
        }

        private void AdimGorunumu()
        {
            for (int i = 0; i < 3; i++)
            {
                bool aktif = AdimDegerleri[i] == EncoderAdim;
                adimDugmeleri[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                adimDugmeleri[i].KenarRengi = aktif ? Mavi : Tema.Kenar2;
                adimDugmeleri[i].Controls[0].ForeColor = aktif ? Color.White : (encoderDestekli ? Tema.Yazi : Tema.Pasif);
                adimDugmeleri[i].Invalidate(true);
            }
        }

        private void TekrarGorunumu()
        {
            for (int i = 0; i < tekrarDugmeleri.Length; i++)
            {
                bool aktif = TekrarDegerleri[i] == EncoderTekrar;
                tekrarDugmeleri[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                tekrarDugmeleri[i].KenarRengi = aktif ? Mavi : Tema.Kenar2;
                tekrarDugmeleri[i].Controls[0].ForeColor = aktif ? Color.White : (kart24 ? Tema.Yazi : Tema.Pasif);
                tekrarDugmeleri[i].Invalidate(true);
            }
        }

        private void UykuGorunumu()
        {
            for (int i = 0; i < 3; i++)
            {
                bool aktif = UykuDegerleri[i] == UykuAralik;
                uykuDugmeleri[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                uykuDugmeleri[i].KenarRengi = aktif ? Mavi : Tema.Kenar2;
                uykuDugmeleri[i].Controls[0].ForeColor = aktif ? Color.White : (uykuDestekli ? Tema.Yazi : Tema.Pasif);
                uykuDugmeleri[i].Invalidate(true);
            }
        }

        private void DuzenGorunumu()
        {
            for (int i = 0; i < 3; i++)
            {
                bool aktif = DuzenKodlari[i] == Duzen;
                bool etkin = duzenDestekli && (DuzenKodlari[i] != "TRF" || trfDestekli);
                duzenDugmeleri[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                duzenDugmeleri[i].KenarRengi = aktif ? Mavi : Tema.Kenar2;
                duzenDugmeleri[i].Controls[0].ForeColor = aktif ? Color.White : (etkin ? Tema.Yazi : Tema.Pasif);
                duzenDugmeleri[i].Invalidate(true);
            }

            if (!duzenDestekli)
            {
                lblDuzenNot.Text = "Düzen seçmek için kart yazılımı 2.0 gerekir (Ayarlar > Kart'tan güncelle).";
                lblDuzenNot.ForeColor = Tema.SolukYazi;
                return;
            }

            string w = KlavyeDuzeni.Windows();
            bool uyumlu = KlavyeDuzeni.WindowsKodu(w) == Duzen;
            if (w == "TR_F" && !trfDestekli)
                lblDuzenNot.Text = "Windows'ta Türkçe F kullanılıyor; Türkçe F için kart yazılımı 2.3 gerekir (Ayarlar > Kart'tan güncelle).";
            else if (uyumlu)
                lblDuzenNot.Text = "Windows'taki klavyenle aynı (" + KlavyeDuzeni.WindowsAdi() + "). Metinler doğru yazılır.";
            else
                lblDuzenNot.Text = "Windows'ta " + KlavyeDuzeni.WindowsAdi() + " kullanılıyor; bununla aynısını seç, " +
                                   "yoksa bazı harf ve işaretler yanlış çıkar.";
            lblDuzenNot.ForeColor = uyumlu ? Tema.OkYazi : Tema.HataYazi;
        }

        private static int Yuvarla(int deger, int adim)
        {
            return (int)Math.Round(deger / (double)adim) * adim;
        }

        private void Yazilari()
        {
            lblUzun.Text = Sure(uzunKaydirici.Deger);
            lblCift.Text = Sure(ciftKaydirici.Deger);
            lblHarf.Text = harfKaydirici.Deger == 0 ? "yok (en hızlı)" : harfKaydirici.Deger + " ms";
            lblTitresim.Text = titresimKaydirici.Deger == 0 ? "kapalı" : titresimKaydirici.Deger + " ms";
        }

        private static string Sure(int ms)
        {
            return (ms / 1000.0).ToString("0.00", new System.Globalization.CultureInfo("tr-TR")) + " sn";
        }
    }

    // =====================================================================
    //  Kart tanılama: boş alan, açılış nedeni, bellek, sıcaklık ...
    // =====================================================================
    public class KartTanilamaForm : TemaliPencere
    {
        private readonly Func<Task<Dictionary<string, string>>> yenile;
        private readonly YuvarlakPanel ozet, liste;
        private readonly CizimAlani alan;
        private readonly Label lblOzet;
        private List<KeyValuePair<string, string>> satirlar = new List<KeyValuePair<string, string>>();
        private bool yenileniyor;

        private const int SatirH = 29;

        public KartTanilamaForm(Dictionary<string, string> bilgi, Func<Task<Dictionary<string, string>>> yenileme)
            : base("Kart tanılama", "", 460, 540)
        {
            yenile = yenileme;

            ozet = new YuvarlakPanel { Location = new Point(20, 58), Size = new Size(420, 62), Yaricap = 12 };
            lblOzet = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 0, 12, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            ozet.Controls.Add(lblOzet);
            Controls.Add(ozet);

            liste = new YuvarlakPanel
            {
                Location = new Point(20, 130),
                Size = new Size(420, 340),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };
            alan = new CizimAlani
            {
                Location = new Point(4, 4),
                Size = new Size(412, 332),
                BackColor = Tema.Kart,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            alan.Paint += ListeCiz;
            liste.Controls.Add(alan);
            Controls.Add(liste);

            Dugme("KOPYALA", 20, 482, 130, 42, false, Kopyala);
            Dugme("YENİLE", 160, 482, 130, 42, false, Yenile);
            Dugme("KAPAT", 310, 482, 130, 42, true, () => DialogResult = DialogResult.OK);

            Goster(bilgi);
        }

        private void Goster(Dictionary<string, string> bilgi)
        {
            List<string> uyarilar;
            satirlar = Satirlar(bilgi, out uyarilar);

            bool sorun = uyarilar.Count > 0;
            ozet.DolguRengi = sorun ? Tema.HataDolgu : Tema.OkDolgu;
            ozet.KenarRengi = sorun ? Tema.HataKenar : Tema.OkKenar;
            lblOzet.ForeColor = sorun ? Tema.HataYazi : Tema.OkYazi;
            lblOzet.Text = sorun ? string.Join("\n", uyarilar.Take(2)) : "Her şey yolunda görünüyor.";
            ozet.Invalidate(true);

            int yukseklik = Math.Max(SatirH + 16, satirlar.Count * SatirH + 16);
            liste.Height = Math.Min(340, yukseklik);
            alan.Invalidate();
        }

        private class CizimAlani : Panel
        {
            public CizimAlani()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        private void ListeCiz(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            StringFormat sol = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (Font adF = new Font("Segoe UI", 9.5f))
            using (Font degerF = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (SolidBrush soluk = new SolidBrush(Tema.SolukYazi))
            using (SolidBrush yazi = new SolidBrush(Tema.Yazi))
            using (Pen cizgi = new Pen(Tema.Hover))
            {
                for (int i = 0; i < satirlar.Count; i++)
                {
                    float y = 4 + i * SatirH;
                    if (i > 0) g.DrawLine(cizgi, 10, y, alan.Width - 10, y);
                    g.DrawString(satirlar[i].Key, adF, soluk, new RectangleF(10, y, 150, SatirH), sol);
                    g.DrawString(satirlar[i].Value, degerF, yazi, new RectangleF(160, y, alan.Width - 170, SatirH), sol);
                }
            }
        }

        private async void Yenile()
        {
            if (yenileniyor) return;
            yenileniyor = true;
            lblOzet.Text = "Karttan okunuyor…";

            try
            {
                Dictionary<string, string> yeni = await yenile();
                if (yeni != null) Goster(yeni);
                else lblOzet.Text = "Kart yanıt vermedi. Bağlantıyı kontrol et.";
            }
            catch (Exception ex) { lblOzet.Text = "Okunamadı: " + ex.Message; }
            finally { yenileniyor = false; }
        }

        private void Kopyala()
        {
            StringBuilder sb = new StringBuilder("Makro Klavye - kart tanılama (" + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + ")\r\n");
            foreach (var s in satirlar) sb.Append(s.Key).Append(": ").Append(s.Value).Append("\r\n");
            try { Clipboard.SetText(sb.ToString()); lblOzet.Text = "Panoya kopyalandı."; }
            catch (Exception) { }
        }

        // ---------- kart cevabını okunur satırlara çevirme ----------
        public static List<KeyValuePair<string, string>> Satirlar(Dictionary<string, string> d, out List<string> uyarilar)
        {
            List<KeyValuePair<string, string>> s = new List<KeyValuePair<string, string>>();
            uyarilar = new List<string>();
            Func<string, string> al = k => { string v; return d != null && d.TryGetValue(k, out v) ? v : null; };
            Action<string, string> ekle = (ad, deger) => { if (deger != null) s.Add(new KeyValuePair<string, string>(ad, deger)); };

            ekle("Kart yazılımı", al("YAZILIM"));
            if (al("CP") != null) ekle("CircuitPython", al("CP") + (al("KART") != null ? " · " + al("KART") : ""));

            long bos = Sayi(al("BOS")), toplam = Sayi(al("TOPLAM"));
            if (bos >= 0 && toplam > 0)
            {
                ekle("Depolama", Boyut(bos) + " boş / " + Boyut(toplam));
                if (bos < 12 * 1024) uyarilar.Add("Kartta yer kalmadı: ayarlar kaydedilemeyebilir. Kullanmadığın dosyaları sil.");
            }

            long ayarBoyut = Sayi(al("AYAR_BOYUT"));
            if (ayarBoyut >= 0) ekle("settings.txt", Boyut(ayarBoyut) + (al("AYAR_SAYI") != null ? " · " + al("AYAR_SAYI") + " ayar" : ""));

            long bellek = Sayi(al("BELLEK"));
            if (bellek >= 0)
            {
                ekle("Boş bellek (RAM)", Boyut(bellek));
                if (bellek < 16 * 1024) uyarilar.Add("Kartın belleği azalmış; uzun makrolar sorun çıkarabilir.");
            }

            long acik = Sayi(al("ACIK")), calisma = Sayi(al("CALISMA"));
            if (acik >= 0) ekle("Kart açık", SureYazisi(acik));
            if (calisma >= 0)
                ekle("Yazılım çalışıyor", SureYazisi(calisma) + (acik >= 0 && acik - calisma > 60 ? " (sonradan yeniden başladı)" : ""));

            string sifirlama = al("SIFIRLAMA");
            if (sifirlama != null)
            {
                ekle("Son açılış nedeni", SifirlamaAdi(sifirlama));
                if (sifirlama == "BROWNOUT")
                    uyarilar.Add("Kart voltaj düştüğü için yeniden başlamış. Başka bir USB kablo / port dene ya da LED parlaklığını düşür.");
                else if (sifirlama == "WATCHDOG")
                    uyarilar.Add("Kart donduğu için kendini yeniden başlatmış.");
            }

            string baslama = al("BASLAMA");
            if (baslama != null) ekle("Yazılım nasıl başladı", BaslamaAdi(baslama));

            double sicaklik;
            if (al("SICAKLIK") != null && double.TryParse(al("SICAKLIK"), System.Globalization.NumberStyles.Float,
                                                          System.Globalization.CultureInfo.InvariantCulture, out sicaklik))
            {
                ekle("İşlemci sıcaklığı", sicaklik.ToString("0.0", new System.Globalization.CultureInfo("tr-TR")) + " °C");
                if (sicaklik > 70) uyarilar.Add("İşlemci çok sıcak (" + sicaklik.ToString("0") + " °C).");
            }

            if (al("YAZILABILIR") != null)
            {
                bool yazilabilir = al("YAZILABILIR") == "1";
                ekle("Ayar kaydı", yazilabilir ? "Kart ayar kaydedebiliyor" : "Salt okunur");
                if (!yazilabilir)
                    uyarilar.Add("Kart salt okunur: ayarlar kaydedilemez. Klavyeyi 9 numaralı tuşa basmadan yeniden tak.");
            }

            if (al("KAYIT_SAYISI") != null) ekle("Bu çalışmadaki kayıt", al("KAYIT_SAYISI"));

            long hataSayisi = Sayi(al("HATA_SAYISI"));
            if (hataSayisi >= 0)
            {
                ekle("Kart hataları", hataSayisi == 0 ? "yok" : hataSayisi + " (kart çalışmaya devam etti)");
                if (al("SON_HATA") != null)
                {
                    long once = Sayi(al("SON_HATA_ONCE"));
                    ekle("Son hata", al("SON_HATA") + (once >= 0 ? "  (" + SureYazisi(once) + " önce)" : ""));
                    uyarilar.Add("Kart yazılımında hata oldu: " + al("SON_HATA") + ". Kart çalışmaya devam ediyor; tekrarlarsa bildir.");
                }
            }
            if (al("ONCEKI_HATA") != null) ekle("Önceki çalışmada", al("ONCEKI_HATA"));

            // Kart 2.3+: uyku engelleyici ve güvenli güncelleme
            if (al("UYKU") != null) ekle("Uyku engelleyici", al("UYKU") == "1" ? "açık" : "kapalı");
            if (al("DENEME") != null) ekle("Güncelleme", "yeni yazılım deneniyor (birkaç saniye içinde onaylanır)");
            string geri = al("GERI_DONUS");
            if (geri != null)
            {
                int ayrac = geri.IndexOf('|');
                string eski = ayrac >= 0 ? geri.Substring(0, ayrac) : "?", neden = ayrac >= 0 ? geri.Substring(ayrac + 1) : geri;
                ekle("Son güncelleme", "çalışmadı, " + eski + " sürümüne geri dönüldü");
                uyarilar.Add("Son kart yazılımı güncellemesi çalışmadı; kart kendiliğinden önceki sürüme (" + eski +
                             ") döndü. Neden: " + neden);
            }

            return s;
        }

        private static long Sayi(string metin)
        {
            long n;
            return metin != null && long.TryParse(metin, out n) ? n : -1;
        }

        private static string Boyut(long bayt)
        {
            System.Globalization.CultureInfo tr = new System.Globalization.CultureInfo("tr-TR");
            if (bayt < 1024) return bayt + " B";
            if (bayt < 1024 * 1024) return (bayt / 1024.0).ToString(bayt < 10 * 1024 ? "0.0" : "0", tr) + " KB";
            return (bayt / (1024.0 * 1024)).ToString("0.0", tr) + " MB";
        }

        private static string SureYazisi(long sn)
        {
            if (sn < 60) return sn + " sn";
            long dk = sn / 60, sa = dk / 60, gun = sa / 24;
            if (gun > 0) return gun + " gün " + (sa % 24) + " sa";
            if (sa > 0) return sa + " sa " + (dk % 60) + " dk";
            return dk + " dk";
        }

        private static string SifirlamaAdi(string kod)
        {
            switch (kod)
            {
                case "POWER_ON": return "Güç verildi (USB takıldı)";
                case "BROWNOUT": return "Voltaj düştü";
                case "SOFTWARE": return "Yazılım yeniden başlattı";
                case "RESET_PIN": return "Reset tuşu";
                case "WATCHDOG": return "Donma koruması (watchdog)";
                case "DEEP_SLEEP_ALARM": return "Uykudan uyandı";
                case "RESCUE_DEBUG": return "Hata ayıklayıcı";
                default: return "Bilinmiyor (" + kod + ")";
            }
        }

        private static string BaslamaAdi(string kod)
        {
            switch (kod)
            {
                case "STARTUP": return "Normal açılış";
                case "AUTO_RELOAD": return "Dosya değişince otomatik";
                case "SUPERVISOR_RELOAD": return "Uygulama yeniden başlattı";
                case "REPL_RELOAD": return "Konsoldan (Ctrl+D)";
                default: return kod;
            }
        }
    }
}
