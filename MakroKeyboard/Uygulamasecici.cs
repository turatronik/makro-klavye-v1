using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Tuşun ne açacağını seçtiren pencere: yüklü uygulamalar (Microsoft Store dahil),
    // herhangi bir dosya, bir klasör ya da bir web sitesi.
    // Ana formla aynı tema: lacivert başlık, yuvarlak kartlar, mavi vurgu.
    // Ana forma sonuç: SecilenAd + SecilenHedef
    //   (.exe / dosya / klasör yolu, shell:AppsFolder\<kimlik> ya da https://... adresi)
    public class UygulamaSecici : Form
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

        // ---------- Sonuç ----------
        public string SecilenAd;
        public string SecilenHedef;

        // ---------- Renkler (ana formla aynı) ----------
        private static readonly Color Mavi = Color.FromArgb(16, 104, 232);
        private static readonly Color MaviKoyu = Color.FromArgb(10, 82, 190);
        private static readonly Color Lacivert = Tema.Yazi;
        private static readonly Color SolukYazi = Tema.SolukYazi;
        private static readonly Color Kenar = Tema.Kenar;

        private static readonly Color[] AvatarRenkleri =
        {
            Color.FromArgb(16, 104, 232), Color.FromArgb(14, 165, 160), Color.FromArgb(124, 58, 237),
            Color.FromArgb(234, 88, 12), Color.FromArgb(219, 39, 119), Color.FromArgb(22, 163, 74),
            Color.FromArgb(202, 138, 4), Color.FromArgb(71, 85, 105)
        };

        // ---------- Veri ----------
        private class Oge
        {
            public string Ad, Id;
            public bool Store;      // Microsoft Store (UWP) uygulaması mı?
        }

        private List<Oge> uygulamalar = new List<Oge>();
        private List<Oge> gosterilen = new List<Oge>();
        private int filtre;          // 0 Tümü, 1 Store, 2 Programlar
        private int hoverIndex = -1;
        private bool yuklendi;

        // ---------- Kontroller ----------
        private TextBox txtAra;
        private Label lblIpucu, lblBos, lblDurum;
        private AkiciListe liste;
        private readonly YuvarlakPanel[] filtrePaneller = new YuvarlakPanel[3];
        private readonly Label[] filtreYazilar = new Label[3];

        private readonly Font adFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        private readonly Font altFont = new Font("Segoe UI", 8.5f);
        private readonly Font harfFont = new Font("Segoe UI", 13f, FontStyle.Bold);

        // Titremeyi azaltan liste
        private class AkiciListe : ListBox
        {
            public AkiciListe() { DoubleBuffered = true; }
        }

        // =================================================================
        public UygulamaSecici()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 620);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            KeyPreview = true;

            BaslikOlustur();
            AramaKutusuOlustur();
            FiltreleriOlustur();
            ListeOlustur();
            AltAlanOlustur();

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            };
            MouseDown += SurukleBaslat;
            Shown += async (s, e) => await Yukle();
        }

        // ---------- Zemin ve köşeler ----------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientRectangle.Width < 1 || ClientRectangle.Height < 1) return;
            using (var fr = new LinearGradientBrush(ClientRectangle,
                Tema.Zemin1, Tema.Zemin2, 50f))
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

        private void SurukleBaslat(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        // ---------- Başlık çubuğu ----------
        private void BaslikOlustur()
        {
            Panel baslik = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Tema.Baslik };

            Label ikon = new Label
            {
                Text = "\uE71D",
                Font = new Font("Segoe MDL2 Assets", 13f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };
            Label yazi = new Label
            {
                Text = "Ne açılsın?",
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
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
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

        // ---------- Arama kutusu ----------
        private void AramaKutusuOlustur()
        {
            YuvarlakPanel kutu = new YuvarlakPanel
            {
                Location = new Point(20, 62),
                Size = new Size(420, 46),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Kenar
            };

            Label buyutec = new Label
            {
                Text = "\uE721",
                Font = new Font("Segoe MDL2 Assets", 12f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                Location = new Point(10, 0),
                Size = new Size(34, 46),
                TextAlign = ContentAlignment.MiddleCenter
            };

            txtAra = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Font = new Font("Segoe UI", 11.5f),
                Location = new Point(48, 12),
                Width = 360
            };
            txtAra.TextChanged += (s, e) =>
            {
                lblIpucu.Visible = txtAra.Text.Length == 0;
                Filtrele();
            };
            txtAra.KeyDown += AramaTusu;

            lblIpucu = new Label
            {
                Text = "Uygulama ara…",
                Font = new Font("Segoe UI", 11.5f),
                ForeColor = Tema.Pasif,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(48, 12),
                Cursor = Cursors.IBeam
            };
            lblIpucu.Click += (s, e) => txtAra.Focus();

            kutu.Controls.Add(buyutec);
            kutu.Controls.Add(txtAra);
            kutu.Controls.Add(lblIpucu);
            kutu.Click += (s, e) => txtAra.Focus();
            Controls.Add(kutu);
        }

        // Arama kutusundayken ok tuşlarıyla listede gezin, Enter ile seç
        private void AramaTusu(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && liste.Items.Count > 0)
            {
                liste.SelectedIndex = Math.Min(liste.Items.Count - 1, liste.SelectedIndex + 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && liste.Items.Count > 0)
            {
                liste.SelectedIndex = Math.Max(0, liste.SelectedIndex - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Sec();
            }
        }

        // ---------- Filtre sekmeleri: Tümü / Store / Programlar ----------
        private void FiltreleriOlustur()
        {
            string[] adlar = { "Tümü", "Microsoft Store", "Programlar" };
            int[] genislik = { 80, 140, 110 };
            int x = 20;

            for (int i = 0; i < 3; i++)
            {
                int indeks = i;
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(x, 120),
                    Size = new Size(genislik[i], 32),
                    Yaricap = 16,
                    Cursor = Cursors.Hand
                };
                Label l = new Label
                {
                    Text = adlar[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                p.Controls.Add(l);

                EventHandler tik = (s, e) => { filtre = indeks; FiltreStilleri(); Filtrele(); txtAra.Focus(); };
                p.Click += tik;
                l.Click += tik;
                EventHandler gir = (s, e) => p.Hover = true;
                EventHandler cik = (s, e) =>
                    p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));
                p.MouseEnter += gir; l.MouseEnter += gir;
                p.MouseLeave += cik; l.MouseLeave += cik;

                filtrePaneller[i] = p;
                filtreYazilar[i] = l;
                Controls.Add(p);
                x += genislik[i] + 10;
            }

            FiltreStilleri();
        }

        private void FiltreStilleri()
        {
            for (int i = 0; i < 3; i++)
            {
                bool aktif = (i == filtre);
                filtrePaneller[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                filtrePaneller[i].KenarRengi = aktif ? Mavi : Kenar;
                filtrePaneller[i].HoverRengi = aktif ? Color.Empty : Tema.Hover;
                filtreYazilar[i].ForeColor = aktif ? Color.White : Lacivert;
                filtrePaneller[i].Invalidate(true);
            }
        }

        // ---------- Liste ----------
        private void ListeOlustur()
        {
            YuvarlakPanel kart = new YuvarlakPanel
            {
                Location = new Point(20, 164),
                Size = new Size(420, 300),
                Yaricap = 14,
                DolguRengi = Tema.Kart,
                KenarRengi = Kenar
            };

            liste = new AkiciListe
            {
                Location = new Point(6, 8),
                Size = new Size(408, 284),
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 58,
                IntegralHeight = false
            };
            liste.DrawItem += ListeCiz;
            liste.DoubleClick += (s, e) => Sec();
            liste.SelectedIndexChanged += (s, e) => liste.Invalidate();
            liste.MouseMove += (s, e) =>
            {
                int i = liste.IndexFromPoint(e.Location);
                if (i != hoverIndex) { hoverIndex = i; liste.Invalidate(); }
            };
            liste.MouseLeave += (s, e) => { hoverIndex = -1; liste.Invalidate(); };

            lblBos = new Label
            {
                Text = "Uygulamalar yükleniyor…",
                Location = new Point(6, 8),
                Size = new Size(408, 284),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SolukYazi,
                BackColor = Tema.Kart,
                Font = new Font("Segoe UI", 10.5f)
            };

            kart.Controls.Add(liste);
            kart.Controls.Add(lblBos);
            lblBos.BringToFront();
            Controls.Add(kart);
        }

        private void ListeCiz(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= gosterilen.Count) return;

            Oge u = gosterilen[e.Index];
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var zemin = new SolidBrush(Tema.Kart))
                g.FillRectangle(zemin, e.Bounds);

            bool secili = (e.State & DrawItemState.Selected) != 0;
            bool hover = (e.Index == hoverIndex);

            Rectangle kutu = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);

            if (secili || hover)
            {
                using (var yol = YuvarlakPanel.Yol(new RectangleF(kutu.X, kutu.Y, kutu.Width - 1, kutu.Height - 1), 10))
                using (var dolgu = new SolidBrush(secili ? Tema.Secili : Tema.Hover))
                {
                    g.FillPath(dolgu, yol);
                    if (secili)
                        using (var kalem = new Pen(Mavi, 1.5f))
                            g.DrawPath(kalem, yol);
                }
            }

            // Avatar: adın ilk harfi, ada göre sabit renk
            Rectangle av = new Rectangle(kutu.X + 12, kutu.Y + (kutu.Height - 38) / 2, 38, 38);
            using (var b = new SolidBrush(AvatarRengi(u.Ad)))
                g.FillEllipse(b, av);

            StringFormat ortala = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(IlkHarf(u.Ad), harfFont, Brushes.White, av, ortala);

            // Ad + tür
            StringFormat sol = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            float metinX = av.Right + 12;
            float metinW = kutu.Right - metinX - 12;

            using (var b = new SolidBrush(Lacivert))
                g.DrawString(u.Ad, adFont, b, new RectangleF(metinX, kutu.Y + 8, metinW, 22), sol);

            using (var b = new SolidBrush(u.Store ? Mavi : SolukYazi))
                g.DrawString(u.Store ? "Microsoft Store" : "Program", altFont, b,
                             new RectangleF(metinX, kutu.Y + 29, metinW, 18), sol);
        }

        private static Color AvatarRengi(string ad)
        {
            int toplam = 0;
            foreach (char c in ad) toplam += c;
            return AvatarRenkleri[toplam % AvatarRenkleri.Length];
        }

        private static string IlkHarf(string ad)
        {
            foreach (char c in ad)
                if (char.IsLetterOrDigit(c)) return c.ToString().ToUpper();
            return "?";
        }

        // ---------- Alt alan: dosya / klasör / site + durum + düğmeler ----------
        private void AltAlanOlustur()
        {
            // Uygulama dışında açılabilecekler
            Controls.Add(Dugme("Dosya…", 20, 474, 132, 36, 1, DosyaSec));
            Controls.Add(Dugme("Klasör…", 164, 474, 132, 36, 1, KlasorSec));
            Controls.Add(Dugme("Web sitesi…", 308, 474, 132, 36, 1, SiteSec));

            lblDurum = new Label
            {
                Location = new Point(24, 522),
                Size = new Size(412, 20),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                Text = "Uygulamalar yükleniyor…"
            };
            Controls.Add(lblDurum);

            Controls.Add(Dugme("İptal", 250, 556, 84, 42, 1, () => DialogResult = DialogResult.Cancel));
            Controls.Add(Dugme("SEÇ", 344, 556, 96, 42, 0, Sec));
        }

        // tur 0: dolu mavi (ana eylem), tur 1: beyaz kenarlıklı
        private YuvarlakPanel Dugme(string yazi, int x, int y, int w, int h, int tur, Action tik)
        {
            bool ana = (tur == 0);

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

            EventHandler gir = (s, e) => p.Hover = true;
            EventHandler cik = (s, e) =>
                p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));
            EventHandler bas = (s, e) => tik();

            p.MouseEnter += gir; l.MouseEnter += gir;
            p.MouseLeave += cik; l.MouseLeave += cik;
            p.Click += bas; l.Click += bas;
            return p;
        }

        // =================================================================
        //  VERİ
        // =================================================================
        private async Task Yukle()
        {
            try
            {
                List<KeyValuePair<string, string>> ham = await Task.Run(() => UygulamalariOku());

                uygulamalar = ham.Select(k => new Oge
                {
                    Ad = k.Key,
                    Id = k.Value,
                    Store = !File.Exists(k.Value) && k.Value.Contains("!")
                }).ToList();
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    lblBos.Text = "Liste alınamadı.\nAşağıdan dosya, klasör ya da site seçebilirsin.";
                    lblDurum.Text = ex.Message;
                }
                return;
            }

            if (IsDisposed) return;

            yuklendi = true;
            Filtrele();
            txtAra.Focus();
        }

        private void Filtrele()
        {
            string ara = txtAra.Text.Trim();

            gosterilen = uygulamalar.Where(u =>
                (filtre == 0 || (filtre == 1 && u.Store) || (filtre == 2 && !u.Store)) &&
                (ara.Length == 0 || u.Ad.IndexOf(ara, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .ToList();

            hoverIndex = -1;
            liste.BeginUpdate();
            liste.Items.Clear();
            foreach (Oge u in gosterilen) liste.Items.Add(u.Ad);
            liste.EndUpdate();

            if (liste.Items.Count > 0) liste.SelectedIndex = 0;

            if (!yuklendi) return;

            lblBos.Visible = gosterilen.Count == 0;
            lblBos.Text = "Sonuç bulunamadı";
            lblDurum.Text = gosterilen.Count == uygulamalar.Count
                ? uygulamalar.Count + " uygulama   •   Çift tıkla ya da Enter'a bas"
                : gosterilen.Count + " / " + uygulamalar.Count + " uygulama";
        }

        private void Sec()
        {
            int i = liste.SelectedIndex;
            if (i < 0 || i >= gosterilen.Count)
            {
                lblDurum.Text = "Önce listeden bir uygulama seç.";
                return;
            }

            Oge u = gosterilen[i];
            SecilenAd = u.Ad;

            // Klasik uygulamalarda kimlik çoğu zaman gerçek .exe yoludur; Store uygulamalarında "Paket!Uygulama"
            SecilenHedef = File.Exists(u.Id) ? u.Id : @"shell:AppsFolder\" + u.Id;

            DialogResult = DialogResult.OK;
        }

        // Herhangi bir dosya: .exe çalışır, belge / resim / müzik varsayılan programıyla açılır
        private void DosyaSec()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Tüm dosyalar (*.*)|*.*|Uygulamalar (*.exe)|*.exe";
                dialog.Title = "Tuşa basınca açılacak dosyayı seç";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                SecilenAd = Path.GetFileNameWithoutExtension(dialog.FileName);
                SecilenHedef = dialog.FileName;
            }

            DialogResult = DialogResult.OK;
        }

        private void KlasorSec()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Tuşa basınca açılacak klasörü seç";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string yol = dialog.SelectedPath;
                string ad = Path.GetFileName(yol.TrimEnd(Path.DirectorySeparatorChar));
                SecilenAd = ad.Length > 0 ? ad : yol.Replace(":", "").Replace("\\", "") + " sürücüsü";   // C:\ -> "C sürücüsü"
                SecilenHedef = yol;
            }

            DialogResult = DialogResult.OK;
        }

        private void SiteSec()
        {
            string adres = GirisKutusu.Sor(this, "Web sitesi",
                "Tuşa basınca varsayılan tarayıcıda açılacak adresi yaz.",
                "örn. youtube.com", "", a => SiteAdresi(a) == null ? "Geçerli bir adres yaz (örn. youtube.com)." : null);
            if (adres == null) return;

            SecilenHedef = SiteAdresi(adres);
            SecilenAd = SiteAdi(SecilenHedef);
            DialogResult = DialogResult.OK;
        }

        // "youtube.com" -> "https://youtube.com"; geçersizse null
        public static string SiteAdresi(string girdi)
        {
            girdi = (girdi ?? "").Trim();
            if (girdi.Length == 0 || girdi.Contains(" ") || girdi.Contains("|")) return null;

            if (!girdi.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !girdi.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                girdi = "https://" + girdi;

            Uri uri;
            if (!Uri.TryCreate(girdi, UriKind.Absolute, out uri) || uri.Host.IndexOf('.') < 0) return null;
            return uri.AbsoluteUri;
        }

        // "https://www.youtube.com/..." -> "Youtube"
        public static string SiteAdi(string adres)
        {
            Uri uri;
            if (!Uri.TryCreate(adres, UriKind.Absolute, out uri)) return "Web sitesi";

            string host = uri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host.Substring(4);

            string ad = host.Split('.')[0];
            return ad.Length == 0 ? "Web sitesi" : char.ToUpper(ad[0]) + ad.Substring(1);
        }

        // Önce Win+R'deki "shell:AppsFolder" ile aynı klasörden okur; olmazsa PowerShell'e düşer
        private static List<KeyValuePair<string, string>> UygulamalariOku()
        {
            List<KeyValuePair<string, string>> ham = null;

            try { ham = ShellKlasorundenOku(); }
            catch (Exception) { }

            if (ham == null || ham.Count == 0)
                ham = PowerShellIleOku();

            HashSet<string> goruldu = new HashSet<string>();
            List<KeyValuePair<string, string>> sonuc = new List<KeyValuePair<string, string>>();

            foreach (var u in ham)
                if (goruldu.Add(u.Key + "|" + u.Value)) sonuc.Add(u);

            sonuc.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.CurrentCultureIgnoreCase));
            return sonuc;
        }

        // Shell COM nesneleri tek iş parçacıklı (STA) çalışmalı, o yüzden ayrı bir thread kullanılır
        private static List<KeyValuePair<string, string>> ShellKlasorundenOku()
        {
            List<KeyValuePair<string, string>> sonuc = null;
            Exception hata = null;

            Thread kanal = new Thread(() =>
            {
                try { sonuc = ShellKlasorunuTara(); }
                catch (Exception ex) { hata = ex; }
            });
            kanal.SetApartmentState(ApartmentState.STA);
            kanal.Start();
            kanal.Join();

            if (hata != null) throw hata;
            return sonuc;
        }

        // shell:AppsFolder klasöründeki her öğenin Adı ve Yolu (Store uygulamalarında "Paket!Uygulama" kimliği)
        private static List<KeyValuePair<string, string>> ShellKlasorunuTara()
        {
            List<KeyValuePair<string, string>> sonuc = new List<KeyValuePair<string, string>>();
            BindingFlags bf = BindingFlags.InvokeMethod | BindingFlags.GetProperty;

            Type shellTipi = Type.GetTypeFromProgID("Shell.Application");
            if (shellTipi == null) return sonuc;

            object shell = Activator.CreateInstance(shellTipi);
            object klasor = shellTipi.InvokeMember("NameSpace", bf, null, shell, new object[] { "shell:AppsFolder" });
            if (klasor == null) return sonuc;

            object ogeler = klasor.GetType().InvokeMember("Items", bf, null, klasor, null);
            int sayi = (int)ogeler.GetType().InvokeMember("Count", bf, null, ogeler, null);

            for (int i = 0; i < sayi; i++)
            {
                object oge = ogeler.GetType().InvokeMember("Item", bf, null, ogeler, new object[] { i });
                string ad = oge.GetType().InvokeMember("Name", bf, null, oge, null) as string;
                string yol = oge.GetType().InvokeMember("Path", bf, null, oge, null) as string;

                if (string.IsNullOrWhiteSpace(ad) || string.IsNullOrWhiteSpace(yol)) continue;
                sonuc.Add(new KeyValuePair<string, string>(ad.Trim(), yol.Trim()));
            }

            return sonuc;
        }

        // Yedek yöntem: PowerShell'in Get-StartApps komutu (Windows 10 1709+)
        private static List<KeyValuePair<string, string>> PowerShellIleOku()
        {
            List<KeyValuePair<string, string>> sonuc = new List<KeyValuePair<string, string>>();

            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                "Get-StartApps | ForEach-Object { $_.Name + '|' + $_.AppID }\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            string cikti;
            using (Process p = Process.Start(psi))
            {
                cikti = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }

            foreach (string satir in cikti.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = satir.LastIndexOf('|');
                if (i <= 0) continue;

                string ad = satir.Substring(0, i).Trim();
                string id = satir.Substring(i + 1).Trim();

                if (ad.Length == 0 || id.Length == 0) continue;
                sonuc.Add(new KeyValuePair<string, string>(ad, id));
            }

            return sonuc;
        }
    }
}