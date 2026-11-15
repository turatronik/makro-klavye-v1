using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Form1'in parçası: soldaki PROFİLLER panelinin altındaki Hazır profiller / Yedekle / Geri yükle
    // (eskiden Ayarlar > Profiller sekmesindeydi).
    public partial class Form1
    {
        private void Uyar(string metin)
        {
            Bildir(metin);
            MessageBox.Show(this, metin, "Makro Klavye", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Profil metnini klavyeye yazar (null = bağlı değil / hata gösterildi)
        private async Task<bool> KartaProfilUygula(string metin, string bekleMesaji, string aciklama)
        {
            if (!klavyeBagli)
            {
                Uyar("Önce klavyeye bağlan.");
                return false;
            }

            Bildir(bekleMesaji);
            string hata;
            try { hata = await ProfilUygulaAsync(metin, aciklama); }
            catch (Exception ex) { hata = ex.Message; }

            if (hata != null)
            {
                Uyar(hata);
                return false;
            }
            return true;
        }

        // ---------- Hazır profiller ----------
        private void HazirProfilMenusuAc(Control yer)
        {
            if (islemde) return;

            ContextMenuStrip m = TemaMenusu(false);
            m.ShowItemToolTips = true;
            foreach (HazirProfiller.Sablon s in HazirProfiller.Liste)
            {
                HazirProfiller.Sablon sablon = s;
                ToolStripMenuItem o = MenuOgesi(null, s.Ad, () => HazirProfilYukle(sablon));
                o.ToolTipText = s.Aciklama;
                m.Items.Add(o);
            }

            m.Closed += (x, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(yer, new Point(0, yer.Height));
        }

        private async void HazirProfilYukle(HazirProfiller.Sablon s)
        {
            if (islemde) return;
            if (!klavyeBagli)
            {
                Uyar("Önce klavyeye bağlan.");
                return;
            }

            if (MessageBox.Show(this,
                    "\"" + s.Ad + "\" hazır profili yüklensin mi?\n\n" + s.Aciklama + "\n\n" +
                    "Tüm tuş atamaların değişir, tuşlara atanmış uygulama kısayolları kaldırılır. LED ayarın ve basış sürelerin korunur.\n" +
                    "Eski hâline Geri al ile ya da Ayarlar > Geçmiş'ten dönebilirsin.",
                    "Hazır profil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // Karttaki LED ayarlarını ve süreleri koru, tuş atamalarını şablondan al
            StringBuilder sb = new StringBuilder("# Hazır profil: " + s.Ad + "\n");
            string mevcut = ProfilMetni() ?? "";
            foreach (string satir in mevcut.Split('\n'))
                if (satir.StartsWith("LED_") || satir.StartsWith("LONG_PRESS_MS=") || satir.StartsWith("DOUBLE_PRESS_MS=") ||
                    satir.StartsWith("KLAVYE_DUZENI=") || satir.StartsWith("TEXT_DELAY_MS=") ||
                    satir.StartsWith("ENCODER_TERS=") || satir.StartsWith("ENCODER_ADIM=") || satir.StartsWith("UYKU_") ||
                    satir.StartsWith("TITRESIM_MS=") || satir.StartsWith("ENCODER_TEKRAR="))
                    sb.Append(satir.TrimEnd('\r')).Append('\n');
            sb.Append(s.Metin);

            if (await KartaProfilUygula(sb.ToString(), "\"" + s.Ad + "\" yükleniyor...", "Hazır profil: " + s.Ad))
            {
                UygulamaAyarlari.AktifProfil = "";
                ProfilEtiketiGuncelle();
                KutucukGuncelle(atamalar.Count - 1);
                Bildir("\"" + s.Ad + "\" yüklendi. Beğendiysen adı yazılı kutuda Enter'a bas: profil olarak saklanır.");

                // Profil olarak saklamak için ad kutusu hazır
                YeniProfilAc();
                if (yeniProfilKutu.Visible)
                {
                    txtYeniProfil.Text = s.Ad;
                    txtYeniProfil.SelectAll();
                }
            }
        }

        // ---------- Yedekle ----------
        private void ProfilYedekle()
        {
            if (islemde) return;

            string metin = ProfilMetni();   // bağlı değilse null
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
                    Uyar("Önce klavyeye bağlan. Bu yedek, klavyedeki ayarlardan alınır.");
                    return;
                }

                try
                {
                    UygulamaAyarlari.DosyaYaz(yol, metin);
                    UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi, "Klavye ayarları yedeklendi: " + Path.GetFileName(yol));
                    Bildir("Yedek kaydedildi: " + Path.GetFileName(yol));
                }
                catch (Exception ex)
                {
                    Uyar("Yedeklenemedi: " + ex.Message);
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
                Bildir("Tam yedek kaydedildi (" + y.Ozet() + ").");
            }
            catch (Exception ex)
            {
                Uyar("Yedeklenemedi: " + ex.Message);
            }
        }

        // ---------- Geri yükle ----------
        private async void YedektenGeriYukle()
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
                await TamYedektenYukle(yol);
                return;
            }

            if (MessageBox.Show(this, "Karttaki tüm ayarlar bu yedekle değiştirilecek. Devam edilsin mi?",
                                "Geri yükle", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string metin;
            try { metin = UygulamaAyarlari.DosyaOku(yol); }
            catch (Exception ex)
            {
                Uyar("Dosya okunamadı: " + ex.Message);
                return;
            }

            if (await KartaProfilUygula(metin, "Yedek karta yükleniyor...", "Yedekten geri yükleme"))
            {
                UygulamaAyarlari.AktifProfil = "";
                ProfilEtiketiGuncelle();
                Bildir("Yedek karta yüklendi.");
            }
        }

        // Tam yedekten: önce şimdiki hâl saklanır, istenirse klavye geri yüklenir,
        // sonra bu bilgisayardaki veriler değiştirilip uygulama yeniden başlatılır.
        private async Task TamYedektenYukle(string yol)
        {
            TamYedek y;
            try { y = UygulamaAyarlari.TamYedekOku(yol); }
            catch (Exception ex)
            {
                Uyar("Yedek okunamadı: " + ex.Message);
                return;
            }

            string kaynak = "";
            if (y.Tarih.HasValue) kaynak += AyarlarForm.ZamanYazisi(y.Tarih.Value);
            if (y.Bilgisayar.Length > 0) kaynak += (kaynak.Length > 0 ? " · " : "") + y.Bilgisayar;

            string soru = (kaynak.Length > 0 ? "Yedek: " + kaynak + "\n" : "") +
                          "İçinde: " + y.Ozet() + "\n\n" +
                          "Bu bilgisayardaki profiller, makrolar, geçmiş, tuş adları ve uygulama ayarları " +
                          "yedektekiyle değiştirilecek. Şimdiki hâl önce \"" + UygulamaAyarlari.OncekiHalDosyasi +
                          "\" olarak saklanır. Sonra uygulama yeniden başlar.";

            bool klavyeye = false;
            if (y.KlavyeMetni != null && klavyeBagli)
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
            try { UygulamaAyarlari.OncekiHaliSakla(ProfilMetni()); }
            catch (Exception ex)
            {
                Uyar("Şimdiki hâl saklanamadığı için geri yükleme yapılmadı: " + ex.Message);
                return;
            }

            if (klavyeye && !await KartaProfilUygula(y.KlavyeMetni, "Klavyedeki ayarlar geri yükleniyor...", "Tam yedekten geri yükleme"))
                return;   // hata gösterildi, uygulama verilerine dokunulmadı

            try { UygulamaAyarlari.TamYedekGeriYukle(y); }
            catch (Exception ex)
            {
                Uyar("Geri yüklenemedi: " + ex.Message + " Önceki hâl: " + UygulamaAyarlari.OncekiHalYolu);
                return;
            }

            MessageBox.Show(this, "Yedek geri yüklendi" + (klavyeye ? " (klavye dahil)" : "") +
                                  ". Uygulama şimdi yeniden başlatılacak.",
                            "Tam yedek", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Program.YenidenBaslat();
        }
    }
}
