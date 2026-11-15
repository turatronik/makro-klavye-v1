using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Çok adımlı makro düzenleyici.
    // Adımlar: kısayol (CTRL+C), metin (TEXT:...), bekleme (WAIT:ms), hazır komut (MUTE...)
    // "Kayıt al" ile klavyede yapılanlar (tuşlar, yazılan metin, aradaki beklemeler) adım olarak eklenir.
    // Karta giden biçim: MACRO:adım1|adım2|...   (adım içindeki | ve \ karakterleri \| ve \\ olarak kaçırılır)
    public class MakroForm : Form
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

        public const int MaxAdim = 20;
        public const int MaxBekle = 5000;    // kart tarafındaki sınırla aynı

        // ---------- Sonuç ----------
        public string Komut { get; private set; }

        // ---------- Veri ----------
        private readonly string[,] hazirlar;
        private readonly List<string> adimlar;
        private int tur;                     // 0 Kısayol, 1 Metin, 2 Bekle, 3 Komut, 4 Kaydır, 5 Tekrar
        private string yakalanan = "";

        // ---------- Kontroller ----------
        private AkiciListe liste;
        private Label lblBos, lblSayac, lblDurum;
        private readonly YuvarlakPanel[] turPaneller = new YuvarlakPanel[7];
        private readonly Label[] turYazilar = new Label[7];

        // Tut: tuşu / fare tuşunu basılı tut ya da bırak (PRESS:x / RELEASE:x, kart 2.5+)
        private ComboBox cmbTutYon, cmbTutTus;
        public static readonly string[,] TutTuslari =
        {
            { "SHIFT", "Shift" }, { "CTRL", "Ctrl" }, { "ALT", "Alt" }, { "WINDOWS", "Windows" },
            { "MOUSE_LEFT", "Sol tık" }, { "MOUSE_RIGHT", "Sağ tık" }, { "MOUSE_MIDDLE", "Orta tık" },
            { "SPACE", "Boşluk" }, { "W", "W" }, { "A", "A" }, { "S", "S" }, { "D", "D" },
            { "UP_ARROW", "Yukarı ok" }, { "DOWN_ARROW", "Aşağı ok" }, { "LEFT_ARROW", "Sol ok" }, { "RIGHT_ARROW", "Sağ ok" }
        };

        // Kaydır: yön + çentik sayısı; Tekrar: son kaç adım, toplam kaç kez (kart 1.9 ve sonrası)
        private ComboBox cmbYon;
        private NumericUpDown sayiCentik, sayiTekrarAdim, sayiTekrarKez, sayiFareX, sayiFareY;
        private Label lblCentik, lblTekrar1, lblTekrar2, lblTekrar3, lblFareX, lblFareY;
        private const int YonTasi = 2;          // cmbYon: 0 aşağı kaydır, 1 yukarı kaydır, 2 imleci taşı
        public const int MaxTekrar = 100;

        // Adım düzenleme ve sürükleyerek sıralama
        private int duzenlenen = -1;          // düzenlenen adımın sırası (-1: yeni adım ekleniyor)
        private YuvarlakPanel btnEkle;
        private int surukKaynak = -1, surukHedef = -1;
        private Point surukBaslangic;
        private bool surukleniyor;
        private readonly ToolTip listeIpucu = new ToolTip();
        private static readonly Color Amber = Color.FromArgb(217, 119, 6);

        // Makrolarım'dan yüklenen (ya da az önce saklanan) makronun adı: "Güncelle" ile üzerine yazılır
        private string yuklenenMakro;
        private TextBox txtYakala, txtMetin;
        private NumericUpDown sayiBekle;
        private Label lblMs;
        private ComboBox cmbKomut;
        private readonly KlavyeKancasi kanca = new KlavyeKancasi();
        private readonly FareKancasi fareKanca = new FareKancasi();   // kayıtta tıklama ve tekerlek

        // ---------- Makro kaydı ----------
        private bool kaydediliyor;
        private bool ilkKayit;
        private DateTime sonKayit;
        private int kayitMetin = -1;        // kayıt sırasında uzatılan metin adımının sırası
        private int kayitBaslangic;         // kayıt başlarken kaç adım vardı
        private YuvarlakPanel btnKayit;
        private CheckBox chkBekle, chkFare;

        // Gizli metin adımı ve saklanan makrolar
        private bool gizliYeni;           // eklenecek metin adımı gizli olsun mu
        private Label lblGoz;
        private YuvarlakPanel btnMakrolar;
        private static readonly Color Kirmizi = Color.FromArgb(220, 38, 38);
        private static readonly Color KirmiziKoyu = Color.FromArgb(185, 28, 28);

        private readonly Font satirFont = new Font("Segoe UI", 10f);
        private readonly Font notFont = new Font("Segoe UI", 10f, FontStyle.Italic);
        private readonly Font numaraFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        private class AkiciListe : ListBox
        {
            public AkiciListe() { DoubleBuffered = true; }
        }

        // =================================================================
        public MakroForm(string mevcutKomut, string[,] hazirKomutlar)
        {
            hazirlar = hazirKomutlar;
            adimlar = Coz(mevcutKomut);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 640);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            KeyPreview = true;

            BaslikOlustur();
            ListeOlustur();
            TurleriOlustur();
            GirisOlustur();
            AltDugmeleriOlustur();

            // Kısayol yakalama kutusu odaktayken ya da kayıt sürerken tuşlar sisteme gitmeden yakalanır
            // (Alt+F4, Alt+Tab dahil)
            kanca.Etkin = () => kaydediliyor || (txtYakala.Focused && tur == 0);
            kanca.TusBasildi += (ctrl, shift, alt, win, tus) =>
                BeginInvoke(new Action(() =>
                {
                    if (kaydediliyor) KayitTusu(ctrl, shift, alt, win, tus);
                    else Yakala(ctrl, shift, alt, win, tus);
                }));
            // Kayıtta fare: tıklama ve tekerlek adım olur (Durdur düğmesine tıklama hariç)
            fareKanca.Etkin = () => kaydediliyor && chkFare.Checked;
            fareKanca.Gecir = nokta => btnKayit.RectangleToScreen(btnKayit.ClientRectangle).Contains(nokta);
            fareKanca.Tiklandi += tus => BeginInvoke(new Action(() =>
                KayitFare(tus == MouseButtons.Right ? "MOUSE_RIGHT" : tus == MouseButtons.Middle ? "MOUSE_MIDDLE" : "MOUSE_LEFT", 0)));
            fareKanca.TekerlekDondu += centik => BeginInvoke(new Action(() =>
                KayitFare(centik > 0 ? "SCROLL_UP" : "SCROLL_DOWN", Math.Abs(centik))));

            Deactivate += (s, e) =>
            {
                KaydiDurdur();      // başka pencereye geçilince kayıt biter
                kanca.Durdur();
            };

            KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape) return;
                if (duzenlenen >= 0)
                {
                    DuzenlemeyiBitir();          // önce düzenleme bırakılır, ikinci Esc pencereyi kapatır
                    Durum("Düzenleme bırakıldı.");
                }
                else DialogResult = DialogResult.Cancel;
            };
            MouseDown += SurukleBaslat;

            ListeyiYenile();
            TurSec(0);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            kanca.Dispose();
            fareKanca.Dispose();
            base.OnFormClosed(e);
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

        // ---------- Başlık ----------
        private void BaslikOlustur()
        {
            Panel baslik = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Tema.Baslik };

            Label ikon = new Label
            {
                Text = "\uE8FD",
                Font = new Font("Segoe MDL2 Assets", 13f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };
            Label yazi = new Label
            {
                Text = "Makro Düzenle",
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
            kapat.Click += (s, e) => DialogResult = DialogResult.Cancel;

            baslik.Controls.Add(ikon);
            baslik.Controls.Add(yazi);
            baslik.Controls.Add(kapat);

            baslik.MouseDown += SurukleBaslat;
            ikon.MouseDown += SurukleBaslat;
            yazi.MouseDown += SurukleBaslat;

            Controls.Add(baslik);
        }

        private Label Baslik(string metin, int x, int y)
        {
            Label l = new Label
            {
                Text = metin,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Lacivert,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(x, y)
            };
            Controls.Add(l);
            return l;
        }

        // ---------- Adım listesi ----------
        private void ListeOlustur()
        {
            Baslik("Adımlar", 22, 58);

            lblSayac = new Label
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(300, 58),
                Size = new Size(140, 22)
            };
            Controls.Add(lblSayac);

            YuvarlakPanel kart = new YuvarlakPanel
            {
                Location = new Point(20, 86),
                Size = new Size(420, 246),
                Yaricap = 14,
                DolguRengi = Tema.Kart,
                KenarRengi = Kenar
            };

            liste = new AkiciListe
            {
                Location = new Point(6, 8),
                Size = new Size(408, 230),
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 42,
                IntegralHeight = false
            };
            liste.SelectionMode = SelectionMode.MultiExtended;   // Ctrl / Shift ile birden çok adım
            liste.DrawItem += AdimCiz;
            liste.SelectedIndexChanged += (s, e) =>
            {
                // Başka bir adıma geçilince düzenleme bırakılır
                if (duzenlenen >= 0 && !surukleniyor && !liste.SelectedIndices.Contains(duzenlenen))
                {
                    DuzenlemeyiBitir();
                    Durum("Düzenleme bırakıldı.");
                }
                liste.Invalidate();
            };
            liste.DoubleClick += (s, e) =>
            {
                int i = liste.IndexFromPoint(liste.PointToClient(Cursor.Position));
                if (i >= 0 && i < adimlar.Count) AdimiDuzenle(i);
            };
            liste.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { Sil(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.D) { Cogalt(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.C) { Kopyala(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.V) { Yapistir(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter && liste.SelectedIndices.Count == 1)
                {
                    AdimiDuzenle(liste.SelectedIndex);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            // Sürükleyerek sıralama
            liste.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                surukKaynak = liste.IndexFromPoint(e.Location);
                surukBaslangic = e.Location;
            };
            liste.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || surukKaynak < 0 || surukKaynak >= adimlar.Count) return;

                if (!surukleniyor)
                {
                    if (Math.Abs(e.Y - surukBaslangic.Y) < 8) return;
                    surukleniyor = true;
                    DuzenlemeyiBitir();
                    liste.Cursor = Cursors.SizeNS;
                }

                int yuva = liste.TopIndex + (int)Math.Round(e.Y / (double)liste.ItemHeight);
                yuva = Math.Max(0, Math.Min(adimlar.Count, yuva));
                if (yuva != surukHedef)
                {
                    surukHedef = yuva;
                    liste.Invalidate();
                }
            };
            liste.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && surukleniyor)
                {
                    int kaynak = surukKaynak, hedef = surukHedef;
                    surukleniyor = false;
                    surukKaynak = surukHedef = -1;
                    liste.Cursor = Cursors.Default;

                    if (hedef >= 0 && hedef != kaynak && hedef != kaynak + 1)
                    {
                        string adim = adimlar[kaynak];
                        adimlar.RemoveAt(kaynak);
                        int yeni = hedef > kaynak ? hedef - 1 : hedef;
                        adimlar.Insert(yeni, adim);
                        ListeyiYenile(yeni);
                        Durum((kaynak + 1) + ". adım " + (yeni + 1) + ". sıraya taşındı.");
                    }
                    else liste.Invalidate();
                    return;
                }
                surukKaynak = -1;

                if (e.Button != MouseButtons.Right) return;
                int i = liste.IndexFromPoint(e.Location);
                if (i < 0 || i >= adimlar.Count)
                {
                    BosMenu(liste, e.Location);   // boş yere sağ tık: yapıştır / not ekle
                    return;
                }
                if (!liste.SelectedIndices.Contains(i))
                {
                    liste.ClearSelected();
                    liste.SetSelected(i, true);
                }
                AdimMenusu(i, e.Location);
            };
            listeIpucu.SetToolTip(liste, "Çift tık: düzenle  ·  Sürükle: sırala  ·  Ctrl / Shift: birden çok seç  ·  " +
                                         "Ctrl+C / Ctrl+V: başka makroya kopyala  ·  Sağ tık: menü, not ekle");

            lblBos = new Label
            {
                Text = "Henüz adım yok.\nAşağıdan adım ekle ya da Kayıt al'a bas.",
                Location = new Point(6, 8),
                Size = new Size(408, 230),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SolukYazi,
                BackColor = Tema.Kart,
                Font = new Font("Segoe UI", 10.5f)
            };

            lblBos.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) BosMenu(lblBos, e.Location); };

            kart.Controls.Add(liste);
            kart.Controls.Add(lblBos);
            lblBos.BringToFront();
            Controls.Add(kart);

            // Sırala / sil
            Controls.Add(Dugme("\uE70E  Yukarı", 20, 340, 76, 34, false, () => Tasi(-1), 9f));
            Controls.Add(Dugme("\uE70D  Aşağı", 102, 340, 76, 34, false, () => Tasi(1), 9f));
            Controls.Add(Dugme("\uE8C8  Çoğalt", 184, 340, 86, 34, false, Cogalt, 9f));
            Controls.Add(Dugme("\uE74D  Sil", 276, 340, 56, 34, false, Sil, 9f));

            // Makro kaydı
            btnKayit = Dugme("\uE7C8  Kayıt al", 338, 340, 102, 34, false, KayitDegistir, 9f);
            Controls.Add(btnKayit);

            // Kayda neler girsin
            Controls.Add(new Label
            {
                Text = "Kayda:",
                AutoSize = true,
                Location = new Point(96, 61),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent
            });
            chkBekle = new CheckBox
            {
                Text = "beklemeler",
                Checked = true,
                AutoSize = true,
                Location = new Point(140, 60),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            Controls.Add(chkBekle);
            chkFare = new CheckBox
            {
                Text = "fare",
                Checked = true,
                AutoSize = true,
                Location = new Point(228, 60),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            Controls.Add(chkFare);
            listeIpucu.SetToolTip(chkFare, "Kayıtta fare tıklamaları ve tekerlek de adım olur (tıklama o sırada hiçbir yere gitmez)");
        }

        private void AdimCiz(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= adimlar.Count) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var zemin = new SolidBrush(Tema.Kart))
                g.FillRectangle(zemin, e.Bounds);

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

            // Sıra numarası
            bool notSatiri = adimlar[e.Index].StartsWith(NotOneki);
            Rectangle daire = new Rectangle(kutu.X + 10, kutu.Y + (kutu.Height - 24) / 2, 24, 24);
            using (var b = new SolidBrush(notSatiri ? SolukYazi : Mavi))
                g.FillEllipse(b, daire);

            StringFormat ortala = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString((e.Index + 1).ToString(), numaraFont, Brushes.White, daire, ortala);

            StringFormat sol = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using (var b = new SolidBrush(notSatiri ? SolukYazi : Lacivert))
                g.DrawString(AdimYazisi(adimlar[e.Index], hazirlar), notSatiri ? notFont : satirFont, b,
                    new RectangleF(daire.Right + 12, kutu.Y, kutu.Right - daire.Right - 20, kutu.Height), sol);

            // Düzenlenen adım: turuncu çerçeve
            if (e.Index == duzenlenen)
                using (var yol = YuvarlakPanel.Yol(new RectangleF(kutu.X + 1, kutu.Y + 1, kutu.Width - 3, kutu.Height - 3), 10))
                using (var kalem = new Pen(Amber, 2f))
                    g.DrawPath(kalem, yol);

            // Sürüklerken bırakılacak yer
            if (surukleniyor && (surukHedef == e.Index || (surukHedef == adimlar.Count && e.Index == adimlar.Count - 1)))
            {
                float y = surukHedef == e.Index ? e.Bounds.Top + 1 : e.Bounds.Bottom - 2;
                using (var kalem = new Pen(Mavi, 3f))
                    g.DrawLine(kalem, e.Bounds.Left + 8, y, e.Bounds.Right - 8, y);
            }
        }

        private void ListeyiYenile(int secilecek = -1)
        {
            liste.BeginUpdate();
            liste.Items.Clear();
            foreach (string a in adimlar) liste.Items.Add(a);
            liste.EndUpdate();

            if (secilecek >= 0 && secilecek < liste.Items.Count) liste.SelectedIndex = secilecek;

            lblBos.Visible = adimlar.Count == 0;
            lblSayac.Text = adimlar.Count + " / " + MaxAdim + " adım";
        }

        private void Tasi(int yon)
        {
            DuzenlemeyiBitir();
            if (liste.SelectedIndices.Count > 1)
            {
                Durum("Taşımak için tek bir adım seç ya da adımı fareyle sürükle.");
                return;
            }
            int i = liste.SelectedIndex;
            int j = i + yon;
            if (i < 0 || j < 0 || j >= adimlar.Count) return;

            string tmp = adimlar[i];
            adimlar[i] = adimlar[j];
            adimlar[j] = tmp;
            ListeyiYenile(j);
        }

        private void Sil()
        {
            List<int> secili = liste.SelectedIndices.Cast<int>().OrderByDescending(x => x).ToList();
            if (secili.Count == 0)
            {
                Durum("Silmek için listeden bir adım seç (Ctrl ile birden çok).");
                return;
            }

            DuzenlemeyiBitir();
            foreach (int i in secili) adimlar.RemoveAt(i);
            ListeyiYenile(Math.Min(secili.Last(), adimlar.Count - 1));
            Durum(secili.Count == 1 ? "Adım silindi." : secili.Count + " adım silindi.");
        }

        // Seçili adımların kopyası, son seçili adımın altına eklenir
        private void Cogalt()
        {
            List<int> secili = liste.SelectedIndices.Cast<int>().OrderBy(x => x).ToList();
            if (secili.Count == 0)
            {
                Durum("Çoğaltmak için listeden bir adım seç (Ctrl ile birden çok).");
                return;
            }
            if (adimlar.Count + secili.Count > MaxAdim)
            {
                Durum("En fazla " + MaxAdim + " adım olabilir; " + secili.Count + " adım daha eklenemez.");
                return;
            }

            DuzenlemeyiBitir();
            int konum = secili.Last() + 1;
            List<string> kopya = secili.Select(i => adimlar[i]).ToList();
            adimlar.InsertRange(konum, kopya);

            ListeyiYenile();
            for (int i = 0; i < kopya.Count; i++) liste.SetSelected(konum + i, true);
            Durum(kopya.Count == 1 ? "Adım çoğaltıldı." : kopya.Count + " adım çoğaltıldı.");
        }

        // Sağ tık menüsü
        private void AdimMenusu(int i, Point konum)
        {
            ContextMenuStrip m = TemaMenuRenkleri.Menu();
            int seciliSayi = liste.SelectedIndices.Count;

            if (seciliSayi == 1)
                m.Items.Add(TemaMenuRenkleri.Oge(adimlar[i].StartsWith(NotOneki) ? "Notu düzenle" : "Düzenle", () => AdimiDuzenle(i)));
            m.Items.Add(TemaMenuRenkleri.Oge(seciliSayi > 1 ? seciliSayi + " adımı çoğalt" : "Çoğalt", Cogalt));
            m.Items.Add(TemaMenuRenkleri.Oge(seciliSayi > 1 ? seciliSayi + " adımı sil" : "Sil", Sil));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(TemaMenuRenkleri.Oge(seciliSayi > 1 ? seciliSayi + " adımı kopyala (Ctrl+C)" : "Kopyala (Ctrl+C)", Kopyala));
            if (PanodakiAdimlar() != null)
                m.Items.Add(TemaMenuRenkleri.Oge("Altına yapıştır (Ctrl+V)", Yapistir));
            m.Items.Add(TemaMenuRenkleri.Oge("Altına not ekle…", () => NotDuzenle(-1)));

            if (seciliSayi == 1 && adimlar[i].StartsWith("TEXT:"))
            {
                string metin = adimlar[i].Substring(5);
                bool gizli = UygulamaAyarlari.GizliMi(metin);
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(TemaMenuRenkleri.Oge(gizli ? "Metni göster" : "Metni gizle (●●●●●●)", () =>
                {
                    UygulamaAyarlari.GizliAyarla(metin, !gizli);
                    liste.Invalidate();
                }));
            }

            m.Closed += (x, y) => BeginInvoke(new Action(m.Dispose));
            m.Show(liste, konum);
        }

        // Boş listeye / listenin boş yerine sağ tık
        private void BosMenu(Control kaynak, Point konum)
        {
            ContextMenuStrip m = TemaMenuRenkleri.Menu();
            if (PanodakiAdimlar() != null) m.Items.Add(TemaMenuRenkleri.Oge("Yapıştır (Ctrl+V)", Yapistir));
            m.Items.Add(TemaMenuRenkleri.Oge("Not ekle…", () => NotDuzenle(-1)));
            m.Closed += (x, y) => BeginInvoke(new Action(m.Dispose));
            m.Show(kaynak, konum);
        }

        // ---------- Makrolar arasında kopyala / yapıştır (Windows panosu üzerinden) ----------
        private const string PanoBasligi = "MAKRO_KLAVYE_ADIMLARI\n";

        private void Kopyala()
        {
            List<int> secili = liste.SelectedIndices.Cast<int>().OrderBy(x => x).ToList();
            if (secili.Count == 0)
            {
                Durum("Kopyalamak için listeden adım seç (Ctrl ile birden çok).");
                return;
            }
            try
            {
                Clipboard.SetText(PanoBasligi + Kodla(secili.Select(i => adimlar[i])));
                Durum(secili.Count + " adım kopyalandı. Başka bir makroda (ya da burada) Ctrl+V ile yapıştır.");
            }
            catch (Exception ex) { Durum("Kopyalanamadı: " + ex.Message); }
        }

        // Panoda makro adımı varsa onlar, yoksa null
        private static List<string> PanodakiAdimlar()
        {
            try
            {
                if (!Clipboard.ContainsText()) return null;
                string m = Clipboard.GetText().Replace("\r\n", "\n");
                if (!m.StartsWith(PanoBasligi)) return null;
                List<string> a = Coz(m.Substring(PanoBasligi.Length));
                return a.Count > 0 ? a : null;
            }
            catch (Exception) { return null; }
        }

        private void Yapistir()
        {
            if (kaydediliyor) return;
            List<string> yeni = PanodakiAdimlar();
            if (yeni == null)
            {
                Durum("Panoda makro adımı yok. Önce bir makroda adımları seçip Ctrl+C'ye bas.");
                return;
            }
            if (adimlar.Count + yeni.Count > MaxAdim)
            {
                Durum("En fazla " + MaxAdim + " adım olabilir; " + yeni.Count + " adım sığmaz (" +
                      (MaxAdim - adimlar.Count) + " yer var).");
                return;
            }

            DuzenlemeyiBitir();
            int konum = SonSecili() + 1;
            adimlar.InsertRange(konum, yeni);
            ListeyiYenile();
            for (int i = 0; i < yeni.Count; i++) liste.SetSelected(konum + i, true);
            Durum(yeni.Count + " adım yapıştırıldı.");
        }

        // ---------- Not: makroda yalnızca açıklama, klavye çalıştırmaz (kart 2.4+ atlar) ----------
        public const string NotOneki = "NOT:";

        // i < 0: seçili adımın altına yeni not; aksi halde o notu düzenle
        private void NotDuzenle(int i)
        {
            if (kaydediliyor) return;
            bool yeni = i < 0;
            if (yeni && adimlar.Count >= MaxAdim)
            {
                Durum("En fazla " + MaxAdim + " adım olabilir; not da bir adım sayılır.");
                return;
            }

            string eski = yeni ? "" : adimlar[i].Substring(NotOneki.Length);
            string not = GirisKutusu.Sor(this, yeni ? "Not ekle" : "Notu düzenle",
                "Kendin için açıklama (örn. \"sayfa yüklenene kadar bekliyor\"). Listede soluk görünür; klavye çalıştırmaz.",
                "Not", eski, d => d.Length == 0 ? "Bir şey yaz." : d.Length > 80 ? "En fazla 80 harf." : null);
            if (not == null) return;

            DuzenlemeyiBitir();
            if (yeni)
            {
                int konum = SonSecili() + 1;
                adimlar.Insert(konum, NotOneki + not);
                ListeyiYenile(konum);
                Durum("Not eklendi.");
            }
            else
            {
                adimlar[i] = NotOneki + not;
                ListeyiYenile(i);
                Durum("Not güncellendi.");
            }
        }

        // Adımı aşağıdaki giriş alanına yükler; EKLE düğmesi TAMAM olur ve adımın yerine yazar
        private void AdimiDuzenle(int i)
        {
            if (i < 0 || i >= adimlar.Count || kaydediliyor) return;
            if (adimlar[i].StartsWith(NotOneki))
            {
                NotDuzenle(i);
                return;
            }

            string a = adimlar[i];
            int k, n, centik, ms, fx, fy;
            int hazir = HazirSirasi(a);

            duzenlenen = -1;   // tür seçerken eski düzenleme karışmasın
            if (a.StartsWith("WAIT:") && int.TryParse(a.Substring(5), out ms))
            {
                TurSec(2);
                sayiBekle.Value = Math.Max(sayiBekle.Minimum, Math.Min(sayiBekle.Maximum, ms));
            }
            else if (a.StartsWith("TEXT:"))
            {
                TurSec(1);
                string metin = a.Substring(5);
                gizliYeni = UygulamaAyarlari.GizliMi(metin);
                GozGorunumu();
                txtMetin.Text = metin;
                txtMetin.SelectAll();
            }
            else if (TekrarCoz(a, out k, out n))
            {
                TurSec(5);
                sayiTekrarAdim.Value = Math.Max(sayiTekrarAdim.Minimum, Math.Min(sayiTekrarAdim.Maximum, k));
                sayiTekrarKez.Value = Math.Max(sayiTekrarKez.Minimum, Math.Min(sayiTekrarKez.Maximum, n));
            }
            else if (hazir >= 0)
            {
                TurSec(3);
                cmbKomut.SelectedIndex = hazir;
            }
            else if (KaydirmaCoz(a, out centik) != null)
            {
                TurSec(4);
                cmbYon.SelectedIndex = a.StartsWith("SCROLL_UP") ? 1 : 0;
                sayiCentik.Value = Math.Max(sayiCentik.Minimum, Math.Min(sayiCentik.Maximum, centik));
            }
            else if (a.StartsWith("PRESS:") || a.StartsWith("RELEASE:"))
            {
                TurSec(6);
                cmbTutYon.SelectedIndex = a.StartsWith("RELEASE:") ? 1 : 0;
                string tus = a.Substring(a.IndexOf(':') + 1);
                int sira = 0;
                for (int t = 0; t < TutTuslari.GetLength(0); t++)
                    if (TutTuslari[t, 0] == tus) sira = t;
                cmbTutTus.SelectedIndex = sira;
            }
            else if (FareTasiCoz(a, out fx, out fy))
            {
                TurSec(4);
                cmbYon.SelectedIndex = YonTasi;
                sayiFareX.Value = Math.Max(sayiFareX.Minimum, Math.Min(sayiFareX.Maximum, fx));
                sayiFareY.Value = Math.Max(sayiFareY.Minimum, Math.Min(sayiFareY.Maximum, fy));
            }
            else
            {
                TurSec(0);
                yakalanan = a;
                txtYakala.Text = Form1.Guzel(a);
            }

            if (!liste.SelectedIndices.Contains(i))
            {
                liste.ClearSelected();
                liste.SetSelected(i, true);
            }
            duzenlenen = i;
            DugmeYazisi(btnEkle, "\uE73E", "TAMAM", Color.White);
            liste.Invalidate();
            Durum((i + 1) + ". adım düzenleniyor. Değiştirip TAMAM'a bas" +
                  (tur == 2 ? " (süreyi değiştirdikçe adım hemen güncellenir)." : "."));
        }

        // Son seçili adımın sırası; seçim yoksa son adım
        private int SonSecili()
        {
            return liste.SelectedIndices.Count > 0 ? liste.SelectedIndices.Cast<int>().Max() : adimlar.Count - 1;
        }

        private void DuzenlemeyiBitir()
        {
            if (duzenlenen < 0) return;
            duzenlenen = -1;
            DugmeYazisi(btnEkle, "\uE710", "EKLE", Color.White);
            liste.Invalidate();
        }

        // Adım hazır komutlardan biri mi (değilse -1)
        private int HazirSirasi(string adim)
        {
            for (int i = 0; i < hazirlar.GetLength(0); i++)
                if (string.Equals(hazirlar[i, 1], adim, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // ---------- Adım türü seçimi ----------
        private void TurleriOlustur()
        {
            Baslik("Adım ekle", 22, 390);

            string[] adlar = { "Kısayol", "Metin", "Bekle", "Komut", "Fare", "Tekrar", "Tut" };

            for (int i = 0; i < adlar.Length; i++)
            {
                int indeks = i;
                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(20 + i * 60, 418),
                    Size = new Size(56, 32),
                    Yaricap = 16,
                    Cursor = Cursors.Hand
                };
                Label l = new Label
                {
                    Text = adlar[i],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                p.Controls.Add(l);

                EventHandler tik = (s, e) => TurSec(indeks);
                p.Click += tik;
                l.Click += tik;

                EventHandler gir = (s, e) => p.Hover = true;
                EventHandler cik = (s, e) =>
                    p.Hover = p.ClientRectangle.Contains(p.PointToClient(Cursor.Position));
                p.MouseEnter += gir; l.MouseEnter += gir;
                p.MouseLeave += cik; l.MouseLeave += cik;

                turPaneller[i] = p;
                turYazilar[i] = l;
                Controls.Add(p);
            }
        }

        private void TurSec(int t)
        {
            tur = t;

            for (int i = 0; i < turPaneller.Length; i++)
            {
                bool aktif = (i == t);
                turPaneller[i].DolguRengi = aktif ? Mavi : Tema.Kart;
                turPaneller[i].KenarRengi = aktif ? Mavi : Kenar;
                turPaneller[i].HoverRengi = aktif ? Color.Empty : Tema.Hover;
                turYazilar[i].ForeColor = aktif ? Color.White : Lacivert;
                turPaneller[i].Invalidate(true);
            }

            txtYakala.Visible = (t == 0);
            txtMetin.Visible = (t == 1);
            lblGoz.Visible = (t == 1);
            sayiBekle.Visible = (t == 2);
            lblMs.Visible = (t == 2);
            cmbKomut.Visible = (t == 3);
            cmbYon.Visible = (t == 4);
            FareGorunumu();
            sayiTekrarAdim.Visible = sayiTekrarKez.Visible = (t == 5);
            lblTekrar1.Visible = lblTekrar2.Visible = lblTekrar3.Visible = (t == 5);
            cmbTutYon.Visible = cmbTutTus.Visible = (t == 6);

            string[] ipuclari =
            {
                "Kutuya tıkla ve kısayola bas (Alt+Tab, Alt+F4 de olur).",
                KlavyeDuzeni.Turkce ? "Yazılacak metni gir (klavye " + KlavyeDuzeni.Adi + " düzeninde yazar)."
                                    : "Yazılacak metni gir (Türkçe harf için Ayarlar > Kart > Klavye ayarları).",
                "Sonraki adımdan önce beklenecek süre (en fazla 5000 ms).",
                "Listeden hazır bir komut seç.",
                "Tekerleği çevirir ya da imleci taşır (x: sağa +, y: aşağı +, piksel; taşıma kart 2.4 ister). " +
                    "Tıklama için Komut'tan Sol/Sağ Tık.",
                "Hemen üstündeki adımları toplam bu kadar kez çalıştırır. Seçili adımın altına eklenir (kart yazılımı 1.9 gerekir).",
                "Tuşu ya da fare tuşunu basılı tutar / bırakır. Sürükle: Sol tık basılı tut → Fare'den imleci taşı → Bırak. " +
                    "Makro bitince basılı kalan her şey bırakılır (kart 2.5 gerekir)."
            };
            Durum(ipuclari[t]);

            if (t == 0) txtYakala.Focus();
            else if (t == 1) txtMetin.Focus();
            else if (!kaydediliyor) kanca.Durdur();
        }

        // ---------- Giriş alanı + EKLE ----------
        private void GirisOlustur()
        {
            YuvarlakPanel kutu = new YuvarlakPanel
            {
                Location = new Point(20, 462),
                Size = new Size(316, 50),
                Yaricap = 12,
                DolguRengi = Tema.Kart,
                KenarRengi = Kenar
            };

            txtYakala = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Font = new Font("Segoe UI", 12f),
                Location = new Point(16, 13),
                Width = 284,
                ReadOnly = true,
                Cursor = Cursors.Hand
            };
            txtYakala.PreviewKeyDown += (s, e) => e.IsInputKey = true;
            txtYakala.KeyDown += (s, e) =>      // kanca çalışmazsa yedek yöntem
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                Yakala(e.Control, e.Shift, e.Alt, false, e.KeyCode);
            };
            txtYakala.GotFocus += (s, e) => { if (tur == 0) kanca.Baslat(); };
            txtYakala.LostFocus += (s, e) => { if (!kaydediliyor) kanca.Durdur(); };

            txtMetin = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Font = new Font("Segoe UI", 12f),
                Location = new Point(16, 13),
                Width = 252
            };

            // Göz: eklenecek metin gizli olsun (PIN, şifre)
            lblGoz = new Label
            {
                Text = "\uE7B3",
                Font = new Font("Segoe MDL2 Assets", 13f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(276, 0),
                Size = new Size(36, 50),
                Cursor = Cursors.Hand,
                Visible = false
            };
            lblGoz.Click += (s, e) =>
            {
                gizliYeni = !gizliYeni;
                GozGorunumu();
                Durum(gizliYeni ? "Bu metin gizli eklenecek: listede ●●●●●● görünür, klavye aynen yazar."
                                : "Metin görünür eklenecek.");
                txtMetin.Focus();
            };
            txtMetin.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AdimEkle();
                }
            };

            sayiBekle = new NumericUpDown
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 12f),
                ForeColor = Lacivert,
                BackColor = Tema.Kart,
                Minimum = 50,
                Maximum = MaxBekle,
                Increment = 50,
                Value = 200,
                Location = new Point(16, 12),
                Width = 120
            };
            lblMs = new Label
            {
                Text = "milisaniye",
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(146, 14)
            };

            cmbKomut = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11f),
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Location = new Point(10, 10),
                Width = 296
            };
            for (int i = 0; i < hazirlar.GetLength(0); i++) cmbKomut.Items.Add(hazirlar[i, 0]);
            if (cmbKomut.Items.Count > 0) cmbKomut.SelectedIndex = 0;

            kutu.Controls.Add(txtYakala);
            kutu.Controls.Add(txtMetin);
            kutu.Controls.Add(lblGoz);
            kutu.Controls.Add(sayiBekle);
            kutu.Controls.Add(lblMs);
            kutu.Controls.Add(cmbKomut);

            // Kaydır: [Aşağı ▾] [5] çentik
            cmbYon = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11f),
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Location = new Point(10, 10),
                Width = 120
            };
            cmbYon.Width = 152;
            cmbYon.Items.AddRange(new object[] { "Aşağı kaydır", "Yukarı kaydır", "İmleci taşı" });
            cmbYon.SelectedIndex = 0;
            cmbYon.SelectedIndexChanged += (s, e) => FareGorunumu();
            sayiCentik = SayiKutusu(1, 50, 3, 172, 60);
            lblCentik = KutuYazisi("çentik", 238);

            // İmleci taşı: x [100] y [0] (piksel)
            lblFareX = KutuYazisi("x", 168);
            sayiFareX = SayiKutusu(-4000, 4000, 100, 182, 58);
            lblFareY = KutuYazisi("y", 244);
            sayiFareY = SayiKutusu(-4000, 4000, 0, 258, 50);
            sayiFareX.Increment = sayiFareY.Increment = 10;

            // Tekrar: son [3] adım × [5] kez
            lblTekrar1 = KutuYazisi("Son", 14);
            sayiTekrarAdim = SayiKutusu(1, MaxAdim, 1, 52, 52);
            lblTekrar2 = KutuYazisi("adım ×", 110);
            sayiTekrarKez = SayiKutusu(2, MaxTekrar, 2, 172, 60);
            lblTekrar3 = KutuYazisi("kez", 240);

            // Tut: [Basılı tut ▾] [Shift ▾]
            cmbTutYon = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11f),
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Location = new Point(10, 10),
                Width = 130
            };
            cmbTutYon.Items.AddRange(new object[] { "Basılı tut", "Bırak" });
            cmbTutYon.SelectedIndex = 0;
            cmbTutTus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11f),
                BackColor = Tema.Kart,
                ForeColor = Lacivert,
                Location = new Point(148, 10),
                Width = 158,
                MaxDropDownItems = 16
            };
            for (int i = 0; i < TutTuslari.GetLength(0); i++) cmbTutTus.Items.Add(TutTuslari[i, 1]);
            cmbTutTus.SelectedIndex = 0;

            foreach (Control c in new Control[] { cmbTutYon, cmbTutTus, cmbYon, sayiCentik, lblCentik, lblFareX, sayiFareX, lblFareY, sayiFareY,
                                                  lblTekrar1, sayiTekrarAdim, lblTekrar2, sayiTekrarKez, lblTekrar3 })
            {
                c.Visible = false;
                kutu.Controls.Add(c);
            }

            kutu.Click += (s, e) => { if (tur == 0) txtYakala.Focus(); else if (tur == 1) txtMetin.Focus(); };
            Controls.Add(kutu);

            btnEkle = Dugme("\uE710  EKLE", 346, 462, 94, 50, true, AdimEkle, 10f);
            Controls.Add(btnEkle);

            // Bekleme adımı düzenlenirken süre değiştikçe adım hemen güncellenir
            sayiBekle.ValueChanged += (s, e) =>
            {
                if (duzenlenen < 0 || tur != 2 || duzenlenen >= adimlar.Count || !adimlar[duzenlenen].StartsWith("WAIT:")) return;
                adimlar[duzenlenen] = "WAIT:" + (int)sayiBekle.Value;
                liste.Invalidate();
            };

            lblDurum = new Label
            {
                Location = new Point(22, 520),
                Size = new Size(418, 38),
                Font = new Font("Segoe UI", 9f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent
            };
            Controls.Add(lblDurum);
        }

        // Fare sekmesi: kaydırmada çentik, taşımada x / y kutuları
        private void FareGorunumu()
        {
            bool fare = tur == 4, tasi = cmbYon.SelectedIndex == YonTasi;
            sayiCentik.Visible = lblCentik.Visible = fare && !tasi;
            lblFareX.Visible = sayiFareX.Visible = lblFareY.Visible = sayiFareY.Visible = fare && tasi;
        }

        private NumericUpDown SayiKutusu(int enAz, int enCok, int deger, int x, int w)
        {
            return new NumericUpDown
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 12f),
                ForeColor = Lacivert,
                BackColor = Tema.Kart,
                Minimum = enAz,
                Maximum = enCok,
                Value = deger,
                Location = new Point(x, 12),
                Width = w
            };
        }

        private Label KutuYazisi(string metin, int x)
        {
            return new Label
            {
                Text = metin,
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = SolukYazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(x, 14)
            };
        }

        private void GozGorunumu()
        {
            lblGoz.Text = gizliYeni ? "\uED1A" : "\uE7B3";
            lblGoz.ForeColor = gizliYeni ? Mavi : SolukYazi;
            txtMetin.PasswordChar = gizliYeni ? '●' : '\0';
        }

        // Kanca ya da KeyDown ile yakalanan kombinasyon
        private void Yakala(bool ctrl, bool shift, bool alt, bool win, Keys k)
        {
            if (tur != 0) return;

            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu ||
                k == Keys.LWin || k == Keys.RWin ||
                k == Keys.LControlKey || k == Keys.RControlKey ||
                k == Keys.LShiftKey || k == Keys.RShiftKey ||
                k == Keys.LMenu || k == Keys.RMenu) return;     // sadece değiştirici: bekle

            string ad = Form1.TusAdi(k);
            if (ad == null)
            {
                Durum("Bu tuş desteklenmiyor.");
                return;
            }

            List<string> p = new List<string>();
            if (ctrl) p.Add("CTRL");
            if (shift) p.Add("SHIFT");
            if (alt) p.Add("ALT");
            if (win) p.Add("WINDOWS");
            p.Add(ad);

            yakalanan = string.Join("+", p);
            txtYakala.Text = Form1.Guzel(yakalanan);
            bool tehlikeli;
            string anlam = Form1.WindowsAnlami(yakalanan, out tehlikeli);
            Durum("Yakalandı: " + Form1.Guzel(yakalanan) + ". " +
                  (anlam != null ? (tehlikeli ? "Dikkat! " : "") + "Windows'ta: " + anlam + ". " : "") +
                  "Eklemek için EKLE'ye bas.");
        }

        private void AdimEkle()
        {
            if (duzenlenen < 0 && adimlar.Count >= MaxAdim)
            {
                Durum("En fazla " + MaxAdim + " adım eklenebilir.");
                return;
            }

            string adim = null;

            switch (tur)
            {
                case 0:
                    if (yakalanan.Length == 0)
                    {
                        Durum("Önce kutuya tıklayıp kısayola bas.");
                        return;
                    }
                    adim = yakalanan;
                    yakalanan = "";
                    txtYakala.Text = "";
                    break;

                case 1:
                    string metin = txtMetin.Text;
                    if (metin.Length == 0)
                    {
                        Durum("Yazılacak metni gir.");
                        return;
                    }
                    string duzenHatasi = KlavyeDuzeni.Hata(metin);
                    if (duzenHatasi != null)
                    {
                        Durum(duzenHatasi);
                        return;
                    }
                    adim = "TEXT:" + metin;
                    if (gizliYeni) UygulamaAyarlari.GizliAyarla(metin, true);
                    txtMetin.Text = "";
                    break;

                case 2:
                    adim = "WAIT:" + (int)sayiBekle.Value;
                    break;

                case 3:
                    if (cmbKomut.SelectedIndex < 0) return;
                    adim = hazirlar[cmbKomut.SelectedIndex, 1];
                    break;

                case 4:
                    if (cmbYon.SelectedIndex == YonTasi)
                    {
                        if (sayiFareX.Value == 0 && sayiFareY.Value == 0)
                        {
                            Durum("İmlecin ne kadar taşınacağını gir (x: sağa +, sola -; y: aşağı +, yukarı -).");
                            return;
                        }
                        adim = "MOUSE_MOVE:" + (int)sayiFareX.Value + ":" + (int)sayiFareY.Value;
                        break;
                    }
                    adim = (cmbYon.SelectedIndex == 1 ? "SCROLL_UP" : "SCROLL_DOWN") +
                           (sayiCentik.Value > 1 ? ":" + (int)sayiCentik.Value : "");
                    break;

                case 6:
                    if (cmbTutTus.SelectedIndex < 0) return;
                    adim = (cmbTutYon.SelectedIndex == 1 ? "RELEASE:" : "PRESS:") + TutTuslari[cmbTutTus.SelectedIndex, 0];
                    break;

                case 5:
                    int onceki = duzenlenen >= 0 ? duzenlenen : SonSecili() + 1;
                    int k = (int)sayiTekrarAdim.Value;
                    if (onceki == 0)
                    {
                        Durum("Önce tekrarlanacak adımları ekle, sonra Tekrar'ı onların altına ekle.");
                        return;
                    }
                    if (k > onceki)
                    {
                        Durum("Üstte yalnızca " + onceki + " adım var; en fazla " + onceki + " adım tekrarlanabilir.");
                        return;
                    }
                    adim = "REPEAT:" + k + ":" + (int)sayiTekrarKez.Value;
                    break;
            }

            if (duzenlenen >= 0)
            {
                int d = duzenlenen;
                adimlar[d] = adim;
                DuzenlemeyiBitir();
                ListeyiYenile(d);
                Durum((d + 1) + ". adım güncellendi.");
                return;
            }

            // Seçili adımın arkasına ekle, seçim yoksa sona
            int konum = SonSecili() + 1;
            adimlar.Insert(konum, adim);
            ListeyiYenile(konum);
            Durum("Adım eklendi.");

            if (tur == 0) txtYakala.Focus();
            else if (tur == 1) txtMetin.Focus();
        }

        // =================================================================
        //  MAKRO KAYDI: klavyede yapılanları adım olarak ekler
        // =================================================================
        private void KayitDegistir()
        {
            if (kaydediliyor) KaydiDurdur();
            else KaydiBaslat();
        }

        private void KaydiBaslat()
        {
            if (adimlar.Count >= MaxAdim)
            {
                Durum("En fazla " + MaxAdim + " adım olabilir. Kayıt için önce adım sil.");
                return;
            }

            kaydediliyor = true;          // önce bayrak: odak kalkınca kanca durmasın
            ilkKayit = true;
            kayitMetin = -1;
            kayitBaslangic = adimlar.Count;

            ActiveControl = null;         // tuşlar hiçbir kutuya yazılmasın
            kanca.Baslat();
            fareKanca.Baslat();

            btnKayit.DolguRengi = Kirmizi;
            btnKayit.HoverRengi = KirmiziKoyu;
            btnKayit.KenarRengi = Color.Transparent;
            DugmeYazisi(btnKayit, "\uE71A", "Durdur", Color.White);

            Durum("Kayıt sürüyor: tuşlara bas, metni yaz" + (chkFare.Checked ? ", tıkla, tekerleği çevir" : "") +
                  ". Bitince Durdur'a tıkla. Bu sırada bunlar başka programa gitmez.");
        }

        private void KaydiDurdur()
        {
            if (!kaydediliyor) return;

            kaydediliyor = false;
            kayitMetin = -1;

            kanca.Durdur();
            fareKanca.Durdur();
            if (tur == 0 && txtYakala.Focused) kanca.Baslat();

            btnKayit.DolguRengi = Tema.Kart;
            btnKayit.HoverRengi = Tema.Hover;
            btnKayit.KenarRengi = Tema.Kenar2;
            DugmeYazisi(btnKayit, "\uE7C8", "Kayıt al", Lacivert);

            int eklenen = adimlar.Count - kayitBaslangic;
            Durum(eklenen > 0
                ? "Kayıt bitti: " + eklenen + " adım eklendi. Gereksiz adımları seçip silebilirsin."
                : "Kayıt bitti, adım eklenmedi.");
        }

        private void KayitTusu(bool ctrl, bool shift, bool alt, bool win, Keys k)
        {
            if (!kaydediliyor) return;

            DateTime simdi = DateTime.Now;
            int bosluk = ilkKayit ? 0 : (int)(simdi - sonKayit).TotalMilliseconds;
            ilkKayit = false;
            sonKayit = simdi;

            bool degistirici = ctrl || alt || win;
            bool metinDevam = kayitMetin >= 0 && kayitMetin < adimlar.Count;
            string harf = degistirici ? null : MetinHarfi(shift, k);

            // Yazarken Backspace: son harfi sil
            if (!degistirici && !shift && k == Keys.Back && metinDevam)
            {
                string m = adimlar[kayitMetin].Substring(5);
                if (m.Length > 0 && !m.EndsWith("}"))
                {
                    m = m.Substring(0, m.Length - 1);
                    if (m.Length == 0)
                    {
                        adimlar.RemoveAt(kayitMetin);
                        kayitMetin = -1;
                        ListeyiYenile(adimlar.Count - 1);
                    }
                    else
                    {
                        adimlar[kayitMetin] = "TEXT:" + m;
                        ListeyiYenile(kayitMetin);
                    }
                    return;
                }
            }

            // Yazının hemen ardından Enter / Tab: metne eklenir ("merhaba{ENTER}"), ayrı adım harcanmaz
            if (harf == null && !degistirici && !shift && metinDevam && (k == Keys.Enter || k == Keys.Tab))
                harf = k == Keys.Enter ? "{ENTER}" : "{TAB}";

            // Bekleme: harfler arasındaki kısa duraklamalar yazma hızıdır, sayılmaz.
            // Ama Enter / Tab'dan sonraki duraklama sayılır (açılan pencerenin yüklenmesini beklemek gerekebilir).
            bool yaziHizi = harf != null && metinDevam && bosluk < 1500 && !adimlar[kayitMetin].EndsWith("}");
            bool bekle = chkBekle.Checked && bosluk >= 400 && !yaziHizi;
            if (bekle)
            {
                int ms = Math.Min(MaxBekle, (bosluk + 25) / 50 * 50);
                if (!KayitEkle("WAIT:" + ms)) return;
                kayitMetin = -1;
                metinDevam = false;
            }

            if (harf != null)
            {
                if (metinDevam)
                {
                    adimlar[kayitMetin] += harf;
                    ListeyiYenile(kayitMetin);
                }
                else if (KayitEkle("TEXT:" + harf))
                {
                    kayitMetin = adimlar.Count - 1;
                }
                return;
            }

            string ad = Form1.TusAdi(k);
            if (ad == null)
            {
                Durum("Bu tuş kaydedilemiyor, atlandı. (Türkçe harfler ve bazı özel tuşlar desteklenmiyor.)");
                return;
            }

            List<string> p = new List<string>();
            if (ctrl) p.Add("CTRL");
            if (shift) p.Add("SHIFT");
            if (alt) p.Add("ALT");
            if (win) p.Add("WINDOWS");
            p.Add(ad);

            if (KayitEkle(string.Join("+", p))) kayitMetin = -1;
        }

        // Kayıtta fare: tıklama (centik 0) ya da tekerlek. Aynı yöne art arda çevirme tek adım olur.
        private void KayitFare(string komut, int centik)
        {
            if (!kaydediliyor) return;

            DateTime simdi = DateTime.Now;
            int bosluk = ilkKayit ? 0 : (int)(simdi - sonKayit).TotalMilliseconds;
            ilkKayit = false;
            sonKayit = simdi;
            kayitMetin = -1;

            if (centik > 0 && bosluk < 700 && adimlar.Count > kayitBaslangic)
            {
                int son = adimlar.Count - 1, onceki;
                string yon = KaydirmaCoz(adimlar[son], out onceki);
                if (yon != null && adimlar[son].StartsWith(komut))
                {
                    adimlar[son] = komut + ":" + Math.Min(50, onceki + centik);
                    ListeyiYenile(son);
                    return;
                }
            }

            if (chkBekle.Checked && bosluk >= 400)
            {
                int ms = Math.Min(MaxBekle, (bosluk + 25) / 50 * 50);
                if (!KayitEkle("WAIT:" + ms)) return;
            }

            KayitEkle(centik > 1 ? komut + ":" + centik : komut);
        }

        // Sona bir adım ekler; sınır dolduysa kaydı durdurur
        private bool KayitEkle(string adim)
        {
            if (adimlar.Count >= MaxAdim)
            {
                KaydiDurdur();
                Durum("En fazla " + MaxAdim + " adım olabilir, kayıt durdu.");
                return false;
            }

            adimlar.Add(adim);
            ListeyiYenile(adimlar.Count - 1);
            Durum("Kaydediliyor… " + (adimlar.Count - kayitBaslangic) + " adım. Bitince Durdur'a tıkla.");
            return true;
        }

        // Değiştiricisiz basılan yazı tuşu: harf, rakam, boşluk, nokta, virgül (yoksa null)
        private static string MetinHarfi(bool shift, Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z)
            {
                string h = ((char)('a' + (k - Keys.A))).ToString();
                bool buyuk = shift ^ Control.IsKeyLocked(Keys.CapsLock);
                return buyuk ? h.ToUpperInvariant() : h;
            }

            if (shift) return null;   // Shift + rakam vb. kısayol olarak kaydedilir

            if (k >= Keys.D0 && k <= Keys.D9) return ((int)k - (int)Keys.D0).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return ((int)k - (int)Keys.NumPad0).ToString();
            if (k == Keys.Space) return " ";
            if (k == Keys.OemPeriod || k == Keys.Decimal) return ".";
            if (k == Keys.Oemcomma) return ",";
            return null;
        }

        // Dugme() ile yapılmış simgeli düğmenin simgesini, yazısını ve rengini değiştirir
        private static void DugmeYazisi(YuvarlakPanel p, string ikon, string metin, Color renk)
        {
            FlowLayoutPanel akis = p.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (akis == null) return;

            List<Label> l = akis.Controls.OfType<Label>().ToList();
            if (l.Count == 2)
            {
                l[0].Text = ikon;
                l[1].Text = metin;
            }
            foreach (Label x in l) x.ForeColor = renk;
            p.Invalidate(true);
        }

        // ---------- Alt düğmeler ----------
        private void AltDugmeleriOlustur()
        {
            btnMakrolar = Dugme("\uE8F1  Makrolarım", 20, 580, 150, 42, false, MakrolarMenusu, 10f);
            Controls.Add(btnMakrolar);

            Controls.Add(Dugme("İPTAL", 236, 580, 96, 42, false, () => DialogResult = DialogResult.Cancel, 10f));
            Controls.Add(Dugme("KAYDET", 344, 580, 96, 42, true, Kaydet, 10f));
        }

        // =================================================================
        //  MAKROLARIM: makroyu adıyla sakla, başka tuşa yükle
        // =================================================================
        private void MakrolarMenusu()
        {
            KaydiDurdur();

            ContextMenuStrip m = TemaMenuRenkleri.Menu();
            List<string> adlar = UygulamaAyarlari.Makrolar();

            if (adlar.Count == 0)
                m.Items.Add(new ToolStripMenuItem("Saklanmış makro yok") { Enabled = false });

            foreach (string ad in adlar)
            {
                string isim = ad;
                ToolStripMenuItem o = TemaMenuRenkleri.Oge(isim, () => MakroYukle(isim));
                try { o.ToolTipText = Ozet(UygulamaAyarlari.MakroOku(isim), hazirlar); }
                catch (Exception) { }
                m.Items.Add(o);
            }

            m.Items.Add(new ToolStripSeparator());

            // Yüklenen makroyu yerinde güncelle (ad sormadan)
            if (yuklenenMakro != null && adlar.Contains(yuklenenMakro))
            {
                bool degisti = true;
                try { degisti = UygulamaAyarlari.MakroOku(yuklenenMakro) != Kodla(adimlar).Trim(); }
                catch (Exception) { }

                ToolStripMenuItem guncelle = TemaMenuRenkleri.Oge(
                    "\"" + yuklenenMakro + "\" makrosunu güncelle" + (degisti ? "" : " (değişiklik yok)"), MakroGuncelle);
                guncelle.Enabled = adimlar.Count > 0 && degisti;
                guncelle.Font = new Font(guncelle.Font, FontStyle.Bold);
                m.Items.Add(guncelle);
            }

            ToolStripMenuItem sakla = TemaMenuRenkleri.Oge(yuklenenMakro != null ? "Yeni adla sakla…" : "Bu makroyu sakla…", MakroSakla);
            sakla.Enabled = adimlar.Count > 0;
            m.Items.Add(sakla);

            if (adlar.Count > 0)
            {
                ToolStripMenuItem sil = TemaMenuRenkleri.Oge("Sil", null);
                sil.DropDown.Renderer = m.Renderer;
                foreach (string ad in adlar)
                {
                    string isim = ad;
                    sil.DropDownItems.Add(TemaMenuRenkleri.Oge(isim, () => MakroSil(isim)));
                }
                m.Items.Add(sil);
            }

            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(btnMakrolar, new Point(0, -m.PreferredSize.Height - 4));
        }

        private void MakroSakla()
        {
            if (adimlar.Count == 0) return;

            string ad = GirisKutusu.Sor(this, "Makroyu sakla",
                "Bu makro bu adla saklanır; başka bir tuşun makro penceresinden Makrolarım ile yükleyebilirsin.",
                "Makro adı (örn. Kilit aç)", yuklenenMakro ?? "",
                a => UygulamaAyarlari.AdTemizle(a).Length == 0 ? "Makro için bir ad yaz." : null);
            if (ad == null) return;

            ad = UygulamaAyarlari.AdTemizle(ad);

            if (System.IO.File.Exists(UygulamaAyarlari.MakroYolu(ad)) &&
                MessageBox.Show(this, "\"" + ad + "\" adında bir makro zaten var. Üzerine yazılsın mı?", "Makrolarım",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                UygulamaAyarlari.MakroYaz(ad, Kodla(adimlar));
                yuklenenMakro = ad;
                Durum("\"" + ad + "\" saklandı. Makrolarım menüsünden istediğin tuşa yükleyebilirsin.");
            }
            catch (Exception ex)
            {
                Durum("Saklanamadı: " + ex.Message);
            }
        }

        private void MakroYukle(string ad)
        {
            List<string> yeni;
            try { yeni = Coz(UygulamaAyarlari.MakroOku(ad)); }
            catch (Exception ex)
            {
                Durum("Makro okunamadı: " + ex.Message);
                return;
            }

            if (yeni.Count == 0)
            {
                Durum("\"" + ad + "\" boş görünüyor.");
                return;
            }

            if (adimlar.Count > 0 &&
                MessageBox.Show(this, "Şu anki adımlar silinip \"" + ad + "\" yüklensin mi?", "Makrolarım",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            DuzenlemeyiBitir();
            adimlar.Clear();
            adimlar.AddRange(yeni.Take(MaxAdim));
            yuklenenMakro = ad;
            ListeyiYenile(0);
            Durum("\"" + ad + "\" yüklendi. KAYDET'e basınca bu tuşa yazılır; değiştirirsen Makrolarım > Güncelle ile saklı hâlini de yenileyebilirsin.");
        }

        // Yüklenen makronun saklı hâlini şimdiki adımlarla değiştirir
        private void MakroGuncelle()
        {
            if (yuklenenMakro == null || adimlar.Count == 0) return;

            try
            {
                UygulamaAyarlari.MakroYaz(yuklenenMakro, Kodla(adimlar));
                Durum("\"" + yuklenenMakro + "\" güncellendi. Bu makroyu kullanan diğer tuşlar değişmez; onlara yeniden yüklemen gerekir.");
            }
            catch (Exception ex)
            {
                Durum("Güncellenemedi: " + ex.Message);
            }
        }

        private void MakroSil(string ad)
        {
            if (MessageBox.Show(this, "\"" + ad + "\" makrosu silinsin mi?\nTuşlara atanmış makrolar değişmez.",
                                "Makrolarım", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                UygulamaAyarlari.MakroSil(ad);
                if (ad == yuklenenMakro) yuklenenMakro = null;
                Durum("\"" + ad + "\" silindi.");
            }
            catch (Exception ex)
            {
                Durum("Silinemedi: " + ex.Message);
            }
        }

        private void Kaydet()
        {
            KaydiDurdur();

            if (adimlar.Count == 0)
            {
                Durum("En az bir adım ekle.");
                return;
            }

            Komut = Kodla(adimlar);
            DialogResult = DialogResult.OK;
        }

        private YuvarlakPanel Dugme(string yazi, int x, int y, int w, int h, bool ana, Action tik, float boyut)
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

            // "\uE70E  Yukarı" gibi metinlerde baştaki simge ayrı yazı tipiyle çizilir
            string ikon = null, metin = yazi;
            if (yazi.Length > 2 && yazi[0] >= '\uE000' && yazi[0] <= '\uF8FF')
            {
                ikon = yazi.Substring(0, 1);
                metin = yazi.Substring(1).Trim();
            }

            Color renk = ana ? Color.White : Lacivert;
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
                    Font = new Font("Segoe MDL2 Assets", boyut),
                    ForeColor = renk,
                    AutoSize = true,
                    Margin = new Padding(0, 3, 4, 0),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                });
            }
            akis.Controls.Add(new Label
            {
                Text = metin,
                Font = new Font("Segoe UI", boyut, FontStyle.Bold),
                ForeColor = renk,
                AutoSize = true,
                Margin = new Padding(0),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            });
            p.Controls.Add(akis);

            // İçeriği ortala
            Action ortala = () =>
                akis.Location = new Point((p.Width - akis.Width) / 2, (p.Height - akis.Height) / 2);
            p.Layout += (s, e) => ortala();
            akis.SizeChanged += (s, e) => ortala();

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
            return p;
        }

        private void Durum(string metin)
        {
            if (lblDurum != null) lblDurum.Text = metin;
        }

        // =================================================================
        //  KODLAMA / ÇÖZME / GÖSTERİM (ana form da kullanır)
        // =================================================================
        public static string Kodla(IEnumerable<string> adimlar)
        {
            return "MACRO:" + string.Join("|",
                adimlar.Select(a => a.Replace("\\", "\\\\").Replace("|", "\\|")));
        }

        public static List<string> Coz(string komut)
        {
            List<string> sonuc = new List<string>();
            if (string.IsNullOrEmpty(komut) || !komut.StartsWith("MACRO:")) return sonuc;

            string g = komut.Substring(6);
            StringBuilder t = new StringBuilder();

            for (int i = 0; i < g.Length; i++)
            {
                char c = g[i];

                if (c == '\\' && i + 1 < g.Length)
                {
                    t.Append(g[i + 1]);
                    i++;
                }
                else if (c == '|')
                {
                    if (t.Length > 0) sonuc.Add(t.ToString());
                    t.Clear();
                }
                else
                {
                    t.Append(c);
                }
            }

            if (t.Length > 0) sonuc.Add(t.ToString());
            return sonuc;
        }

        private static string HazirAdi(string adim, string[,] hazirlar)
        {
            for (int i = 0; i < hazirlar.GetLength(0); i++)
                if (string.Equals(hazirlar[i, 1], adim, StringComparison.OrdinalIgnoreCase))
                    return hazirlar[i, 0];
            return null;
        }

        // "REPEAT:3:5" -> son 3 adım, toplam 5 kez
        public static bool TekrarCoz(string adim, out int adet, out int kez)
        {
            adet = kez = 0;
            if (!adim.StartsWith("REPEAT:")) return false;
            string[] p = adim.Substring(7).Split(':');
            return p.Length == 2 && int.TryParse(p[0], out adet) && int.TryParse(p[1], out kez);
        }

        // "MOUSE_LEFT" -> "Sol tık", "SHIFT" -> "Shift"
        public static string TutAdi(string kod)
        {
            for (int i = 0; i < TutTuslari.GetLength(0); i++)
                if (TutTuslari[i, 0] == kod) return TutTuslari[i, 1];
            return Form1.Guzel(kod);
        }

        // "MOUSE_MOVE:100:-50" -> 100, -50 (kart 2.4+)
        public static bool FareTasiCoz(string adim, out int x, out int y)
        {
            x = y = 0;
            if (adim == null || !adim.StartsWith("MOUSE_MOVE:")) return false;
            string[] p = adim.Split(':');
            return p.Length == 3 && int.TryParse(p[1], out x) && int.TryParse(p[2], out y);
        }

        // "imleç 100 px sağa, 50 px yukarı"
        public static string FareTasiYazisi(int x, int y)
        {
            List<string> p = new List<string>();
            if (x != 0) p.Add(Math.Abs(x) + " px " + (x > 0 ? "sağa" : "sola"));
            if (y != 0) p.Add(Math.Abs(y) + " px " + (y > 0 ? "aşağı" : "yukarı"));
            return string.Join(", ", p);
        }

        // "SCROLL_DOWN:5" -> "aşağı", 5. Kaydırma değilse null. ("SCROLL_DOWN" tek çentik; hazır komut olarak da görünür)
        public static string KaydirmaCoz(string adim, out int centik)
        {
            centik = 0;
            int iki = adim.IndexOf(':');
            if (iki < 0) return null;

            string ad = adim.Substring(0, iki);
            if (ad != "SCROLL_UP" && ad != "SCROLL_DOWN") return null;
            if (!int.TryParse(adim.Substring(iki + 1), out centik)) return null;
            return ad == "SCROLL_UP" ? "yukarı" : "aşağı";
        }

        // Listede görünen uzun yazı: "Kısayol: Ctrl + C", "Bekle: 200 ms" ...
        public static string AdimYazisi(string adim, string[,] hazirlar)
        {
            if (adim.StartsWith("TEXT:"))
                return "Metin: " + UygulamaAyarlari.GizliyseMaskele(adim.Substring(5), adim.Substring(5));
            if (adim.StartsWith("WAIT:")) return "Bekle: " + adim.Substring(5) + " ms";
            if (adim.StartsWith(NotOneki)) return "Not: " + adim.Substring(NotOneki.Length);
            if (adim.StartsWith("PRESS:")) return "Basılı tut: " + TutAdi(adim.Substring(6));
            if (adim.StartsWith("RELEASE:")) return "Bırak: " + TutAdi(adim.Substring(8));

            int k, n;
            if (TekrarCoz(adim, out k, out n))
                return "Tekrarla: üstteki " + (k == 1 ? "adımı" : k + " adımı") + " toplam " + n + " kez";

            int centik;
            string yon = KaydirmaCoz(adim, out centik);
            if (yon != null) return "Kaydır: " + yon + " " + centik + " çentik";

            int fx, fy;
            if (FareTasiCoz(adim, out fx, out fy)) return "İmleci taşı: " + FareTasiYazisi(fx, fy);

            string ad = HazirAdi(adim, hazirlar);
            return ad != null ? "Komut: " + ad : "Kısayol: " + Form1.Guzel(adim);
        }

        // Kutuda / kartta görünen kısa özet: Ctrl + C → 200 ms → Alt + Tab
        public static string Ozet(string komut, string[,] hazirlar)
        {
            List<string> a = Coz(komut).Where(x => !x.StartsWith(NotOneki)).ToList();   // notlar özette görünmez
            if (a.Count == 0) return "";

            return string.Join("  →  ", a.Select(x =>
            {
                if (x.StartsWith("TEXT:")) return UygulamaAyarlari.GizliyseMaskele(x.Substring(5), "\"" + x.Substring(5) + "\"");
                if (x.StartsWith("WAIT:")) return x.Substring(5) + " ms";
                if (x.StartsWith("PRESS:")) return "tut " + TutAdi(x.Substring(6));
                if (x.StartsWith("RELEASE:")) return "bırak " + TutAdi(x.Substring(8));

                int k, n, centik;
                if (TekrarCoz(x, out k, out n)) return "↻ " + k + " adım × " + n;
                string yon = KaydirmaCoz(x, out centik);
                if (yon != null) return yon + " kaydır ×" + centik;
                int fx, fy;
                if (FareTasiCoz(x, out fx, out fy)) return "imleç " + FareTasiYazisi(fx, fy);

                return HazirAdi(x, hazirlar) ?? Form1.Guzel(x);
            }));
        }
    }
}