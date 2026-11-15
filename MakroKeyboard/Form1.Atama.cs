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
    // Form1'in parçası: Tuş ataması: seçim, basış türü, makro, tuş adı, encoder modları, kısayol kutusu, metin
    public partial class Form1
    {
        // =================================================================
        //  SEÇİM / GÖRÜNÜM
        // =================================================================
        private void SecimYap(int i)
        {
            if (i != secili) basisTuru = 0;      // başka tuşa geçince kısa basışla başla
            secili = i;
            var a = atamalar[i];

            KutucukStilleri();

            lblTusAdi.Text = (i < atamalar.Count - 1) ? "BUTON " + (i + 1) : "ENCODER";
            OzetGuncelle();
            lblEncoderModu.Visible = EncoderSecili;
            lblEncoderModu.Text = "Çevirme: " + EncoderModAdi() + "  ▾";
            SeciliTusAltYazisi();
            BasisGorunum();

            string komut = AktifKomut();
            bekleyenExe = null;
            metinHam = "";
            metinGizli = komut.StartsWith("TEXT:") && UygulamaAyarlari.GizliMi(komut.Substring(5));

            if (komut.StartsWith("TEXT:"))
            {
                metinHam = MetinCoz(komut.Substring(5));
                komutHam = "";
                GosterMetin();
            }
            else if (komut.StartsWith("MACRO:"))
            {
                komutHam = komut;
                GosterMakro();
            }
            else if (basisTuru == 0 && uygulamalar.ContainsKey(HotkeyNorm(komut)))
            {
                komutHam = komut;
                GosterUygulama();
            }
            else
            {
                komutHam = komut;
                GosterKisayol();
            }
        }

        // =================================================================
        //  BASIŞ TÜRÜ (kısa / uzun / çift — encoder'da basma / sağa / sola)
        // =================================================================
        private bool EncoderSecili
        {
            get { return secili == atamalar.Count - 1; }
        }

        // Seçili tuş + basış türünün settings.txt anahtarı
        private string Anahtar()
        {
            // Encoder'da 3 = sağa, 4 = sola çevirme; 0-2 diğer tuşlarla aynı (basma, uzun, çift)
            if (basisTuru == 3) return "ENCODER_CW";
            if (basisTuru == 4) return "ENCODER_CCW";
            // 5 / 6: bu tuş (encoder'da: encoder'a) basılıyken encoder sağa / sola (kart 2.8+)
            if (basisTuru == 5) return "BUTTON" + secili + "_CW";
            if (basisTuru == 6) return "BUTTON" + secili + "_CCW";

            return "BUTTON" + secili + (basisTuru == 1 ? "_LONG" : basisTuru == 2 ? "_DOUBLE" : "");
        }

        private string AktifKomut()
        {
            if (basisTuru == 0) return atamalar[secili].Kisayol;

            string varsayilan = basisTuru == 3 ? "VOLUME_UP" : basisTuru == 4 ? "VOLUME_DOWN" : "";
            return Al(Anahtar(), varsayilan);
        }

        private string BasisAdi()
        {
            string[] adlar = EncoderSecili
                ? new[] { "Basma", "Uzun basma", "Çift basma", "Sağa çevirme", "Sola çevirme", "Basılı + sağa", "Basılı + sola" }
                : new[] { "Kısa basış", "Uzun basış", "Çift basış", "", "", "Tut + sağa çevir", "Tut + sola çevir" };
            return adlar[Math.Min(basisTuru, adlar.Length - 1)];
        }

        private static readonly Font BasisFontu = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font BasisFontuDar = new Font("Segoe UI", 9f, FontStyle.Bold);

        private int BasisSayisi
        {
            get { return EncoderSecili ? 7 : 3; }
        }

        // Seçili tuşta seçilebilen basış türleri (sırasıyla; ok tuşları bunlar arasında gezer)
        private int[] GecerliBasislar
        {
            get { return EncoderSecili ? new[] { 0, 1, 2, 3, 4, 5, 6 } : new[] { 0, 1, 2, 5, 6 }; }
        }

        private void BasisKaydir(int yon)
        {
            int[] l = GecerliBasislar;
            int i = Array.IndexOf(l, basisTuru);
            if (i < 0) i = 0;
            KullaniciBasisSec(l[(i + yon + l.Length) % l.Length]);
        }

        private void BasisOlustur(Control ebeveyn)
        {
            for (int i = 0; i < basisPaneller.Length; i++)
            {
                int indeks = i;
                var p = new YuvarlakPanel
                {
                    Location = new Point(30 + i * 115, 204),
                    Size = new Size(105, 30),
                    Yaricap = 15
                };
                basisYazilar[i] = Etiket(p, "", new Font("Segoe UI", 9.5f, FontStyle.Bold), Lacivert,
                                         0, 0, 105, 30, ContentAlignment.MiddleCenter);
                basisYazilar[i].Dock = DockStyle.Fill;
                HoverBagla(p);
                TiklaBagla(p, (s, e) => KullaniciBasisSec(indeks));
                basisPaneller[i] = p;
                ebeveyn.Controls.Add(p);
            }
        }

        private void BasisGorunum()
        {
            bool enc = EncoderSecili;
            string[] adlar = enc
                ? new[] { "Bas", "Uzun", "Çift", "Sağa", "Sola" }
                : new[] { "Kısa", "Uzun basış", "Çift basış" };

            // Tuşlarda 3 geniş sekme, encoder'da 5 dar sekme (aynı 335 px'lik alanda)
            int adet = adlar.Length, ara = enc ? 6 : 10;
            int gen = (335 - ara * (adet - 1)) / adet;

            for (int i = 0; i < basisPaneller.Length; i++)
            {
                bool gorunur = i < adet;
                basisPaneller[i].Visible = gorunur;
                if (!gorunur) continue;

                basisPaneller[i].SetBounds(30 + i * (gen + ara), 204, gen, 30);
                basisYazilar[i].Font = enc ? BasisFontuDar : BasisFontu;

                bool dolu = (i == 1 || i == 2) &&
                            Al("BUTTON" + secili + (i == 1 ? "_LONG" : "_DOUBLE"), "").Length > 0;

                basisYazilar[i].Text = adlar[i] + (dolu ? " •" : "");
                SekmeStil(basisPaneller[i], basisYazilar[i], i == basisTuru);
            }
        }

        private void BasisSec(int b)
        {
            basisTuru = b;
            SecimYap(secili);

            if (EncoderSecili && b == 0) Bildir("Encoder'a basınca çalışacak komut.");
            else if (b == 3) Bildir("Encoder sağa çevrilince çalışır (örn. Ses Artır, Kaydır Aşağı).");
            else if (b == 4) Bildir("Encoder sola çevrilince çalışır (örn. Ses Azalt, Kaydır Yukarı).");
            else if (b == 5 || b == 6)
            {
                Bildir((EncoderSecili ? "Encoder'a basılı tutup " : "Bu tuşu basılı tutup encoder'ı ") +
                       (b == 5 ? "sağa" : "sola") + " çevirince çalışır (örn. yakınlaştır, fırça boyutu). " +
                       "Tanımlıysa tuşun kendi komutu bırakınca çalışır; çevirdiysen hiç çalışmaz. Kart yazılımı 2.8 gerekir.");
            }
            else if (b == 1)
            {
                Bildir((EncoderSecili ? "Encoder'ı " : "Tuşu ") + SaniyeYazisi("LONG_PRESS_MS", 600) +
                       " basılı tutunca çalışır. Kaldırmak için listeden Boş'u seçip kaydet.");
            }
            else if (b == 2)
            {
                Bildir(SaniyeYazisi("DOUBLE_PRESS_MS", 300) + " içinde iki kez basınca çalışır. " +
                       "Çift basışı olan " + (EncoderSecili ? "encoder'da basma" : "tuşta kısa basış") + " biraz gecikir.");
            }
            else
            {
                Bildir(VarsayilanBilgi);
            }
        }

        // Karttaki ms ayarı -> "0,6 sn"
        private string SaniyeYazisi(string anahtar, int varsayilan)
        {
            int ms;
            if (!int.TryParse(Al(anahtar, ""), out ms)) ms = varsayilan;
            return (ms / 1000.0).ToString("0.0#", new System.Globalization.CultureInfo("tr-TR")) + " sn";
        }

        // =================================================================
        //  MAKRO
        // =================================================================
        private void GosterMakro()
        {
            ModAyarla(false, false, true);
            yukleniyor = true;
            txtKisayol.Text = MakroForm.Ozet(komutHam, Hazirlar);
            cmbUygulama.SelectedIndex = Hazirlar.GetLength(0) + 3;
            yukleniyor = false;
            KaydetDurumu();
        }

        private async void MakroAc()
        {
            if (metinModu) metinHam = txtKisayol.Text;
            string mevcut = komutHam.StartsWith("MACRO:") ? komutHam : "";

            using (MakroForm f = new MakroForm(mevcut, Hazirlar))
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                {
                    ModuGeriYukle();
                    return;
                }

                bekleyenExe = null;
                komutHam = f.Komut;
            }

            GosterMakro();
            await HemenKaydet("Makro");   // ayrıca AYARI KAYDET'e basmaya gerek kalmasın
        }

        private void KutuculariYenile()
        {
            for (int i = 0; i < kutucuklar.Count; i++) KutucukGuncelle(i);
            OzetGuncelle();
            GovdeIsigiGuncelle();
            IsikDisaridanDegisti();
        }

        // Kısa basış dışındaki komutlar (uzun, çift, basılıyken çevirme)
        private int EkKomutSayisi(int i)
        {
            int n = 0;
            foreach (string ek in new[] { "_LONG", "_DOUBLE", "_CW", "_CCW" })
            {
                string v = Al("BUTTON" + i + ek, "");
                if (v.Length > 0 && v != "NONE") n++;
            }
            return n;
        }

        private void KutucukGuncelle(int i)
        {
            var a = atamalar[i];
            var k = kutucuklar[i];
            bool encoder = (i == atamalar.Count - 1);
            string gosterim = GosterimKisayol(a.Kisayol);
            string ozelAd = OzelAd(i);

            if (encoder)   // çevirme = ENCODER_CW / ENCODER_CCW, basma = BUTTON9
            {
                string cw = Al("ENCODER_CW", "VOLUME_UP"), ccw = Al("ENCODER_CCW", "VOLUME_DOWN");
                string mod = EncoderModAdi();
                string cevir = mod == "özel" ? "Çevirme: özel" : mod;

                int ekstraE = EkKomutSayisi(i);
                k.Ad.Text = (ozelAd ?? "Encoder") + (ekstraE > 0 ? "  +" + ekstraE : "");
                k.Kisayol.Text = a.Kisayol.Length == 0 ? cevir : cevir + " · " + gosterim;
            }
            else
            {
                int ekstra = EkKomutSayisi(i);

                k.Ad.Text = (ozelAd ?? AdBul(a.Kisayol)) + (ekstra > 0 ? "  +" + ekstra : "");
                k.Kisayol.Text = gosterim;
            }

            // Tuşa verilen renk: numara o renkte, sol üstte küçük daire
            Color renk = OzelRenk(i);
            k.Numara.ForeColor = renk.IsEmpty ? Lacivert : renk;
            k.Nokta.Visible = !renk.IsEmpty;
            if (!renk.IsEmpty)
            {
                k.Nokta.DolguRengi = renk;
                k.Nokta.Invalidate();
            }

            // Tuşa verilen simge: sağ üstte, tuşun renginde
            TusAdRenk gorunum = OzelGorunum(i);
            string simgeKarakteri = gorunum != null ? gorunum.SimgeKarakteri : "";
            k.Simge.Visible = simgeKarakteri.Length > 0;
            k.Simge.Text = simgeKarakteri;
            k.Simge.ForeColor = renk.IsEmpty ? Lacivert : renk;

            if (isiHaritasi) k.Kisayol.Text = KullanimYazisi(i);

            BaloncukAyarla(k.Panel, TusAciklamasi(i));
        }

        // =================================================================
        //  TUŞ ADI VE RENGİ (yalnızca uygulamada)
        // =================================================================
        private TusAdRenk OzelGorunum(int i)
        {
            TusAdRenk t;
            return tusAdlari.TryGetValue(i, out t) && !t.Bos ? t : null;
        }

        private string OzelAd(int i)
        {
            TusAdRenk t = OzelGorunum(i);
            return t != null && !string.IsNullOrEmpty(t.Ad) ? t.Ad : null;
        }

        private Color OzelRenk(int i)
        {
            TusAdRenk t = OzelGorunum(i);
            return t != null ? TusAdiForm.HexRenk(t.Renk) : Color.Empty;
        }

        // null ya da boş verilirse tuşun adı ve rengi kaldırılır
        private void OzelGorunumAyarla(int i, TusAdRenk t)
        {
            if (t == null || t.Bos) tusAdlari.Remove(i);
            else tusAdlari[i] = t;
            UygulamaAyarlari.TusAdlariYaz(tusAdlari);
        }

        // Sağ paneldeki "(GP1) · Toplantı sesi" satırı
        private void SeciliTusAltYazisi()
        {
            string ozelAd = OzelAd(secili);
            lblTusPin.Text = "(" + atamalar[secili].Pin + ")" + (ozelAd != null ? "  ·  " + ozelAd : "");
        }

        private async void TusAdiDuzenle(int i)
        {
            // Adlar klavyede de saklandığı için (başka bilgisayara taşınır) bağlıyken verilir
            if (!klavyeBagli)
            {
                Bildir("Ad ve renk vermek için önce klavyeye bağlan.");
                return;
            }

            TusAdRenk mevcut = OzelGorunum(i) ?? new TusAdRenk();

            using (TusAdiForm f = new TusAdiForm(TusEtiketi(i), mevcut.Ad, mevcut.Renk, mevcut.Simge))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                OzelGorunumAyarla(i, new TusAdRenk { Ad = f.Ad, Renk = f.Renk, Simge = f.Simge });
            }

            KutucukGuncelle(i);
            if (i == secili) SeciliTusAltYazisi();
            await TasinanKopyayiGuncelle();
            OtomatikYedek(TusEtiketi(i) + " adı / rengi");

            string ad = OzelAd(i);
            Bildir(OzelGorunum(i) == null
                ? TusEtiketi(i) + " için ad, renk ve simge kaldırıldı."
                : TusEtiketi(i) + (ad != null ? " artık \"" + ad + "\" olarak görünüyor." : " için görünüm kaydedildi."));
        }

        // =================================================================
        //  ENCODER HAZIR ÇEVİRME MODLARI
        // =================================================================
        // Şu anki sağa / sola çevirme hangi hazır moda uyuyor (yoksa "özel")
        private string EncoderModAdi()
        {
            string cw = HotkeyNorm(Al("ENCODER_CW", "VOLUME_UP")), ccw = HotkeyNorm(Al("ENCODER_CCW", "VOLUME_DOWN"));

            for (int i = 0; i < EncoderModlari.GetLength(0); i++)
                if (HotkeyNorm(EncoderModlari[i, 1]) == cw && HotkeyNorm(EncoderModlari[i, 2]) == ccw)
                    return EncoderModlari[i, 0];

            return "özel";
        }

        private List<ToolStripItem> EncoderModOgeleri()
        {
            List<ToolStripItem> liste = new List<ToolStripItem>();
            string simdiki = EncoderModAdi();

            for (int i = 0; i < EncoderModlari.GetLength(0); i++)
            {
                string ad = EncoderModlari[i, 0], cw = EncoderModlari[i, 1], ccw = EncoderModlari[i, 2];
                ToolStripMenuItem o = MenuOgesi(null, ad, () => EncoderModuUygula(ad, cw, ccw));
                o.Checked = ad == simdiki;
                o.ToolTipText = "Sağa: " + KomutAciklamasi(cw) + "   ·   Sola: " + KomutAciklamasi(ccw);
                liste.Add(o);
            }

            liste.Add(new ToolStripSeparator());
            ToolStripMenuItem elle = MenuOgesi(null, "Kendim ayarlayacağım…", () =>
            {
                if (secili != atamalar.Count - 1) KullaniciTusSec(atamalar.Count - 1);
                else KullaniciBasisSec(3);
                Bildir("Sağa ve sola çevirme için Sağa / Sola sekmelerinden istediğin komutu seç.");
            });
            liste.Add(elle);
            return liste;
        }

        private void EncoderMenusuAc(Control yanina, bool imlecte)
        {
            ContextMenuStrip m = TemaMenusu(true);
            m.ShowItemToolTips = true;
            foreach (ToolStripItem o in EncoderModOgeleri()) m.Items.Add(o);

            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            if (imlecte) m.Show(Cursor.Position);
            else m.Show(yanina, new Point(yanina.Width - m.PreferredSize.Width, yanina.Height));
        }

        private async void EncoderModuUygula(string ad, string cw, string ccw)
        {
            if (!klavyeBagli)
            {
                Bildir("Önce klavyeye bağlan.");
                return;
            }
            if (!await DegisiklikleriSor()) return;
            if (!await IslemSirasiniBekle())
            {
                Bildir("Başka bir işlem sürüyor, biraz sonra tekrar dene.");
                return;
            }

            string aciklama = "Encoder çevirme modu: " + ad;
            GeriAlNoktasi(aciklama);

            string eskiCw = Al("ENCODER_CW", "VOLUME_UP"), eskiCcw = Al("ENCODER_CCW", "VOLUME_DOWN");
            kartAyarlari["ENCODER_CW"] = cw;
            kartAyarlari["ENCODER_CCW"] = ccw;

            Bildir(aciklama + "...");
            string hata = await KartaYaz();

            if (hata != null)
            {
                kartAyarlari["ENCODER_CW"] = eskiCw;
                kartAyarlari["ENCODER_CCW"] = eskiCcw;
                GeriAlIptal();
                HataGoster(hata);
                return;
            }

            KutucukGuncelle(atamalar.Count - 1);
            SecimYap(secili);
            OtomatikYedek(aciklama);
            Bildir("Encoder artık " + ad.ToLower(new System.Globalization.CultureInfo("tr-TR")) +
                   " için: sağa " + KomutAciklamasi(cw) + ", sola " + KomutAciklamasi(ccw) + ".");
        }

        // =================================================================
        //  KISAYOL KUTUSU: yalnızca istenince tuş yakalar
        // =================================================================
        private void KutuyaTiklandi()
        {
            if (uygulamaModu) UygulamaSec();
            else if (makroModu) MakroAc();
            else if (metinModu) txtKisayol.Focus();
            else YakalamayiAc();
        }

        private void YakalamayiAc()
        {
            if (metinModu || uygulamaModu || makroModu) return;

            yakalamaAcik = true;
            txtKisayol.Focus();
            if (txtKisayol.Focused) kanca.Baslat();
            KutuGorunumu();
        }

        private void YakalamayiKapat()
        {
            if (!yakalamaAcik) return;
            yakalamaAcik = false;
            kanca.Durdur();
            KutuGorunumu();
        }

        // Klavye simgesi ve ipucu: hazır komutta gizli, özel kısayolda ve yakalarken görünür
        private void KutuGorunumu()
        {
            if (lblKlavyeIkon == null || lblIpucu == null) return;

            bool kisayolModu = !metinModu && !uygulamaModu && !makroModu;
            bool hazir = kisayolModu && komutHam.Length > 0 && HazirIndeks(komutHam) < Hazirlar.GetLength(0);

            // Kutu gerektiğinde görünür: hazır komutta (Kopyala, Sustur ...) gizli
            bool kutu = !hazir || yakalamaAcik;
            kisayolKutu.Visible = kutu;
            lblIpucu.Top = kutu ? kisayolKutu.Bottom + 6 : kisayolKutu.Top + 2;

            // Kutunun içindeki başlık: yapılan işlem
            if (metinModu) lblKutuBaslik.Text = "YAZILACAK METİN";
            else if (uygulamaModu) lblKutuBaslik.Text = "AÇILACAK UYGULAMA / KLASÖR / SİTE";
            else if (makroModu) lblKutuBaslik.Text = "MAKRO ADIMLARI";
            else if (yakalamaAcik) lblKutuBaslik.Text = "KISAYOL · ŞİMDİ TUŞLARA BAS";
            else lblKutuBaslik.Text = "KISAYOL";
            lblKutuBaslik.ForeColor = yakalamaAcik ? Mavi : SolukYazi;

            // Salt okunur kutuda yazı mavi seçili görünmesin (uygulama / makro / hazır komut)
            if (!metinModu) txtKisayol.SelectionLength = 0;

            lblIpucu.ForeColor = SolukYazi;
            lblKlavyeIkon.Visible = kisayolModu && (yakalamaAcik || !hazir);

            // Gizli metin
            lblGozIkon.Visible = metinModu;
            lblGozIkon.Text = metinGizli ? "\uED1A" : "\uE7B3";
            lblGozIkon.ForeColor = metinGizli ? Mavi : SolukYazi;
            txtKisayol.PasswordChar = metinModu && metinGizli ? '●' : '\0';
            KaydetDurumu();
            lblKlavyeIkon.ForeColor = yakalamaAcik ? Mavi : SolukYazi;

            if (metinModu)
                lblIpucu.Text = "Sonuna + Enter ya da + Tab yazarsan o tuşa da basılır.";
            else if (uygulamaModu)
                lblIpucu.Text = "Değiştirmek için kutuya tıkla (uygulama, dosya, klasör, site).";
            else if (makroModu)
                lblIpucu.Text = "Makroyu düzenlemek için kutuya tıkla.";
            else if (yakalamaAcik)
                lblIpucu.Text = "Şimdi kısayola bas. Alt+F4, Alt+Tab, Win+D de olur.";
            else if (hazir)
                lblIpucu.Text = "Hazır komut. Kendi kısayolun için listeden Özel Kısayol'u seç.";
            else
            {
                // Özel kısayolun Windows'ta zaten yaptığı iş (kapatır, kilitler ise kırmızı)
                bool tehlikeli;
                string anlam = WindowsAnlami(komutHam, out tehlikeli);
                lblIpucu.Text = anlam != null
                    ? (tehlikeli ? "Dikkat! " : "") + "Windows'ta bu kısayol: " + anlam + "."
                    : "Kısayolu değiştirmek için kutuya tıkla, sonra kısayola bas.";
                lblIpucu.ForeColor = tehlikeli ? Tema.HataYazi : SolukYazi;
            }
        }

        // Kısayol sekmesinde: basılan tuş kombinasyonunu otomatik algıla
        private void KisayolTusu(object sender, KeyEventArgs e)
        {
            if (metinModu) return;   // metin sekmesinde normal yazı yazılır
            if (!yakalamaAcik) return;   // hazır komut / tıklanmamış kutu: tuşları yakalama

            e.SuppressKeyPress = true;
            e.Handled = true;
            if (uygulamaModu || makroModu) return;   // uygulama / makro seçiliyken yakalama yok

            KombinasyonUygula(e.Control, e.Shift, e.Alt, false, e.KeyCode);
        }

        // Kanca ile yakalanan kombinasyon (Alt+F4, Alt+Tab, Windows+D gibi sistem kısayolları dahil)
        private void KisayolYakala(bool ctrl, bool shift, bool alt, bool win, Keys tus)
        {
            if (metinModu || uygulamaModu || makroModu || !yakalamaAcik) return;
            KombinasyonUygula(ctrl, shift, alt, win, tus);
        }

        private void KombinasyonUygula(bool ctrl, bool shift, bool alt, bool win, Keys k)
        {
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu ||
                k == Keys.LWin || k == Keys.RWin) return;   // sadece değiştirici: bekle

            string ad = TusAdi(k);
            if (ad == null)
            {
                Bildir("Bu tuş desteklenmiyor.");
                return;
            }

            List<string> parcalar = new List<string>();
            if (ctrl) parcalar.Add("CTRL");
            if (shift) parcalar.Add("SHIFT");
            if (alt) parcalar.Add("ALT");
            if (win) parcalar.Add("WINDOWS");
            parcalar.Add(ad);

            komutHam = string.Join("+", parcalar);

            string cakisan = CakismaBul(komutHam);
            bool tehlikeli;
            string anlam = WindowsAnlami(komutHam, out tehlikeli);
            string windows = anlam == null ? ""
                : (tehlikeli ? " Dikkat! Windows'ta: " : " Windows'ta: ") + anlam + ".";

            if (Ayrilmis(komutHam))
                Bildir("Bu kombinasyon uygulama kısayolları için ayrılmış. Başka bir kombinasyon seç.");
            else if (cakisan != null)
                Bildir("Yakalandı: " + Guzel(komutHam) + ". Dikkat: bu kısayol " + cakisan + " için de atanmış." + windows);
            else
                Bildir("Yakalandı: " + Guzel(komutHam) + "." + windows);

            yukleniyor = true;
            txtKisayol.Text = Guzel(komutHam);
            cmbUygulama.SelectedIndex = HazirIndeks(komutHam);
            yukleniyor = false;
            KutuGorunumu();
        }

        // Keys -> macro_keys.py'deki tuş adı (desteklenmeyen tuşta null)
        internal static string TusAdi(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return k.ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)k - (int)Keys.D0).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();

            switch (k)
            {
                case Keys.Enter: return "ENTER";
                case Keys.Escape: return "ESC";
                case Keys.Tab: return "TAB";
                case Keys.Space: return "SPACE";
                case Keys.Back: return "BACKSPACE";
                case Keys.Delete: return "DELETE";
                case Keys.Insert: return "INSERT";
                case Keys.Home: return "HOME";
                case Keys.End: return "END";
                case Keys.PageUp: return "PAGE_UP";
                case Keys.Next: return "PAGE_DOWN";
                case Keys.PrintScreen: return "PRINT_SCREEN";
                case Keys.Up: return "UP";
                case Keys.Down: return "DOWN";
                case Keys.Left: return "LEFT";
                case Keys.Right: return "RIGHT";
                case Keys.OemPeriod: return "PERIOD";
                case Keys.Oemcomma: return "COMMA";
            }
            return null;
        }

        // =================================================================
        //  METİN + TUŞ ("sifre + Enter")
        // =================================================================
        // Metnin sonuna ya da arasına "+ Enter", "+ Tab" yazılırsa o tuşa da basılır.
        private static readonly string[] MetinTuslari =
        {
            "ENTER", "TAB", "ESC", "SPACE", "BACKSPACE", "DELETE",
            "UP", "DOWN", "LEFT", "RIGHT", "HOME", "END", "PAGE_UP", "PAGE_DOWN",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
        };

        private static bool MetinTusuMu(string ad)
        {
            return Array.IndexOf(MetinTuslari, ad.Trim().ToUpperInvariant()) >= 0;
        }

        // "sifre + Tab + parola + Enter"  ->  "sifre{TAB}parola{ENTER}"
        // İlk parça her zaman yazıdır (yalnızca "Enter" yazarsan kelime olarak yazılır).
        private static string MetinKodla(string girdi)
        {
            string[] parca = girdi.Split('+');
            StringBuilder sonuc = new StringBuilder();
            string tampon = parca[0];
            bool yeniYazi = false;      // az önce tuş eklendi: sıradaki yazının baş boşluğunu at

            for (int i = 1; i < parca.Length; i++)
            {
                if (MetinTusuMu(parca[i]))
                {
                    sonuc.Append(tampon.TrimEnd());
                    sonuc.Append("{" + parca[i].Trim().ToUpperInvariant() + "}");
                    tampon = "";
                    yeniYazi = true;
                }
                else if (yeniYazi)
                {
                    tampon = parca[i].TrimStart();
                    yeniYazi = false;
                }
                else
                {
                    tampon += "+" + parca[i];       // yazının içindeki gerçek "+" işareti
                }
            }

            sonuc.Append(tampon);
            return sonuc.ToString();
        }

        // "sifre{TAB}parola{ENTER}"  ->  "sifre + Tab + parola + Enter"
        private static string MetinCoz(string kodlu)
        {
            List<string> parcalar = new List<string>();
            int konum = 0;

            foreach (Match m in Regex.Matches(kodlu, @"\{([A-Za-z0-9_]+)\}"))
            {
                string ad = m.Groups[1].Value;
                if (!MetinTusuMu(ad)) continue;

                if (parcalar.Count == 0 && m.Index == 0) parcalar.Add("");   // baştaki tuş için boş yazı
                if (m.Index > konum) parcalar.Add(kodlu.Substring(konum, m.Index - konum));

                parcalar.Add(ad.Substring(0, 1).ToUpper() + ad.Substring(1).ToLower());
                konum = m.Index + m.Length;
            }

            if (konum < kodlu.Length) parcalar.Add(kodlu.Substring(konum));
            return string.Join(" + ", parcalar);
        }

        // Görünüm düzeni (gerçek klavyeye benzer): üstte 1-2-3-4 + encoder,
        // altta 5-6-7-8 + LED modu.  Dönen değer: (sütun, satır)
        private static Point KartKonumu(int i)
        {
            if (i == 9) return new Point(4, 0);    // encoder: sağ üst
            if (i == 8) return new Point(4, 1);    // LED modu (9): sağ alt
            return new Point(i % 4, i / 4);        // 1-4 üstte, 5-8 altta
        }

        // =================================================================
        //  KOMUT YARDIMCILARI
        // =================================================================
        // Komuta uyan liste satırı (yoksa son satır: Özel Kısayol)
        private int HazirIndeks(string komut)
        {
            int hazir = Hazirlar.GetLength(0);

            if (komut.StartsWith("MACRO:")) return hazir + 3;                       // Makro
            if (komut.StartsWith("TEXT:")) return hazir + 1;                        // Metin Yaz
            if (uygulamalar.ContainsKey(HotkeyNorm(komut))) return hazir + 2;       // Uygulama

            string norm = HotkeyNorm(komut);
            for (int i = 0; i < hazir; i++)
                if (string.Equals(HotkeyNorm(Hazirlar[i, 1]), norm, StringComparison.OrdinalIgnoreCase))
                    return i;

            return hazir;                                                           // Özel Kısayol
        }
    }
}
