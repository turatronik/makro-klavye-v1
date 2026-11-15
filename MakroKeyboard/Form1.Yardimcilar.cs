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
    // Form1'in parçası: Açıklama balonu, klavye kısayolları, yazdırılabilir tuş listesi, LED
    public partial class Form1
    {
        // =================================================================
        //  AÇIKLAMA BALONCUĞU
        // =================================================================
        private void BaloncukAyarla(Control c, string metin)
        {
            baloncuk.SetToolTip(c, metin);
            foreach (Control alt in c.Controls) BaloncukAyarla(alt, metin);
        }

        // Bir komutun tam, okunur açıklaması (makroda tüm adımlar)
        private string KomutAciklamasi(string komut)
        {
            if (string.IsNullOrEmpty(komut)) return "Atanmadı";
            if (komut == "NONE") return "Boş (bir şey yapmaz)";
            if (komut.StartsWith("TEXT:"))
                return "Metin: " + UygulamaAyarlari.GizliyseMaskele(komut.Substring(5), MetinCoz(komut.Substring(5)));

            if (komut.StartsWith("MACRO:"))
            {
                List<string> adimlar = MakroForm.Coz(komut);
                StringBuilder sb = new StringBuilder("Makro (" + adimlar.Count + " adım)");
                for (int i = 0; i < adimlar.Count; i++)
                    sb.Append("\n      ").Append(i + 1).Append(". ").Append(MakroForm.AdimYazisi(adimlar[i], Hazirlar));
                return sb.ToString();
            }

            if (uygulamalar.ContainsKey(HotkeyNorm(komut))) return UygulamaAciklamasi(komut);

            int n = HazirIndeks(komut);
            if (n < Hazirlar.GetLength(0)) return Hazirlar[n, 0];
            bool tehlikeli;
            string anlam = WindowsAnlami(komut, out tehlikeli);
            return "Kısayol: " + Guzel(komut) + (anlam != null ? "  (Windows'ta: " + anlam + ")" : "");
        }

        private string TusAciklamasi(int i)
        {
            bool encoder = i == atamalar.Count - 1;
            StringBuilder sb = new StringBuilder();
            string ozelAd = OzelAd(i);
            sb.Append(TusEtiketi(i)).Append(ozelAd != null ? "  ·  " + ozelAd : "").Append('\n');

            if (encoder)
            {
                sb.Append("Basma:  ").Append(KomutAciklamasi(atamalar[i].Kisayol)).Append('\n');
                string uzunE = Al("BUTTON" + i + "_LONG", ""), ciftE = Al("BUTTON" + i + "_DOUBLE", "");
                if (uzunE.Length > 0 && uzunE != "NONE") sb.Append("Uzun basma:  ").Append(KomutAciklamasi(uzunE)).Append('\n');
                if (ciftE.Length > 0 && ciftE != "NONE") sb.Append("Çift basma:  ").Append(KomutAciklamasi(ciftE)).Append('\n');
                sb.Append("Sağa:  ").Append(KomutAciklamasi(Al("ENCODER_CW", "VOLUME_UP"))).Append('\n');
                sb.Append("Sola:  ").Append(KomutAciklamasi(Al("ENCODER_CCW", "VOLUME_DOWN"))).Append('\n');
                CevirmeIpucu(sb, i, "Basılı + ");
            }
            else
            {
                sb.Append("Kısa:  ").Append(KomutAciklamasi(atamalar[i].Kisayol)).Append('\n');

                string uzun = Al("BUTTON" + i + "_LONG", "");
                string cift = Al("BUTTON" + i + "_DOUBLE", "");
                if (uzun.Length > 0 && uzun != "NONE") sb.Append("Uzun:  ").Append(KomutAciklamasi(uzun)).Append('\n');
                if (cift.Length > 0 && cift != "NONE") sb.Append("Çift:  ").Append(KomutAciklamasi(cift)).Append('\n');
                CevirmeIpucu(sb, i, "Tut + ");
            }

            if (isiHaritasi) sb.Append("Kullanım:  ").Append(KullanimYazisi(i)).Append('\n');
            sb.Append("\nSürükle: yer değiştir · Sağ tık: menü · Kısayol: ").Append(encoder ? "0" : (i + 1).ToString());
            return sb.ToString();
        }

        // =================================================================
        //  KLAVYE KISAYOLLARI
        // =================================================================
        // Ctrl+S kaydet · Ctrl+Z geri al · 1-9 tuş seç, 0 encoder · sol/sağ ok basış türü
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+1 / 2 / 3: Tuşlar / Işık / Ayarlar
            if (keyData == (Keys.Control | Keys.D1)) { SayfaAc(Sayfa.Tuslar); return true; }
            if (keyData == (Keys.Control | Keys.D2)) { SayfaAc(Sayfa.Isik); return true; }
            if (keyData == (Keys.Control | Keys.D3)) { SayfaAc(Sayfa.Ayarlar); return true; }

            // Aşağıdaki kısayollar yalnızca Tuşlar sayfasında (Ayarlar'da arama kutusuna rakam yazılabilsin)
            if (sayfa != Sayfa.Tuslar) return base.ProcessCmdKey(ref msg, keyData);
            if (txtYeniProfil != null && txtYeniProfil.Focused) return base.ProcessCmdKey(ref msg, keyData);   // profil adı yazılıyor

            bool kutuda = txtKisayol != null && txtKisayol.Focused && (metinModu || yakalamaAcik);

            if (RehberAcik && !kutuda)
            {
                if (keyData == Keys.Escape) { RehberBitir(); return true; }
                if (keyData == Keys.Enter || keyData == Keys.Right) { RehberIleri(); return true; }
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                AyariKaydet();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Z) && !(kutuda && metinModu))   // metin yazarken Ctrl+Z yazıyı geri alsın
            {
                GeriAl();
                return true;
            }

            if (!kutuda && (keyData & Keys.Modifiers) == Keys.None)
            {
                Keys k = keyData & Keys.KeyCode;
                int n = -1;

                if (k >= Keys.D1 && k <= Keys.D9) n = (int)k - (int)Keys.D1;
                else if (k >= Keys.NumPad1 && k <= Keys.NumPad9) n = (int)k - (int)Keys.NumPad1;
                else if (k == Keys.D0 || k == Keys.NumPad0) n = atamalar.Count - 1;

                if (n >= 0 && n < atamalar.Count)
                {
                    KullaniciTusSec(n);
                    return true;
                }
                if (k == Keys.Left)
                {
                    BasisKaydir(-1);
                    return true;
                }
                if (k == Keys.Right)
                {
                    BasisKaydir(1);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // =================================================================
        //  YAZDIRILABİLİR TUŞ LİSTESİ
        // =================================================================
        private void ListeMenusuAc()
        {
            ContextMenuStrip m = TemaMenusu(false);
            m.Items.Add(MenuOgesi(null, "Resim olarak kaydet (PNG)…", () => ListeyiKaydet()));
            m.Items.Add(MenuOgesi(null, "Yazdır… (PDF için \"Microsoft Print to PDF\")", () => ListeyiYazdir()));
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(Cursor.Position);
        }

        private string ListeMetni(string komut)
        {
            if (string.IsNullOrEmpty(komut)) return "—";
            if (komut == "NONE") return "Boş";
            if (komut.StartsWith("TEXT:"))
                return "Metin: " + UygulamaAyarlari.GizliyseMaskele(komut.Substring(5), "\"" + MetinCoz(komut.Substring(5)) + "\"");
            if (komut.StartsWith("MACRO:")) return "Makro: " + MakroForm.Ozet(komut, Hazirlar);
            if (uygulamalar.ContainsKey(HotkeyNorm(komut))) return UygulamaAciklamasi(komut);

            int n = HazirIndeks(komut);
            if (n < Hazirlar.GetLength(0)) return Hazirlar[n, 0];
            return Guzel(komut);
        }

        // Yazdırmak için her zaman açık renkli çizilir
        private Bitmap TusListesiResmi()
        {
            const int W = 1240, Kenar = 40, Sutun = 5, Ara = 18, KartH = 250;
            int kartW = (W - Kenar * 2 - Ara * (Sutun - 1)) / Sutun;
            int H = 150 + KartH * 2 + Ara + 70;

            Color lacivert = Color.FromArgb(18, 45, 78), soluk = Color.FromArgb(95, 115, 140);
            Color mavi = Color.FromArgb(16, 104, 232), kenar = Color.FromArgb(200, 222, 242);

            Bitmap bmp = new Bitmap(W, H);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font baslikF = new Font("Segoe UI", 24f, FontStyle.Bold))
            using (Font altF = new Font("Segoe UI", 11f))
            using (Font noF = new Font("Segoe UI", 22f, FontStyle.Bold))
            using (Font adF = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (Font etF = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (Font yaziF = new Font("Segoe UI", 9.5f))
            using (SolidBrush lac = new SolidBrush(lacivert))
            using (SolidBrush sol = new SolidBrush(soluk))
            using (SolidBrush mav = new SolidBrush(mavi))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.White);

                g.DrawString("Makro Klavye — Tuş Listesi", baslikF, lac, Kenar, 34);
                string profil = UygulamaAyarlari.AktifProfil;
                g.DrawString((profil.Length > 0 ? "Profil: " + profil + "   ·   " : "") +
                             DateTime.Now.ToString("dd.MM.yyyy"), altF, sol, Kenar + 2, 84);

                StringFormat sarma = new StringFormat { Trimming = StringTrimming.EllipsisWord };

                for (int i = 0; i < atamalar.Count; i++)
                {
                    Point h = KartKonumu(i);
                    RectangleF r = new RectangleF(Kenar + h.X * (kartW + Ara), 140 + h.Y * (KartH + Ara), kartW, KartH);
                    bool encoder = i == atamalar.Count - 1;

                    using (GraphicsPath yol = YuvarlakPanel.Yol(r, 14))
                    using (SolidBrush zemin = new SolidBrush(Color.FromArgb(246, 250, 255)))
                    using (Pen p = new Pen(kenar, 1.5f))
                    {
                        g.FillPath(zemin, yol);
                        g.DrawPath(p, yol);
                    }

                    Color tusRengi = OzelRenk(i);
                    using (SolidBrush numaraFirca = new SolidBrush(tusRengi.IsEmpty ? mavi : tusRengi))
                        g.DrawString(encoder ? "⟳" : (i + 1).ToString(), encoder ? new Font("Segoe UI Symbol", 20f) : noF,
                                     numaraFirca, r.X + 14, r.Y + 8);
                    g.DrawString(OzelAd(i) ?? (encoder ? "Encoder" : AdBul(atamalar[i].Kisayol)), adF, lac,
                                 new RectangleF(r.X + 60, r.Y + 18, r.Width - 70, 22), sarma);

                    TusAdRenk gorunum = OzelGorunum(i);
                    if (gorunum != null && gorunum.SimgeKarakteri.Length > 0)
                        using (Font simgeF = new Font("Segoe MDL2 Assets", 14f))
                        using (SolidBrush simgeB = new SolidBrush(tusRengi.IsEmpty ? mavi : tusRengi))
                            g.DrawString(gorunum.SimgeKarakteri, simgeF, simgeB, r.Right - 34, r.Y + 44);

                    List<KeyValuePair<string, string>> satirlar = new List<KeyValuePair<string, string>>();
                    if (encoder)
                    {
                        satirlar.Add(new KeyValuePair<string, string>("BASMA", ListeMetni(atamalar[i].Kisayol)));
                        string uzunE = Al("BUTTON" + i + "_LONG", ""), ciftE = Al("BUTTON" + i + "_DOUBLE", "");
                        if (uzunE.Length > 0 && uzunE != "NONE") satirlar.Add(new KeyValuePair<string, string>("UZUN BASMA", ListeMetni(uzunE)));
                        if (ciftE.Length > 0 && ciftE != "NONE") satirlar.Add(new KeyValuePair<string, string>("ÇİFT BASMA", ListeMetni(ciftE)));
                        satirlar.Add(new KeyValuePair<string, string>("SAĞA", ListeMetni(Al("ENCODER_CW", "VOLUME_UP"))));
                        satirlar.Add(new KeyValuePair<string, string>("SOLA", ListeMetni(Al("ENCODER_CCW", "VOLUME_DOWN"))));
                    }
                    else
                    {
                        satirlar.Add(new KeyValuePair<string, string>("KISA", ListeMetni(atamalar[i].Kisayol)));
                        string uzun = Al("BUTTON" + i + "_LONG", ""), cift = Al("BUTTON" + i + "_DOUBLE", "");
                        if (uzun.Length > 0 && uzun != "NONE") satirlar.Add(new KeyValuePair<string, string>("UZUN", ListeMetni(uzun)));
                        if (cift.Length > 0 && cift != "NONE") satirlar.Add(new KeyValuePair<string, string>("ÇİFT", ListeMetni(cift)));
                    }

                    float y = r.Y + 62;
                    float kalan = r.Bottom - 12 - y;
                    float her = Math.Max(40, kalan / Math.Max(1, satirlar.Count));

                    foreach (var s in satirlar)
                    {
                        g.DrawString(s.Key, etF, sol, r.X + 14, y);
                        g.DrawString(s.Value, yaziF, lac, new RectangleF(r.X + 14, y + 16, r.Width - 24, her - 20), sarma);
                        y += her;
                    }
                }

                g.DrawString("Makro Klavye uygulamasıyla oluşturuldu", altF, sol, Kenar, H - 46);
            }
            return bmp;
        }

        private void ListeyiKaydet()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Tuş listesini kaydet";
                d.Filter = "PNG resmi (*.png)|*.png";
                string profil = UygulamaAyarlari.AktifProfil;
                d.FileName = "makro-klavye-tuslar" + (profil.Length > 0 ? "-" + UygulamaAyarlari.AdTemizle(profil) : "") + ".png";

                if (d.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    using (Bitmap bmp = TusListesiResmi())
                        bmp.Save(d.FileName, ImageFormat.Png);
                    Bildir("Tuş listesi kaydedildi: " + Path.GetFileName(d.FileName));
                }
                catch (Exception ex)
                {
                    Bildir("Kaydedilemedi: " + ex.Message);
                }
            }
        }

        private void ListeyiYazdir()
        {
            Bitmap bmp = TusListesiResmi();
            PrintDocument belge = new PrintDocument { DocumentName = "Makro Klavye - Tuş Listesi" };
            belge.DefaultPageSettings.Landscape = true;
            belge.PrintPage += (s, e) =>
            {
                Rectangle alan = e.MarginBounds;
                float olcek = Math.Min(alan.Width / (float)bmp.Width, alan.Height / (float)bmp.Height);
                int w = (int)(bmp.Width * olcek), h = (int)(bmp.Height * olcek);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(bmp, alan.X + (alan.Width - w) / 2, alan.Y + (alan.Height - h) / 2, w, h);
                e.HasMorePages = false;
            };

            try
            {
                using (PrintDialog d = new PrintDialog { Document = belge, UseEXDialog = true })
                {
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        belge.Print();
                        Bildir("Tuş listesi yazdırılıyor.");
                    }
                }
            }
            catch (Exception ex)
            {
                Bildir("Yazdırılamadı: " + ex.Message);
            }
            finally
            {
                belge.Dispose();
                bmp.Dispose();
            }
        }

        // Seçili tuş mavi, o an basılan tuş yeşil çerçeveli
        private void KutucukStilleri()
        {
            int enCok = isiHaritasi ? EnCokKullanim() : 0;

            for (int k = 0; k < kutucuklar.Count; k++)
            {
                bool s = (k == secili);
                bool v = (k == vurguIndex);
                var p = kutucuklar[k].Panel;

                // Isı haritasında zemin kullanım rengidir; seçili tuş yalnızca mavi çerçeveyle belli olur
                Color zemin = isiHaritasi ? IsiRengi(k, enCok) : KutuDolgu;

                p.DolguRengi = v ? Tema.OkDolgu : (s && !isiHaritasi ? Tema.Secili : zemin);
                p.KenarRengi = v ? Color.FromArgb(34, 197, 94) : (s ? Mavi : KenarAcik);
                p.KenarKalinligi = (s || v) ? 2 : 1;
                p.HoverRengi = (s || v || isiHaritasi) ? Color.Empty : Tema.Hover;

                if (surukleniyor && k == surukleKaynak)           // sürüklenen tuş soluk
                {
                    p.DolguRengi = KutuDolgu;
                    p.KenarRengi = Tema.Pasif;
                    p.KenarKalinligi = 2;
                }
                else if (surukleniyor && k == surukleHedef)       // bırakılacak yer
                {
                    p.DolguRengi = Tema.Secili;
                    p.KenarRengi = Mavi;
                    p.KenarKalinligi = 3;
                }

                p.Invalidate();
            }
        }

        // =================================================================
        //  LED AYARLARI
        //  LED ayarı kaydedilince (ya da klavyedeki LED tuşuyla değişince) etkin profil varsa
        //  o profilin dosyasına da yazılır: profile dönünce LED de onunla gelir.
        // =================================================================
        private void LedAc()
        {
            SayfaAc(Sayfa.Isik);   // LED ayarları artık ana pencerede: Işık sekmesi (Form1.Sayfalar.cs)
        }

        // Önizleme komutlarını sıraya koyar: kaydırıcı hızlı değişse bile en son değer gönderilir
        private async void LedGonder()
        {
            GovdeIsigiGuncelle();   // önizleme: klavye çizimindeki ışık da değişsin
            if (ledGonderiyor)
            {
                ledTekrar = true;
                return;
            }

            ledGonderiyor = true;
            try
            {
                do
                {
                    ledTekrar = false;
                    string m = ledMod, c = ledRenk, a = ledAkis;
                    int p = ledParlaklik, h = ledHiz;
                    await Task.Run(() => seri.LedUygula(m, c, p, h, a));
                }
                while (ledTekrar);
            }
            finally { ledGonderiyor = false; }
        }

        private void CevirmeIpucu(StringBuilder sb, int i, string onEk)
        {
            string cw = Al("BUTTON" + i + "_CW", ""), ccw = Al("BUTTON" + i + "_CCW", "");
            if (cw.Length > 0 && cw != "NONE") sb.Append(onEk).Append("sağa:  ").Append(KomutAciklamasi(cw)).Append('\n');
            if (ccw.Length > 0 && ccw != "NONE") sb.Append(onEk).Append("sola:  ").Append(KomutAciklamasi(ccw)).Append('\n');
        }

        private void Bildir(string mesaj)
        {
            if (lblBilgi != null) lblBilgi.Text = mesaj;
            if (sayfa == Sayfa.Isik && lblIsikBilgi != null)
            {
                lblIsikBilgi.Text = mesaj;
                lblIsikBilgi.ForeColor = SolukYazi;
            }
        }

        // Etkin profilin dosyasındaki LED_* satırlarını karttaki güncel LED ayarıyla değiştirir
        private void AktifProfileLedYaz()
        {
            string ad = UygulamaAyarlari.AktifProfil;
            if (string.IsNullOrEmpty(ad)) return;

            try
            {
                if (!System.IO.File.Exists(UygulamaAyarlari.ProfilYolu(ad))) return;
                List<string> satirlar = UygulamaAyarlari.ProfilOku(ad).Replace("\r", "").Split('\n')
                    .Where(s => !s.StartsWith("LED_")).ToList();
                while (satirlar.Count > 0 && satirlar[satirlar.Count - 1].Length == 0) satirlar.RemoveAt(satirlar.Count - 1);

                foreach (var kv in kartAyarlari.Where(k => k.Key.StartsWith("LED_")).OrderBy(k => k.Key, StringComparer.Ordinal))
                    satirlar.Add(kv.Key + "=" + kv.Value);

                UygulamaAyarlari.ProfilYaz(ad, string.Join("\n", satirlar) + "\n");
            }
            catch (Exception ex)
            {
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari, "LED ayarı \"" + ad + "\" profiline yazılamadı: " + ex.Message);
            }
        }
    }
}
