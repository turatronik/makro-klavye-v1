using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Form1'in parçası: sayfa sekmeleri (Tuşlar / Işık / Ayarlar, 16 tuşlu uygulamadaki gibi),
    // pencereyi büyütme / kenardan boyutlandırma, Işık sayfası (canlı klavye demosu + LED ayarları)
    // ve ana pencerenin içindeki Ayarlar sayfası.
    public partial class Form1
    {
        private enum Sayfa { Tuslar, Isik, Ayarlar }

        private Sayfa sayfa = Sayfa.Tuslar;
        private SayfaPaneli tuslarSayfasi, isikSayfasi, ayarSayfasi;
        private readonly YuvarlakPanel[] sekmeler = new YuvarlakPanel[3];
        private readonly Label[] sekmeSimgeleri = new Label[3], sekmeYazilari = new Label[3];

        // Pencere büyüdükçe: sol (profiller) sola, sağ (tuş ayarları, bağlan) sağa yapışık, orta (klavye) büyür
        private readonly List<Control> pencereSolu = new List<Control>();
        private readonly List<Control> pencereSagi = new List<Control>();
        private readonly List<Control> pencereDolu = new List<Control>();

        private static readonly Size EnKucukBoyut = new Size(1336, 720);
        private Label btnBuyut;
        private bool tamEkran;
        private Rectangle normalSinir;
        private bool rehberBekliyor;

        // =================================================================
        //  BAŞLIKTAKİ SEKMELER
        // =================================================================
        private void SekmeleriOlustur(Panel baslik)
        {
            string[] adlar = { "Tuşlar", "Işık", "Ayarlar" };
            string[] simgeler = { "", "", "" };
            string[] ipuclari = { "Tuş atamaları (Ctrl+1)", "LED efekti, renk, parlaklık; canlı önizleme (Ctrl+2)",
                                  "Profiller, klavye ayarları, güncelleme, geçmiş (Ctrl+3)" };
            int x = 200;

            for (int i = 0; i < adlar.Length; i++)
            {
                Sayfa hedef = (Sayfa)i;
                int w = TextRenderer.MeasureText(adlar[i], new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width + 48;

                YuvarlakPanel p = new YuvarlakPanel
                {
                    Location = new Point(x, 6),
                    Size = new Size(w, 28),
                    Yaricap = 14
                };
                sekmeSimgeleri[i] = Etiket(p, simgeler[i], Ikon(9.5f), Color.White, 10, 0, 20, 28, ContentAlignment.MiddleCenter);
                sekmeYazilari[i] = Etiket(p, adlar[i], new Font("Segoe UI", 9.5f, FontStyle.Bold), Color.White,
                                          32, 0, w - 36, 28, ContentAlignment.MiddleLeft);
                HoverBagla(p);
                TiklaBagla(p, (s, e) => SayfaAc(hedef));
                BaloncukAyarla(p, ipuclari[i]);

                sekmeler[i] = p;
                baslik.Controls.Add(p);
                x += w + 6;
            }

            btnLed = sekmeler[1];              // tanıtımda "LED" adımı
            lblAyarlarDugme = sekmeler[2];     // tanıtımda "Ayarlar" adımı
            SekmeStilleri();
        }

        private void SekmeStilleri()
        {
            for (int i = 0; i < sekmeler.Length; i++)
            {
                if (sekmeler[i] == null) continue;
                bool aktif = (int)sayfa == i;
                sekmeler[i].DolguRengi = aktif ? Mavi : Color.Transparent;
                sekmeler[i].HoverRengi = aktif ? Color.Empty : Color.FromArgb(40, 255, 255, 255);
                sekmeYazilari[i].ForeColor = sekmeSimgeleri[i].ForeColor = aktif ? Color.White : Color.FromArgb(185, 198, 218);
                sekmeler[i].Invalidate(true);
            }
        }

        // =================================================================
        //  SAYFALAR
        // =================================================================
        private SayfaPaneli SayfaPaneliOlustur(Rectangle alan, bool gorunur)
        {
            SayfaPaneli p = new SayfaPaneli
            {
                Bounds = alan,
                Visible = gorunur
            };
            p.MouseDown += SurukleBaslat;
            Controls.Add(p);
            p.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            return p;
        }

        // Ana arayüz Tuşlar sayfasına taşınır; Işık ve Ayarlar sayfaları açıldıkça doldurulur.
        // Sayfalar pencere kenarından 6 piksel içeride: kenardan tutup boyutlandırılabilsin.
        private void SayfalariOlustur()
        {
            Rectangle alan = new Rectangle(6, baslikCubugu.Height, ClientSize.Width - 12, ClientSize.Height - baslikCubugu.Height - 6);

            List<Control> eskiler = Controls.Cast<Control>()
                .Where(c => c != baslikCubugu && c != panel1 && c != pictureBox1).ToList();

            tuslarSayfasi = SayfaPaneliOlustur(alan, true);
            isikSayfasi = SayfaPaneliOlustur(alan, false);
            ayarSayfasi = SayfaPaneliOlustur(alan, false);

            tuslarSayfasi.SuspendLayout();
            foreach (Control c in eskiler)
            {
                c.Location = new Point(c.Left - alan.X, c.Top - alan.Y);
                tuslarSayfasi.Controls.Add(c);
            }
            tuslarSayfasi.ResumeLayout(false);

            foreach (Control c in pencereSolu) c.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            foreach (Control c in pencereDolu) c.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            foreach (Control c in pencereSagi)
                c.Anchor = AnchorStyles.Top | AnchorStyles.Right | (c.Height > 200 ? AnchorStyles.Bottom : AnchorStyles.None);

            // Işık sayfası: solda canlı klavye demosu, sağda LED ayarları
            isikKart = new YuvarlakPanel
            {
                Yaricap = 20,
                DolguRengi = Tema.Panel,
                KenarRengi = Tema.PanelKenar,
                Size = new Size(500, 500)
            };
            Etiket(isikKart, "", Ikon(13f), Lacivert, 22, 22);
            Etiket(isikKart, "CANLI ÖNİZLEME", new Font("Segoe UI", 10.5f, FontStyle.Bold), Lacivert, 48, 20);
            lblIsikAlt = Etiket(isikKart, "Klavyenin ışığı seçtiğin efektle burada oynar.", new Font("Segoe UI", 8.5f),
                                SolukYazi, 48, 42, 440, 18);
            lblIsikAlt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblIsikBilgi = Etiket(isikKart, "", new Font("Segoe UI", 9f, FontStyle.Bold), SolukYazi, 22, 500 - 56, 456, 40,
                                  ContentAlignment.MiddleLeft);
            lblIsikBilgi.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Tahmini LED akımı + sınır (USB'yi zorlamasın; kart 2.8+ sınırı aşınca parlaklığı kısar)
            lblAkim = Etiket(isikKart, "", new Font("Segoe UI", 9f), SolukYazi, 22, 500 - 100, 300, 20, ContentAlignment.MiddleLeft);
            lblAkim.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lblAkimSinir = Etiket(isikKart, "", new Font("Segoe UI", 9f, FontStyle.Bold), Mavi, 500 - 22 - 160, 500 - 100, 160, 20,
                                  ContentAlignment.MiddleRight);
            lblAkimSinir.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            lblAkimSinir.Cursor = Cursors.Hand;
            lblAkimSinir.Click += (s, e) => AkimSiniriMenusu();
            baloncuk.SetToolTip(lblAkimSinir, "LED'lerin çekebileceği en fazla akım. Aşılınca kart parlaklığı kendisi kısar " +
                                              "(bilgisayarın USB girişi genelde 500 mA verir). Kart yazılımı 2.8 gerekir.");
            akimCubugu = new AkimCubugu { Location = new Point(22, 500 - 76), Size = new Size(456, 8) };
            akimCubugu.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            isikKart.Controls.Add(akimCubugu);

            isikSayfasi.Controls.Add(isikKart);
            isikSayfasi.Resize += (s, e) => IsikYerlestir();

            ayarSayfasi.Resize += (s, e) => AyarYerlestir();
        }

        private void SayfaAc(Sayfa s)
        {
            if (tuslarSayfasi == null || s == sayfa) return;
            if (RehberAcik) RehberBitir();

            if (sayfa == Sayfa.Isik && !IsiktanCik()) return;
            if (sayfa == Sayfa.Ayarlar && !AyarlardanCik()) return;

            ActiveControl = null;   // kısayol yakalanıyorsa kapansın
            sayfa = s;

            if (s == Sayfa.Isik) IsikSayfasiniKur();
            else if (s == Sayfa.Ayarlar) AyarSayfasiniKur();

            tuslarSayfasi.Visible = s == Sayfa.Tuslar;
            isikSayfasi.Visible = s == Sayfa.Isik;
            ayarSayfasi.Visible = s == Sayfa.Ayarlar;
            SekmeStilleri();

            if (s == Sayfa.Tuslar && rehberBekliyor)
            {
                rehberBekliyor = false;
                BeginInvoke(new Action(RehberBaslat));
            }
        }

        // Klavye çizimi ve altındaki "bütün basışlar" listesi kartın ortasında dursun
        private void KlavyeyiOrtala()
        {
            if (govde == null || atamaKarti == null) return;
            int dx = (atamaKarti.Width - govde.Width) / 2 - govde.Left;
            if (dx == 0) return;

            govde.Left += dx;
            if (lblOzetBaslik != null) lblOzetBaslik.Left += dx;
            foreach (YuvarlakPanel satir in ozetSatirlari)
                if (satir != null) satir.Left += dx;
        }

        // =================================================================
        //  PENCERE: BÜYÜT / KENARDAN BOYUTLANDIR
        // =================================================================
        private void TamEkranDegistir()
        {
            if (WindowState != FormWindowState.Normal) return;

            if (!tamEkran)
            {
                normalSinir = Bounds;
                tamEkran = true;
                Bounds = Screen.FromControl(this).WorkingArea;   // görev çubuğu görünür kalır
            }
            else
            {
                tamEkran = false;
                Bounds = normalSinir;
            }

            if (btnBuyut != null) btnBuyut.Text = tamEkran ? "" : "";

            if (win11 && IsHandleCreated)
            {
                int tercih = tamEkran ? 1 : DWMWCP_ROUND;   // 1: köşe yuvarlama yok
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref tercih, sizeof(int));
            }
            OnResize(EventArgs.Empty);
            Invalidate();
        }

        // Fare pencere kenarındaysa Windows'a "buradan boyutlandır" de
        private int KenarBul(IntPtr lParam)
        {
            long lp = lParam.ToInt64();
            Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            const int k = 8;

            bool sol = p.X < k, sag = p.X >= ClientSize.Width - k;
            bool ust = p.Y < 5, alt = p.Y >= ClientSize.Height - k;

            if (ust && sol) return 13;     // HTTOPLEFT
            if (ust && sag) return 14;     // HTTOPRIGHT
            if (alt && sol) return 16;     // HTBOTTOMLEFT
            if (alt && sag) return 17;     // HTBOTTOMRIGHT
            if (sol) return 10;            // HTLEFT
            if (sag) return 11;            // HTRIGHT
            if (ust) return 12;            // HTTOP
            if (alt) return 15;            // HTBOTTOM
            return HTCLIENT;
        }

        // =================================================================
        //  AYARLAR SAYFASI (ayarlar penceresi ana pencerenin içinde)
        // =================================================================
        private AyarlarForm ayarFormu;

        private void AyarSayfasiniKur()
        {
            if (ayarFormu != null) return;

            ayarFormu = new AyarlarForm(this, true);
            ayarFormu.GomuluYap();
            ayarFormu.KapatIstendi += () => BeginInvoke(new Action(() => SayfaAc(Sayfa.Tuslar)));
            ayarSayfasi.Controls.Add(ayarFormu);
            AyarYerlestir();
            ayarFormu.Show();
        }

        private void AyarYerlestir()
        {
            if (ayarFormu == null) return;
            // Ayarlar sayfanın tamamını kaplar; içindeki bölümler genişliğe göre iki sütuna yayılır
            ayarFormu.Bounds = new Rectangle(14, 10, Math.Max(600, ayarSayfasi.ClientSize.Width - 28),
                                             Math.Max(600, ayarSayfasi.ClientSize.Height - 20));
        }

        private bool AyarlardanCik()
        {
            if (ayarFormu == null) return true;

            if (ayarFormu.Islemde)
            {
                MessageBox.Show(this, "Ayarlar'da bir işlem sürüyor (ör. kart güncelleniyor). Bitince geç.",
                                "Makro Klavye", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            rehberBekliyor = ayarFormu.RehberIstendi;
            ayarFormu.Dispose();
            ayarFormu = null;

            TepsiGuncelle();
            ProfilEtiketiGuncelle();
            KutucukGuncelle(atamalar.Count - 1);   // encoder kartı (hazır profil yüklendiyse)
            return true;
        }

        // =================================================================
        //  IŞIK SAYFASI
        //  Her değişiklik klavyede anında denenir (kaydedilmeden). KAYDET klavyeye yazar,
        //  GERİ AL ya da kaydetmeden çıkmak eski ışığa döner.
        // =================================================================
        private YuvarlakPanel isikKart;
        private Label lblIsikBilgi, lblIsikAlt, lblAkim, lblAkimSinir;
        private AkimCubugu akimCubugu;
        private Timer akimZaman;
        private int akimSiniri = 400, eskiAkimSiniri = 400, ilkAkimSiniri = 400;
        private LedAyarForm ledFormu;
        private KlavyeDemosu ledDemo;

        // Klavyede kayıtlı (karttaki) değerler: geri almak için
        private string eskiLedMod, eskiLedRenk, eskiLedAkis;
        private int eskiLedPar, eskiLedHiz;

        // Formun açılıştaki hâli: değişiklik var mı?
        private string ilkMod, ilkRenk, ilkAkis;
        private int ilkPar, ilkHiz;

        private void KayitliLedOku(out string mod, out string renk, out string akis, out int par, out int hiz)
        {
            mod = Al("LED_MODE", "rainbow");
            renk = Al("LED_COLOR", "0066FF");
            akis = Al("LED_FLOW", LedAyarForm.VarsayilanAkis);
            if (!int.TryParse(Al("LED_BRIGHTNESS", "100"), out par)) par = 100;
            if (!int.TryParse(Al("LED_SPEED", "100"), out hiz)) hiz = 100;
        }

        private void IsikSayfasiniKur()
        {
            IsikFormunuKaldir();

            KayitliLedOku(out eskiLedMod, out eskiLedRenk, out eskiLedAkis, out eskiLedPar, out eskiLedHiz);

            if (!int.TryParse(Al("LED_AKIM_SINIR", "400"), out akimSiniri) || akimSiniri < 0) akimSiniri = 400;
            eskiAkimSiniri = ilkAkimSiniri = akimSiniri;

            ledDemo = new KlavyeDemosu { AkimSiniri = akimSiniri };
            for (int i = 0; i < kutucuklar.Count && i < ledDemo.Adlar.Length; i++)
                ledDemo.Adlar[i] = kutucuklar[i].Ad.Text;
            isikKart.Controls.Add(ledDemo);

            akimZaman = new Timer { Interval = 200 };
            akimZaman.Tick += (s, e) => AkimGuncelle();
            akimZaman.Start();

            ledFormu = new LedAyarForm(eskiLedMod, eskiLedRenk, eskiLedPar, eskiLedHiz, eskiLedAkis, true);
            ilkMod = ledFormu.Mod;
            ilkRenk = ledFormu.RenkHex;
            ilkAkis = ledFormu.AkisRenkleri;
            ilkPar = ledFormu.Parlaklik;
            ilkHiz = ledFormu.Hiz;

            ledFormu.Degisti += () =>
            {
                ledMod = ledFormu.Mod;
                ledRenk = ledFormu.RenkHex;
                ledParlaklik = ledFormu.Parlaklik;
                ledHiz = ledFormu.Hiz;
                ledAkis = ledFormu.AkisRenkleri;
                if (klavyeBagli) LedGonder();
                else GovdeIsigiGuncelle();
                IsikBilgisi();
            };
            ledFormu.Bitti += kaydet =>
            {
                if (kaydet) LedKaydet();
                else BeginInvoke(new Action(LedGeriAl));   // form kendi düğmesinin içindeyken yeniden kurulmasın
            };

            isikSayfasi.Controls.Add(ledFormu);
            ledFormu.Onizleme = ledDemo;
            IsikYerlestir();
            ledFormu.Show();
            IsikBilgisi();
            AkimGuncelle();
        }

        private void IsikYerlestir()
        {
            if (isikKart == null) return;

            int w = isikSayfasi.ClientSize.Width, h = isikSayfasi.ClientSize.Height;
            int formG = 768, formY = 500;
            int y = Math.Max(14, (h - formY) / 2);
            int sagX = w - 14 - formG;

            if (ledFormu != null) ledFormu.Location = new Point(sagX, y);
            isikKart.SetBounds(14, y, Math.Max(300, sagX - 16 - 14), formY);
            if (ledDemo != null) ledDemo.SetBounds(16, 70, isikKart.Width - 32, isikKart.Height - 70 - 108);
        }

        // ---------- LED akımı ----------
        private void AkimGuncelle()
        {
            if (ledDemo == null || lblAkim == null) return;

            int ham = ledDemo.HamAkim, akim = ledDemo.Akim;
            lblAkim.Text = "Tahmini LED akımı: " + akim + " mA" + (ham > akim + 2 ? "  (sınır yüzünden kısıldı)" : "");
            lblAkim.ForeColor = ham > akim + 2 ? Tema.HataYazi : SolukYazi;
            lblAkimSinir.Text = "Sınır: " + (akimSiniri > 0 ? akimSiniri + " mA" : "yok") + "  ▾";
            akimCubugu.Ayarla(akim, akimSiniri > 0 ? akimSiniri : 800);
        }

        private void AkimSiniriMenusu()
        {
            ContextMenuStrip m = TemaMenusu(true);
            foreach (int deger in new[] { 300, 400, 500, 0 })
            {
                int d = deger;
                ToolStripMenuItem o = MenuOgesi(null, d > 0 ? d + " mA" + (d == 400 ? " (önerilen)" : "") : "Sınır yok", () =>
                {
                    akimSiniri = d;
                    if (ledDemo != null) ledDemo.AkimSiniri = d;
                    AkimGuncelle();
                    IsikBilgisi();
                    if (d == 0 || d > 500)
                        Bildir("Sınır yokken tam beyaz ışık 750 mA'ya kadar çekebilir; bazı USB girişleri bunu kaldıramaz.");
                    else
                        Bildir("Kaydedince klavyede geçerli olur (kart yazılımı 2.8 gerekir).");
                });
                o.Checked = d == akimSiniri;
                m.Items.Add(o);
            }
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(lblAkimSinir, new Point(0, lblAkimSinir.Height));
        }

        private bool LedDegisti()
        {
            if (ledFormu == null) return false;
            return ledFormu.Mod != ilkMod || ledFormu.Parlaklik != ilkPar || ledFormu.Hiz != ilkHiz ||
                   !string.Equals(ledFormu.RenkHex, ilkRenk, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(ledFormu.AkisRenkleri, ilkAkis, StringComparison.OrdinalIgnoreCase) ||
                   akimSiniri != ilkAkimSiniri;
        }

        private void IsikBilgisi()
        {
            if (lblIsikBilgi == null || ledFormu == null) return;

            if (LedDegisti())
            {
                lblIsikBilgi.Text = klavyeBagli
                    ? "●  Kaydedilmedi: klavyede deneniyor. KAYDET'e bas ya da GERİ AL."
                    : "●  Kaydedilmedi. Kaydetmek için klavyeye bağlan.";
                lblIsikBilgi.ForeColor = Tema.HataYazi;
            }
            else
            {
                lblIsikBilgi.Text = klavyeBagli
                    ? "Değiştirdiğin her şey klavyede de anında görünür."
                    : "Klavye bağlı değil: önizleme yalnızca burada.";
                lblIsikBilgi.ForeColor = SolukYazi;
            }
        }

        // Karttaki LED ayarı dışarıdan değişti (bağlanınca okundu, profil yüklendi, LED tuşu):
        // Işık sayfası açık ve değişiklik yoksa yeni değerlerle kurulur
        private void IsikDisaridanDegisti()
        {
            if (sayfa != Sayfa.Isik || ledFormu == null || LedDegisti()) return;

            string m, r, a;
            int p, h;
            KayitliLedOku(out m, out r, out a, out p, out h);
            if (m == eskiLedMod && r == eskiLedRenk && a == eskiLedAkis && p == eskiLedPar && h == eskiLedHiz) return;

            BeginInvoke(new Action(() => { if (sayfa == Sayfa.Isik) IsikSayfasiniKur(); }));
        }

        private async void LedKaydet()
        {
            if (ledFormu == null) return;
            if (!klavyeBagli)
            {
                Bildir("Kaydetmek için önce klavyeye bağlan.");
                IsikBilgisi();
                return;
            }

            // Değerler beklemeden önce alınır (sayfadan çıkılırsa form kapanır)
            string yeniMod = ledFormu.Mod, yeniRenk = ledFormu.RenkHex, yeniAkis = ledFormu.AkisRenkleri;
            int yeniPar = ledFormu.Parlaklik, yeniHiz = ledFormu.Hiz;

            GeriAlNoktasi("LED ayarı");
            kartAyarlari["LED_MODE"] = yeniMod;
            kartAyarlari["LED_COLOR"] = yeniRenk;
            kartAyarlari["LED_BRIGHTNESS"] = yeniPar.ToString();
            kartAyarlari["LED_SPEED"] = yeniHiz.ToString();
            kartAyarlari["LED_FLOW"] = yeniAkis;
            kartAyarlari["LED_AKIM_SINIR"] = akimSiniri.ToString();
            eskiAkimSiniri = ilkAkimSiniri = akimSiniri;
            ledMod = yeniMod;
            ledRenk = yeniRenk;
            ledParlaklik = yeniPar;
            ledHiz = yeniHiz;
            ledAkis = yeniAkis;

            // Kaydedilen hâl artık "değişiklik yok" sayılır
            eskiLedMod = ilkMod = yeniMod;
            eskiLedRenk = ilkRenk = yeniRenk;
            eskiLedAkis = ilkAkis = yeniAkis;
            eskiLedPar = ilkPar = yeniPar;
            eskiLedHiz = ilkHiz = yeniHiz;

            Bildir("LED ayarı kaydediliyor...");
            string hata = await KartaYaz();

            if (hata == null)
            {
                OtomatikYedek("LED ayarı", LedAyarForm.ModAdi(yeniMod) + ", parlaklık %" + yeniPar + ", hız %" + yeniHiz);
                AktifProfileLedYaz();
                GovdeIsigiGuncelle();
                Bildir("LED ayarı kaydedildi.");
                IsikBilgisi();
            }
            else
            {
                GeriAlIptal();
                HataGoster(hata);
            }
        }

        // Kaydedilmemiş ışık değişikliğini bırak: klavye kayıtlı ışığa döner
        private void LedIptal()
        {
            ledMod = eskiLedMod;
            ledRenk = eskiLedRenk;
            ledParlaklik = eskiLedPar;
            ledHiz = eskiLedHiz;
            ledAkis = eskiLedAkis;
            akimSiniri = eskiAkimSiniri;
            if (klavyeBagli) LedGonder();
            else GovdeIsigiGuncelle();
        }

        private void LedGeriAl()
        {
            if (sayfa != Sayfa.Isik) return;
            LedIptal();
            IsikSayfasiniKur();
            Bildir("LED ayarı geri alındı.");
        }

        private bool IsiktanCik()
        {
            if (LedDegisti())
            {
                DialogResult c = MessageBox.Show(this, "Işıkta kaydedilmemiş değişiklik var. Klavyeye kaydedilsin mi?\n\n" +
                                                 "Hayır: klavye eski ışığına döner.", "Işık",
                                                 MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (c == DialogResult.Cancel) return false;
                if (c == DialogResult.Yes && klavyeBagli) LedKaydet();
                else LedIptal();
            }
            IsikFormunuKaldir();
            return true;
        }

        private void IsikFormunuKaldir()
        {
            if (akimZaman != null)
            {
                akimZaman.Stop();
                akimZaman.Dispose();
                akimZaman = null;
            }
            if (ledFormu != null)
            {
                ledFormu.Dispose();
                ledFormu = null;
            }
            if (ledDemo != null)
            {
                ledDemo.Dispose();
                ledDemo = null;
            }
        }
    }

    // Işık sayfasındaki akım çubuğu: yeşil -> turuncu -> kırmızı (sınıra yaklaştıkça)
    internal class AkimCubugu : Control
    {
        private int akim, sinir = 400;

        public AkimCubugu()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void Ayarla(int yeniAkim, int yeniSinir)
        {
            if (yeniAkim == akim && yeniSinir == sinir) return;
            akim = yeniAkim;
            sinir = Math.Max(1, yeniSinir);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            float yaricap = Math.Max(1, Height / 2f - 0.5f);
            using (var yol = YuvarlakPanel.Yol(r, yaricap))
            using (var b = new SolidBrush(Tema.Kenar))
                g.FillPath(b, yol);

            float oran = Math.Min(1f, akim / (float)sinir);
            if (oran <= 0.01f) return;
            Color c = oran < 0.7f ? Color.FromArgb(52, 199, 120) : oran < 0.95f ? Color.FromArgb(245, 166, 35) : Color.FromArgb(235, 77, 75);
            using (var yol = YuvarlakPanel.Yol(new RectangleF(0, 0, Math.Max(Height, (Width - 1) * oran), Height - 1), yaricap))
            using (var b = new SolidBrush(c))
                g.FillPath(b, yol);
        }
    }

    // Ana penceredeki bir sayfa: zemini pencerenin gradyanının kendine düşen parçası
    internal class SayfaPaneli : Panel
    {
        public SayfaPaneli()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Tema.ZeminBoya(e.Graphics, this);
        }
    }
}
