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
    // Form1'in parçası: Seri bağlantı: kartı bulma, bağlanma, nabız
    public partial class Form1
    {
        // =================================================================
        //  SERİ BAĞLANTI (port otomatik bulunur)
        // =================================================================
        private async void BaglanTikla()
        {
            await BaglanVeOku(null, false);
        }

        // portlar null ise tüm portlar denenir. sessiz: bulamazsa hata mesajı gösterme
        private async Task BaglanVeOku(string[] portlar, bool sessiz)
        {
            if (islemde) return;
            islemde = true;

            // Arka plandaki otomatik denemelerde düğme yazısı yanıp sönmesin
            string eskiYazi = lblBaglanYazi.Text;
            if (!sessiz) lblBaglanYazi.Text = "ARANIYOR...";
            bool okundu = false;

            try
            {
                // Zaten bağlıysa sadece karttaki ayarları yeniden oku
                bool bagli = klavyeBagli && seri.Bagli;

                if (!bagli)
                {
                    if (!sessiz) Bildir("Seri portlar taranıyor...");
                    bagli = portlar == null
                        ? await Task.Run(() => seri.Baglan())
                        : await Task.Run(() => seri.Baglan(portlar));
                }

                if (!bagli)
                {
                    DurumGuncelle(false);
                    if (!sessiz)
                        Bildir("CircuitPython klavye bulunamadı. USB'yi kontrol et; " +
                               "Mu/PuTTY gibi seri terminal açıksa kapat.");
                    return;
                }

                Dictionary<string, string> gelen = await Task.Run(() => seri.Oku());

                if (gelen == null)
                {
                    DurumGuncelle(false);
                    if (!sessiz) Bildir("Karttan ayarlar okunamadı. settings_server.py yüklü mü?");
                    return;
                }

                // Hangi klavye? (kart 2.4+ kimliğini söyler; geçmiş ve etkin profil klavyeye göre)
                string kartSurum = await Task.Run(() => seri.Surum());
                KlavyeDuzeni.AltKodu = SurumEnAz(kartSurum, 2, 5);   // é, ä, ñ ... Alt koduyla
                string kimlik = SurumEnAz(kartSurum, 2, 4) ? await Task.Run(() => seri.KartKimligi()) : null;
                string oncekiKlavye = UygulamaAyarlari.AktifKlavye;
                bool yeniKlavye = UygulamaAyarlari.KlavyeyiSec(kimlik);
                bool klavyeDegisti = UygulamaAyarlari.AktifKlavye != oncekiKlavye;

                AyarlariYukle(gelen);
                DurumGuncelle(true);
                if (klavyeDegisti) ProfilEtiketiGuncelle();

                string ad = UygulamaAyarlari.KlavyeAdi(UygulamaAyarlari.AktifKlavye);
                if (yeniKlavye && UygulamaAyarlari.Klavyeler().Count > 1)
                {
                    Bildir("Yeni bir klavye bağlandı: \"" + ad + "\". Ad vermek için üstteki adına tıkla.");
                    UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBaglanti, "Yeni klavye tanındı: " + ad);
                }
                else if (klavyeDegisti && UygulamaAyarlari.Klavyeler().Count > 1)
                    Bildir("Bağlandı: " + ad + " (" + seri.PortAdi + "). Ayarlar karttan okundu.");
                else
                    Bildir("Bağlandı: " + seri.PortAdi + ". Ayarlar karttan okundu.");
                okundu = true;
            }
            finally
            {
                islemde = false;
                if (lblBaglanYazi.Text == "ARANIYOR...") lblBaglanYazi.Text = eskiYazi;
            }

            if (!okundu) return;

            await KlavyeyleEsitle();   // başka bilgisayarda kurulmuş uygulama kısayolları / tuş adları
            GecmiseEkle("Bağlanınca karttaki durum");   // ilk değişiklikten önceki hâle dönülebilsin
            await SaglikKontrolu();
            await KartGuncellemesiniSor();
            await DuzenSor();
        }

        // Bağlı değilken: yalnızca yeni takılan portları ve bağlantısı kopan son portu dener
        // (diğer seri cihazlara dokunmaz). Kart açılırken ilk denemede cevap vermeyebileceği için
        // bu portlar YenidenDenemeSuresi boyunca her nabızda (3 sn) yeniden denenir.
        private async Task YeniPortKontrol()
        {
            string[] simdi;
            try { simdi = SerialPort.GetPortNames(); }
            catch (Exception) { return; }

            DateTime an = DateTime.Now;

            foreach (string p in simdi)
                if (!bilinenPortlar.Contains(p))
                    bekleyenPortlar[p] = an.AddSeconds(YenidenDenemeSuresi);

            bilinenPortlar = new HashSet<string>(simdi);

            // Süresi dolanları bırak
            foreach (string p in bekleyenPortlar.Where(kv => kv.Value < an).Select(kv => kv.Key).ToList())
                bekleyenPortlar.Remove(p);

            string[] denenecek = tumPortlariDene
                ? simdi
                : simdi.Where(x => bekleyenPortlar.ContainsKey(x)).ToArray();

            tumPortlariDene = false;

            if (denenecek.Length > 0)
            {
                await BaglanVeOku(denenecek, true);
                if (klavyeBagli) bekleyenPortlar.Clear();
            }
        }

        // Bağlıyken de port listesi güncel kalsın (yoksa çıkar-tak sonrası port "bilinen" sayılır)
        private void PortListesiniGuncelle()
        {
            try { bilinenPortlar = new HashSet<string>(SerialPort.GetPortNames()); }
            catch (Exception) { }
        }
    }
}
