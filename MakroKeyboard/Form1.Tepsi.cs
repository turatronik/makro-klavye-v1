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
    // Form1'in parçası: Sistem tepsisi
    public partial class Form1
    {
        // =================================================================
        //  SİSTEM TEPSİSİ
        // =================================================================
        private void TepsiOlustur()
        {
            tepsiMenu = new ContextMenuStrip();

            ToolStripMenuItem ac = new ToolStripMenuItem("Makro Klavye'yi aç", null, (s, e) => PencereyiGoster());
            ac.Font = new Font(ac.Font, FontStyle.Bold);

            // Bağlı değilken elle bağlanma (tepsiden çıkmadan)
            tepsiBaglan = new ToolStripMenuItem("Klavyeye bağlan", null, async (s, e) =>
            {
                if (klavyeBagli) return;
                await BaglanVeOku(null, true);
                if (!klavyeBagli && tepsi != null)
                    tepsi.ShowBalloonTip(3000, "Makro Klavye",
                        "Klavye bulunamadı. USB'yi kontrol et; seri terminal (Mu/PuTTY) açıksa kapat.",
                        ToolTipIcon.Warning);
            });

            tepsiProfilMenu = new ToolStripMenuItem("Profil");
            ToolStripMenuItem denetle = new ToolStripMenuItem("Güncellemeleri denetle", null, (s, e) => GuncellemeleriDenetle());
            ToolStripMenuItem ayarlar = new ToolStripMenuItem("Ayarlar…", null, (s, e) =>
            {
                PencereyiGoster();
                AyarlariAc();
            });
            ToolStripMenuItem cikis = new ToolStripMenuItem("Çıkış", null, (s, e) =>
            {
                cikisIstendi = true;
                Close();
            });

            tepsiMenu.Items.AddRange(new ToolStripItem[]
            {
                ac, tepsiBaglan, new ToolStripSeparator(), tepsiProfilMenu, denetle, ayarlar, new ToolStripSeparator(), cikis
            });
            tepsiMenu.Opening += (s, e) =>
            {
                ProfilMenusunuDoldur();
                tepsiBaglan.Text = klavyeBagli ? "Bağlı: " + sonPort : "Klavyeye bağlan";
                tepsiBaglan.Enabled = !klavyeBagli && !islemde;
            };

            tepsi = new NotifyIcon
            {
                Text = klavyeBagli ? "Makro Klavye - bağlı (" + sonPort + ")" : "Makro Klavye - bağlı değil",
                Icon = TepsiSimgesi(),
                ContextMenuStrip = tepsiMenu
            };
            tepsi.DoubleClick += (s, e) => PencereyiGoster();

            TepsiGuncelle();
        }

        private void TepsiGuncelle()
        {
            if (tepsi != null) tepsi.Visible = UygulamaAyarlari.Tepsi;
        }

        private void PencereyiGoster()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ProfilMenusunuDoldur()
        {
            tepsiProfilMenu.DropDownItems.Clear();

            List<string> adlar = UygulamaAyarlari.Profiller();
            string aktif = UygulamaAyarlari.AktifProfil;

            if (adlar.Count == 0)
            {
                tepsiProfilMenu.DropDownItems.Add(new ToolStripMenuItem("Kayıtlı profil yok") { Enabled = false });
                return;
            }

            foreach (string ad in adlar)
            {
                string profil = ad;
                ToolStripMenuItem oge = new ToolStripMenuItem(profil)
                {
                    Checked = string.Equals(profil, aktif, StringComparison.CurrentCultureIgnoreCase),
                    Enabled = klavyeBagli
                };
                oge.Click += async (s, e) => await TepsidenProfilYukle(profil);
                tepsiProfilMenu.DropDownItems.Add(oge);
            }

            if (!klavyeBagli)
                tepsiProfilMenu.DropDownItems.Add(new ToolStripMenuItem("(klavye bağlı değil)") { Enabled = false });
        }

        private async Task TepsidenProfilYukle(string ad)
        {
            string hata;

            try { hata = await ProfilUygulaAsync(UygulamaAyarlari.ProfilOku(ad), "\"" + ad + "\" profiline geçiş"); }
            catch (Exception ex) { hata = ex.Message; }

            if (hata == null)
            {
                UygulamaAyarlari.AktifProfil = ad;
                ProfilEtiketiGuncelle();
                tepsi.ShowBalloonTip(2000, "Makro Klavye", "\"" + ad + "\" profili yüklendi.", ToolTipIcon.Info);
            }
            else
            {
                tepsi.ShowBalloonTip(3000, "Makro Klavye", hata, ToolTipIcon.Warning);
            }
        }

        // Karttan gelen BUTTONn=komut satırlarını tuşlara aktar
        private void AyarlariYukle(Dictionary<string, string> gelen)
        {
            UygulamalariTara();

            kartAyarlari = new Dictionary<string, string>(gelen);
            KlavyeDuzeni.Kod = KlavyeDuzeni.Coz(Al("KLAVYE_DUZENI", "US"));

            for (int i = 0; i < atamalar.Count; i++)
            {
                string komut;
                if (gelen.TryGetValue("BUTTON" + i, out komut))
                    atamalar[i].Kisayol = komut;
            }

            ledMod = Al("LED_MODE", ledMod);
            ledRenk = Al("LED_COLOR", ledRenk);
            int par;
            if (int.TryParse(Al("LED_BRIGHTNESS", "100"), out par)) ledParlaklik = par;
            if (int.TryParse(Al("LED_SPEED", "100"), out par)) ledHiz = par;
            ledAkis = Al("LED_FLOW", LedAyarForm.VarsayilanAkis);

            KutuculariYenile();
            SecimYap(secili);
        }

        private string Al(string anahtar, string varsayilan)
        {
            string deger;
            return kartAyarlari.TryGetValue(anahtar, out deger) && deger.Length > 0 ? deger : varsayilan;
        }

        // Ekrandaki ayardan karta gidecek komutu üretir; geçersizse mesaj verip null döner
        private string KomutUret()
        {
            if (metinModu)
            {
                string metin = txtKisayol.Text;

                if (metin.Length == 0)
                {
                    Bildir("Yazılacak metni gir.");
                    return null;
                }
                string duzenHatasi = KlavyeDuzeni.Hata(metin);
                if (duzenHatasi != null)
                {
                    Bildir(duzenHatasi);
                    return null;
                }

                return "TEXT:" + MetinKodla(metin);   // başına TEXT: kendiliğinden eklenir
            }

            string komut = komutHam;

            if (uygulamaModu && bekleyenExe == null && !uygulamalar.ContainsKey(HotkeyNorm(komut)))
            {
                Bildir("Önce kutuya tıklayıp ne açılacağını seç.");
                return null;
            }
            if (komut.Length == 0)
            {
                Bildir("Önce kısayol kutusuna tıkla ve kombinasyona bas.");
                return null;
            }

            return komut;
        }

        // Karttaki tüm ayarları (BUTTON*, LED_* ...) gönderir. null = başarılı
        private async Task<string> KartaYaz()
        {
            Dictionary<string, string> kopya = new Dictionary<string, string>(kartAyarlari);

            islemde = true;
            try { return await Task.Run(() => seri.Kaydet(kopya)); }
            finally { islemde = false; }
        }

        private void HataGoster(string hata)
        {
            UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari,
                                        (sonIslem ?? "Ayar") + " klavyeye yazılamadı: " + hata);

            if (hata.IndexOf("Read-only", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Bildir("Kart salt okunur. boot.py'deki storage.remount satırını kontrol et.");
            }
            else
            {
                Bildir("Yazılamadı: " + hata);
                if (!seri.Bagli) DurumGuncelle(false);
            }
        }

        private async void AyariKaydet()
        {
            await AyariKaydetAsync();
        }

        private async Task<bool> AyariKaydetAsync()
        {
            if (islemde) return false;

            if (!klavyeBagli)
            {
                Bildir("Önce klavyeye bağlan.");
                return false;
            }

            // Uzun / çift basış boş kaydedilirse kaldırılır
            bool kaldir = (basisTuru == 1 || basisTuru == 2 || basisTuru == 5 || basisTuru == 6) &&
                          !metinModu && !uygulamaModu && !makroModu &&
                          (komutHam.Length == 0 || komutHam == "NONE");

            string komut = kaldir ? "" : KomutUret();
            if (komut == null) return false;

            // Gizli metin işareti (karta gitmez, yalnızca ekranda ●●● gösterilir)
            if (metinModu && komut.StartsWith("TEXT:"))
                UygulamaAyarlari.GizliAyarla(komut.Substring(5), metinGizli);

            string anahtar = Anahtar();
            string eskiKomut = AktifKomut();

            // Bu tuşta şu an kayıtlı olan uygulama kısayol dosyası (yalnızca kısa basış; yoksa null)
            string eskiLnk = null;
            if (basisTuru == 0) uygulamalar.TryGetValue(HotkeyNorm(eskiKomut), out eskiLnk);

            // Uygulama kısayolları için ayrılmış kombinasyonlar elle verilemez
            // (kartta zaten kayıtlı olan değişmemişse sorun yok)
            if (!kaldir && !uygulamaModu && komut != eskiKomut && Ayrilmis(komut))
            {
                Bildir("Bu kombinasyon uygulama kısayolları için ayrılmış. Başka bir kombinasyon seç.");
                return false;
            }

            // Aynı komut başka bir tuşta da varsa sor
            string cakisan = kaldir ? null : CakismaBul(komut);
            if (cakisan != null &&
                MessageBox.Show(this, GosterimKisayol(komut) + " zaten " + cakisan + " için atanmış.\n\n" +
                                "İki tuş aynı şeyi yapacak. Yine de kaydedilsin mi?", "Aynı komut",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Bildir("Kaydedilmedi. Başka bir komut seçebilirsin.");
                return false;
            }

            string yedekAciklama = TusEtiketi(secili) + " · " + BasisAdi();
            GeriAlNoktasi(yedekAciklama);

            string eskiAyar;
            bool eskiVar = kartAyarlari.TryGetValue(anahtar, out eskiAyar);

            if (kaldir) kartAyarlari.Remove(anahtar);
            else kartAyarlari[anahtar] = komut;
            if (basisTuru == 0) atamalar[secili].Kisayol = komut;
            KutucukGuncelle(secili);

            Bildir("Kaydediliyor...");
            string hata = await KartaYaz();

            if (hata == null)
            {
                string mesaj = kaldir
                    ? BasisAdi() + " ataması kaldırıldı."
                    : BasisAdi() + " kaydedildi. Klavyeye anında uygulandı.";

                if (basisTuru == 0)
                {
                    try
                    {
                        UygulamaKisayoluGuncelle(eskiLnk, uygulamaModu ? bekleyenExe : null, bekleyenAd, komut, uygulamaModu);
                    }
                    catch (Exception ex)
                    {
                        mesaj = "Kaydedildi ama uygulama kısayolu güncellenemedi: " + ex.Message;
                    }
                }
                else
                {
                    SecimYap(secili);   // sekmedeki "•" işareti ve kutu güncellensin
                }

                await TasinanKopyayiGuncelle();
                OtomatikYedek(yedekAciklama,
                              kaldir ? "ataması kaldırıldı"
                                     : uygulamaModu && basisTuru == 0 && bekleyenExe != null
                                         ? HedefTuru(bekleyenExe) + " → " + bekleyenAd
                                         : ListeMetni(komut));
                Bildir(mesaj);
                return true;
            }

            // Kayıt başarısız: ekrandaki durumu karttakiyle aynı tut
            if (basisTuru == 0) atamalar[secili].Kisayol = eskiKomut;
            if (eskiVar) kartAyarlari[anahtar] = eskiAyar; else kartAyarlari.Remove(anahtar);
            KutucukGuncelle(secili);
            KaydetDurumu();

            GeriAlIptal();
            HataGoster(hata);
            return false;
        }
    }
}
