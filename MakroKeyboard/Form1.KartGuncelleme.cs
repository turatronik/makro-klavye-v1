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
    // Form1'in parçası: Kart yazılımı ve uygulama güncelleme
    public partial class Form1
    {
        // =================================================================
        //  KART YAZILIMI GÜNCELLEME
        // =================================================================
        private static readonly string[] KartDosyaSirasi =
            { "settings_server.py", "macro_keys.py", "led_animations.py", "code.py", "boot.py" };   // code.py ana yazılım

        // boot.py'yi kart yazılımı 2.3'ten önce kabul etmez: yeni yazılım açıldıktan sonra gönderilir
        private const string BootDosyasi = "boot.py";

        private static bool SurumEnAz(string surum, int buyuk, int kucuk)
        {
            Version v;
            return surum != null && Version.TryParse(surum, out v) && v >= new Version(buyuk, kucuk);
        }

        // Zip'ten / klasörden okunmuş kart dosyalarını (ad -> içerik) seri porttan karta yazar,
        // kartı yeniden başlatıp ayarları yeniden okur
        private async Task<string> KartiGuncelleAsync(Dictionary<string, byte[]> paket, Action<string, int> ilerleme)
        {
            if (!klavyeBagli) return "Önce klavyeye bağlan.";
            if (!await IslemSirasiniBekle()) return "Başka bir işlem sürüyor, biraz sonra tekrar dene.";

            List<string> dosyalar = KartDosyaSirasi.Where(paket.ContainsKey).ToList();
            if (dosyalar.Count == 0) return "Seçilen dosyada kart dosyası yok.";

            long toplam = Math.Max(1, dosyalar.Sum(ad => (long)paket[ad].Length));
            long gonderilen = 0;

            islemde = true;
            try
            {
                string surum = await Task.Run(() => seri.Surum());
                if (surum == null)
                    return "Karttaki yazılım uygulamadan güncellemeyi desteklemiyor. " +
                           "Dosyaları bir kez elle kopyala (Kart sekmesindeki nota bak).";

                string yeniSurum = KartPaketi.Surum(paket);
                bool bootSonra = dosyalar.Contains(BootDosyasi) && !SurumEnAz(surum, 2, 3);

                Func<string, Task<string>> gonder = async ad =>
                {
                    byte[] veri = paket[ad];
                    long once = gonderilen;
                    ilerleme(ad + " gönderiliyor…", (int)(once * 100 / toplam));

                    Action<int> yuzde = y => UiyeAt(() =>
                        ilerleme(ad + " gönderiliyor… %" + y, (int)((once + veri.Length * y / 100) * 100 / toplam)));

                    string h = await Task.Run(() => seri.DosyaGonder(ad, veri, yuzde));
                    if (h != null)
                    {
                        if (!seri.Bagli) DurumGuncelle(false);
                        return ad + " gönderilemedi: " + h;
                    }
                    gonderilen += veri.Length;
                    return null;
                };

                foreach (string ad in dosyalar)
                {
                    if (bootSonra && ad == BootDosyasi) continue;
                    string hata = await gonder(ad);
                    if (hata != null) return hata;
                }

                ilerleme("Kart yeniden başlatılıyor…", 100);
                await Task.Run(() => seri.YenidenBaslat());

                // Yeni yazılım açılsın. Kart 2.3+ yeni yazılımı önce dener: açılamazsa ya da
                // ilk saniyelerde art arda hata verirse kendiliğinden eski sürüme döner.
                string calisan = await YeniKartYaziliminiBekle(yeniSurum, ilerleme);

                if (calisan == null)
                {
                    DurumGuncelle(false);
                    return "Dosyalar gönderildi ama kart yeniden başladıktan sonra bağlanılamadı. BAĞLAN'a bas.";
                }

                Dictionary<string, string> gelen = await Task.Run(() => seri.Oku());
                if (gelen != null) AyarlariYukle(gelen);
                DurumGuncelle(true);

                if (yeniSurum != null && yeniSurum != "?" && calisan != yeniSurum)
                {
                    // Kart yeni yazılımı çalıştıramadı: neden TANI'da (GERI_DONUS=eski|neden)
                    Dictionary<string, string> tani = await Task.Run(() => seri.Tanilama());
                    string geri;
                    if (tani != null && tani.TryGetValue("GERI_DONUS", out geri))
                    {
                        int ayrac = geri.IndexOf('|');
                        string neden = ayrac >= 0 ? geri.Substring(ayrac + 1) : geri;
                        return "Yeni kart yazılımı (" + yeniSurum + ") çalışmadı; kart kendiliğinden önceki sürüme (" +
                               calisan + ") döndü, klavye eskisi gibi çalışıyor. Neden: " + neden;
                    }
                    return "Kart yazılımı güncellenemedi: kart hâlâ " + calisan + " sürümünde.";
                }

                // boot.py artık kabul edilir (yeniden başlatma gerekmez; takıp çıkarınca etkili olur)
                string ek = "";
                if (bootSonra && SurumEnAz(calisan, 2, 3))
                {
                    string hata = await gonder(BootDosyasi);
                    ek = hata == null ? " Windows'ta \"Makro Klavye\" adıyla görünmesi için klavyeyi bir kez çıkarıp tak."
                                      : " (" + hata + ")";
                }

                Bildir("Kart yazılımı güncellendi" + (yeniSurum != null && yeniSurum != "?" ? " (" + yeniSurum + ")" : "") + "." + ek);
                return null;
            }
            finally { islemde = false; }
        }

        // Kart yeniden başlarken: bağlanır, kart yeni yazılımı deniyorsa (TANI'da DENEME) onaylanmasını
        // ya da eski sürüme dönmesini bekler. Döner: sonunda çalışan sürüm; bağlanılamazsa null.
        private async Task<string> YeniKartYaziliminiBekle(string beklenen, Action<string, int> ilerleme)
        {
            await Task.Delay(3000);
            DateTime bitis = DateTime.Now.AddSeconds(50);
            string calisan = null;

            while (DateTime.Now < bitis)
            {
                bool bagli = await Task.Run(() => seri.Ping());
                if (!bagli) bagli = await Task.Run(() => seri.Baglan());

                if (bagli)
                {
                    string s = await Task.Run(() => seri.Surum());
                    if (s == null) { await Task.Delay(1500); continue; }   // kart henüz cevap veremedi
                    calisan = s;

                    // Eski yazılım (deneme yok) ya da beklenmeyen sürüm: daha beklenecek bir şey yok
                    if (!SurumEnAz(calisan, 2, 3) || (beklenen != null && beklenen != "?" && calisan != beklenen))
                        return calisan;

                    Dictionary<string, string> tani = await Task.Run(() => seri.Tanilama());
                    if (tani != null && !tani.ContainsKey("DENEME")) return calisan;   // onaylandı
                    ilerleme("Yeni yazılım deneniyor… (kart sorun görürse eski sürüme kendisi döner)", 100);
                }
                else calisan = null;

                await Task.Delay(1500);
            }
            return calisan;
        }

        // Açılışta internette uygulamanın yeni sürümü var mı (açılış başına bir kez)
        private bool uygulamaGuncellemesiSoruldu;

        // elle: tepsideki "Güncellemeleri denetle" (atlanan sürüm de sorulur). Döner: yeni sürüm var mıydı
        private async Task<bool> UygulamaGuncellemesiniSor(bool elle = false)
        {
            if ((uygulamaGuncellemesiSoruldu && !elle) || !UygulamaGuncelleme.Ayarli) return false;
            uygulamaGuncellemesiSoruldu = true;

            UzaktakiUygulama u = await UygulamaGuncelleme.DenetleAsync(elle);
            if (u == null || !UygulamaGuncelleme.DahaYeni(u.Surum)) return false;
            if (!elle && u.Surum == UygulamaAyarlari.UygulamaAtlananSurum) return false;
            if (elle) PencereyiGoster();
            if (!Visible || islemde) return true;

            // "Sormadan kur" açıksa (ve kaydedilmemiş değişiklik yoksa) soru yok; yenilikler kurulunca gösterilir
            bool sormadan = !elle && UygulamaAyarlari.OtomatikUygulamaGuncelle && !DegisiklikVar();
            DialogResult c = sormadan ? DialogResult.Yes : MessageBox.Show(this,
                "Makro Klavye'nin yeni sürümü çıktı: " + u.Surum + "  (bu: " + UygulamaGuncelleme.Surum + ")\n\n" +
                (string.IsNullOrEmpty(u.Not) ? "" : "Yenilikler: " + u.Not + "\n\n") +
                "Şimdi güncellensin mi? Uygulama kapanıp yeni sürümle açılır; ayarların, profillerin ve makroların korunur.\n\n" +
                "Hayır dersen bu sürüm için tekrar sorulmaz; Ayarlar > Uygulama'dan güncelleyebilirsin.",
                "Uygulama güncellemesi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (c != DialogResult.Yes)
            {
                UygulamaAyarlari.UygulamaAtlananSurum = u.Surum;
                return true;
            }
            if (!await DegisiklikleriSor()) return true;
            if (UygulamaGuncelleme.UygulamaGuncelleniyor) return true;

            UygulamaGuncelleme.UygulamaGuncelleniyor = true;
            try
            {
                Bildir("Makro Klavye " + u.Surum + " indiriliyor...");
                byte[] exe = await UygulamaGuncelleme.IndirAsync(u);

                // Kart güncellemesi sürüyorsa önce o bitsin (uygulama kapanınca yarıda kalmasın)
                if (!await UygulamaGuncelleme.KartGuncellemesiniBekle(
                        () => Bildir("Kart güncellemesinin bitmesi bekleniyor, sonra uygulama güncellenecek...")))
                    throw new InvalidOperationException("Kart güncellemesi bitmedi, uygulama güncellemesi sonraya bırakıldı.");

                UygulamaGuncelleme.Kur(exe, u.Surum, u.Not);
            }
            catch (Exception ex)
            {
                UygulamaGuncelleme.UygulamaGuncelleniyor = false;
                Bildir("Güncellenemedi: " + ex.Message);
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari, "Uygulama güncellenemedi: " + ex.Message);
            }
            return true;
        }

        // Güncellemeyle yeni sürüm ilk açıldığında bir kez: "1.2'ye güncellendi" + yayın notu
        private bool YenilikleriGoster()
        {
            string bekleyen = UygulamaAyarlari.BekleyenYenilik;
            if (bekleyen.Length == 0) return false;
            UygulamaAyarlari.BekleyenYenilik = "";

            int ayrac = bekleyen.IndexOf('|');
            string surum = ayrac >= 0 ? bekleyen.Substring(0, ayrac) : bekleyen;
            string not = ayrac >= 0 ? bekleyen.Substring(ayrac + 1) : "";
            if (surum != UygulamaGuncelleme.Surum) return false;   // güncelleme yarım kaldıysa gösterme

            MessageBox.Show(this, "Makro Klavye " + surum + " sürümüne güncellendi." +
                                  (not.Length > 0 ? "\n\nYenilikler: " + not : ""),
                            "Güncellendi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        // Tepsi menüsü: uygulama ve kart için internete şimdi bak
        private async void GuncellemeleriDenetle()
        {
            if (!UygulamaGuncelleme.Ayarli && !KartGuncelleme.Ayarli) return;

            if (tepsi != null && tepsi.Visible && !Visible)
                tepsi.ShowBalloonTip(2000, "Makro Klavye", "Güncellemeler denetleniyor…", ToolTipIcon.Info);

            if (await UygulamaGuncellemesiniSor(true)) return;
            bool kartYeni = klavyeBagli && await KartGuncellemesiniSor(true);
            if (kartYeni) return;

            string kart = null;
            if (klavyeBagli && await IslemSirasiniBekle())
            {
                islemde = true;
                try { kart = await Task.Run(() => seri.Surum()); }
                finally { islemde = false; }
            }

            string mesaj = "Her şey güncel: uygulama " + UygulamaGuncelleme.Surum +
                           (kart != null ? ", kart yazılımı " + kart : (klavyeBagli ? "" : " (kart için klavyeyi bağla)")) + ".";
            if (Visible) Bildir(mesaj);
            else if (tepsi != null && tepsi.Visible) tepsi.ShowBalloonTip(3000, "Makro Klavye", mesaj, ToolTipIcon.Info);
        }

        // Windows Türkçe Q / F kullanıyor ama kart başka düzende yazıyorsa bir kez sorar (Q: kart 2.0+, F: 2.3+)
        private async Task DuzenSor()
        {
            if (UygulamaAyarlari.DuzenSoruldu || !klavyeBagli || !Visible) return;

            // Windows Türkçe Q ya da F, kart başka düzende yazıyorsa
            string hedef = KlavyeDuzeni.WindowsKodu(KlavyeDuzeni.Windows());
            if (hedef == null || hedef == "US" || hedef == KlavyeDuzeni.Kod || !await IslemSirasiniBekle()) return;

            string surum;
            islemde = true;
            try { surum = await Task.Run(() => seri.Surum()); }
            finally { islemde = false; }

            if (!SurumEnAz(surum, 2, hedef == "TRF" ? 3 : 0)) return;   // Türkçe F: kart 2.3+

            string ad = KlavyeDuzeni.Ad(hedef);
            UygulamaAyarlari.DuzenSoruldu = true;
            if (MessageBox.Show(this,
                    "Bilgisayarında " + ad + " klavye kullanılıyor ama klavye metinleri " + KlavyeDuzeni.Adi +
                    " düzenine göre yazıyor. Bu yüzden metin ve makrolarda bazı harf ve işaretler yanlış çıkabilir.\n\n" +
                    "Klavye de " + ad + "'ye göre yazsın mı? (Sonra Ayarlar > Kart > KLAVYE AYARLARI'ndan değiştirebilirsin.)",
                    "Klavye düzeni", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string hata = await ((IAyarlarSahibi)this).KartAyarlariniYaz(
                new Dictionary<string, string> { { "KLAVYE_DUZENI", hedef } }, "Klavye düzeni", ad);
            if (hata == null)
            {
                KlavyeDuzeni.Kod = hedef;
                Bildir("Klavye artık " + ad + " düzenine göre yazıyor; metinlerde Türkçe harf kullanabilirsin.");
            }
        }

        // Bağlanınca kartın sağlığına bir kez bakar; ciddi bir sorun varsa haber verir.
        // Aynı uyarı uygulama açık kaldıkça bir kez gösterilir.
        private readonly HashSet<string> gosterilenUyarilar = new HashSet<string>();

        private async Task SaglikKontrolu()
        {
            if (!klavyeBagli || !await IslemSirasiniBekle()) return;

            Dictionary<string, string> bilgi;
            islemde = true;
            try
            {
                // Eski kart yazılımı TANI bilmez: cevap beklemekle vakit kaybetmemek için önce sürüme bak
                string surum = await Task.Run(() => seri.Surum());
                Version v;
                if (surum == null || !Version.TryParse(surum, out v) || v < new Version(1, 8)) return;
                bilgi = await Task.Run(() => seri.Tanilama());
            }
            finally { islemde = false; }

            if (bilgi == null) return;

            List<string> uyarilar;
            KartTanilamaForm.Satirlar(bilgi, out uyarilar);
            List<string> yeni = uyarilar.Where(u => gosterilenUyarilar.Add(u)).ToList();
            if (yeni.Count == 0) return;

            foreach (string u in yeni) UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari, "Kart: " + u);

            string mesaj = string.Join(" ", yeni);
            Bildir(mesaj + " (Ayrıntı: Ayarlar > Kart > Tanılama)");
            if (tepsi != null && tepsi.Visible && (!Visible || WindowState == FormWindowState.Minimized))
                tepsi.ShowBalloonTip(6000, "Makro Klavye", mesaj, ToolTipIcon.Warning);
        }

        // Bağlanınca internette daha yeni kart yazılımı var mı (uygulama açık kaldıkça bir kez sorulur)
        private bool kartGuncellemesiSoruldu;

        // elle: tepsideki "Güncellemeleri denetle". Döner: yeni sürüm var mıydı
        private async Task<bool> KartGuncellemesiniSor(bool elle = false)
        {
            // Uygulama güncelleniyorsa kart güncellemesi yeni sürüm açılınca yapılır
            if (UygulamaGuncelleme.UygulamaGuncelleniyor || UygulamaGuncelleme.KartGuncelleniyor) return false;

            if ((kartGuncellemesiSoruldu && !elle) || !KartGuncelleme.Ayarli || !klavyeBagli) return false;
            kartGuncellemesiSoruldu = true;

            UzaktakiSurum internet = await KartGuncelleme.DenetleAsync(elle);
            if (internet == null || !klavyeBagli || !await IslemSirasiniBekle()) return false;

            string kart;
            islemde = true;
            try { kart = await Task.Run(() => seri.Surum()); }
            finally { islemde = false; }

            if (!KartGuncelleme.DahaYeni(internet.Surum, kart)) return false;
            if (!elle && internet.Surum == UygulamaAyarlari.KartAtlananSurum) return false;

            UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBilgi, "İnternette yeni kart yazılımı var: " + internet.Surum);
            if (elle) PencereyiGoster();

            // Tepsideyken (pencere kapalı) soru açılmaz, yalnızca haber verilir
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                if (tepsi != null && tepsi.Visible)
                    tepsi.ShowBalloonTip(5000, "Makro Klavye",
                        "Kart yazılımı " + internet.Surum + " çıktı. Güncellemek için pencereyi aç: Ayarlar > Kart.", ToolTipIcon.Info);
                return true;
            }

            bool sormadan = !elle && UygulamaAyarlari.OtomatikKartGuncelle;
            DialogResult c = sormadan ? DialogResult.Yes : MessageBox.Show(this,
                "Kart yazılımının yeni sürümü çıktı: " + internet.Surum + "  (klavyedeki: " + (kart ?? "eski") + ")\n\n" +
                (string.IsNullOrEmpty(internet.Not) ? "" : "Yenilikler: " + internet.Not + "\n\n") +
                "Şimdi indirilip klavyeye yüklensin mi? Tuş ayarların korunur, işlem yarım dakika kadar sürer.\n\n" +
                "Hayır dersen bu sürüm için tekrar sorulmaz; istediğin zaman Ayarlar > Kart'tan güncelleyebilirsin.",
                "Kart yazılımı güncellemesi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (c != DialogResult.Yes)
            {
                UygulamaAyarlari.KartAtlananSurum = internet.Surum;
                return true;
            }

            if (karttaUzunSurenIslemVar) return true;
            karttaUzunSurenIslemVar = true;
            try
            {
                Bildir("Kart yazılımı " + internet.Surum + " indiriliyor...");
                Dictionary<string, byte[]> paket;
                try { paket = await KartGuncelleme.IndirAsync(internet); }
                catch (Exception ex)
                {
                    Bildir("Kart yazılımı indirilemedi: " + ex.Message);
                    UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari, "Kart yazılımı indirilemedi: " + ex.Message);
                    return true;
                }

                string hata = await ((IAyarlarSahibi)this).KartiGuncelle(paket, (metin, yuzde) => Bildir(metin));
                Bildir(hata == null ? "Kart yazılımı " + internet.Surum + " sürümüne güncellendi." : hata);
            }
            finally { karttaUzunSurenIslemVar = false; }
            return true;
        }

        private bool karttaUzunSurenIslemVar;
    }
}
