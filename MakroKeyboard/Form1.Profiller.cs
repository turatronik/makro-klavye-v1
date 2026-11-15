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
    // Form1'in parçası: Ayarlar penceresi, profiller, başlıktaki profil seçici, geçmiş farkı
    public partial class Form1
    {
        // =================================================================
        //  AYARLAR PENCERESİ / PROFİLLER
        // =================================================================
        // Ayarlar artık ana pencerenin bir sekmesi (Form1.Sayfalar.cs); çıkınca ana ekran yenilenir
        private void AyarlariAc()
        {
            SayfaAc(Sayfa.Ayarlar);
        }

        // ---------- IAyarlarSahibi (ayarlar penceresinin kullandıkları) ----------
        bool IAyarlarSahibi.KlavyeBagli
        {
            get { return klavyeBagli; }
        }

        string IAyarlarSahibi.ProfilMetni()
        {
            return ProfilMetni();
        }

        Task<string> IAyarlarSahibi.ProfilUygula(string metin)
        {
            return ProfilUygulaAsync(metin);
        }

        async Task<string> IAyarlarSahibi.KartSurumu()
        {
            if (!klavyeBagli || !await IslemSirasiniBekle()) return null;

            islemde = true;
            try { return await Task.Run(() => seri.Surum()); }
            finally { islemde = false; }
        }

        async Task<string> IAyarlarSahibi.KartYedekSurumu()
        {
            if (!klavyeBagli || !await IslemSirasiniBekle()) return null;
            islemde = true;
            try
            {
                string surum = await Task.Run(() => seri.Surum());
                if (!SurumEnAz(surum, 2, 4)) return null;
                return await Task.Run(() => seri.YedekSurumu());
            }
            finally { islemde = false; }
        }

        async Task<string> IAyarlarSahibi.KartiGeriAl(string beklenen, Action<string, int> ilerleme)
        {
            if (!klavyeBagli) return "Önce klavyeye bağlan.";
            if (UygulamaGuncelleme.KartGuncelleniyor) return "Kart güncellemesi sürüyor.";
            if (!await IslemSirasiniBekle()) return "Başka bir işlem sürüyor, biraz sonra tekrar dene.";

            string hata = null, calisan;
            UygulamaGuncelleme.KartGuncelleniyor = true;
            islemde = true;
            try
            {
                hata = await Task.Run(() => seri.GeriAl());
                if (hata == null)
                {
                    ilerleme("Kart yeniden başlatılıyor…", 80);
                    calisan = await YeniKartYaziliminiBekle(beklenen, ilerleme);
                    if (calisan == null)
                    {
                        DurumGuncelle(false);
                        hata = "Kart yeniden başladıktan sonra bağlanılamadı. BAĞLAN'a bas.";
                    }
                    else
                    {
                        Dictionary<string, string> gelen = await Task.Run(() => seri.Oku());
                        if (gelen != null) AyarlariYukle(gelen);
                        DurumGuncelle(true);
                        if (beklenen != "?" && calisan != beklenen)
                            hata = "Kart " + beklenen + " yerine " + calisan + " sürümüyle açıldı.";
                    }
                }
            }
            finally
            {
                islemde = false;
                UygulamaGuncelleme.KartGuncelleniyor = false;
            }

            UygulamaAyarlari.GunlukEkle(hata == null ? UygulamaAyarlari.GunlukYazma : UygulamaAyarlari.GunlukUyari,
                                        hata == null ? "Kart yazılımı önceki sürüme (" + beklenen + ") döndü"
                                                     : "Kart yazılımı geri alınamadı: " + hata);
            if (hata == null) Bildir("Kart yazılımı " + beklenen + " sürümüne döndü.");
            return hata;
        }

        async Task<string> IAyarlarSahibi.KartiGuncelle(Dictionary<string, byte[]> paket, Action<string, int> ilerleme)
        {
            if (UygulamaGuncelleme.UygulamaGuncelleniyor)
                return "Uygulama güncelleniyor; kart güncellemesi uygulama yeniden açılınca yapılabilir.";
            if (UygulamaGuncelleme.KartGuncelleniyor) return "Kart güncellemesi zaten sürüyor.";

            string surum = KartPaketi.Surum(paket);
            string hata;
            UygulamaGuncelleme.KartGuncelleniyor = true;
            try { hata = await KartiGuncelleAsync(paket, ilerleme); }
            finally { UygulamaGuncelleme.KartGuncelleniyor = false; }

            if (hata == null)
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukYazma,
                                            "Kart yazılımı güncellendi" + (surum != null && surum != "?" ? " (sürüm " + surum + ")" : ""));
            else
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari, "Kart yazılımı güncellenemedi: " + hata);
            return hata;
        }

        string IAyarlarSahibi.KartAyari(string anahtar)
        {
            string deger;
            return klavyeBagli && kartAyarlari.TryGetValue(anahtar, out deger) ? deger : null;
        }

        async Task<string> IAyarlarSahibi.KartAyarlariniYaz(Dictionary<string, string> degerler, string aciklama, string ayrinti)
        {
            if (!klavyeBagli) return "Önce klavyeye bağlan.";
            if (!await IslemSirasiniBekle()) return "Başka bir işlem sürüyor, biraz sonra tekrar dene.";

            Dictionary<string, string> eski = new Dictionary<string, string>(kartAyarlari);
            GeriAlNoktasi(aciklama);
            foreach (var kv in degerler) kartAyarlari[kv.Key] = kv.Value;

            string hata = await KartaYaz();
            if (hata != null)
            {
                kartAyarlari = eski;
                GeriAlIptal();
                HataGoster(hata);
                return hata.IndexOf("Read-only", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Kart salt okunur. boot.py'deki storage.remount satırını kontrol et."
                    : "Yazılamadı: " + hata;
            }

            OtomatikYedek(aciklama, ayrinti);
            KlavyeDuzeni.Kod = KlavyeDuzeni.Coz(Al("KLAVYE_DUZENI", "US"));
            BasisGorunum();
            return null;
        }

        async Task<Dictionary<string, string>> IAyarlarSahibi.KartTanilama()
        {
            if (!klavyeBagli || !await IslemSirasiniBekle()) return null;

            islemde = true;
            try
            {
                Dictionary<string, string> sonuc = await Task.Run(() => seri.Tanilama());
                return sonuc != null && sonuc.Count > 0 ? sonuc : null;
            }
            finally { islemde = false; }
        }

        List<string> IAyarlarSahibi.ProfilFarki(string yedekMetni)
        {
            return ProfilFarki(yedekMetni);
        }

        List<IstatistikSatiri> IAyarlarSahibi.IstatistikSatirlari()
        {
            return IstatistikSatirlari();
        }

        async Task<string> IAyarlarSahibi.FabrikaAyarlari()
        {
            StringBuilder sb = new StringBuilder("# Fabrika ayarları\n");
            for (int i = 0; i < VarsayilanKomutlar.Length; i++)
                sb.Append("BUTTON").Append(i).Append('=').Append(VarsayilanKomutlar[i]).Append('\n');
            sb.Append("ENCODER_CW=VOLUME_UP\nENCODER_CCW=VOLUME_DOWN\n");
            sb.Append("LED_MODE=rainbow\nLED_COLOR=0066FF\nLED_BRIGHTNESS=100\n");
            sb.Append("LED_SPEED=100\nLED_FLOW=").Append(LedAyarForm.VarsayilanAkis).Append('\n');
            sb.Append("KLAVYE_DUZENI=").Append(Al("KLAVYE_DUZENI", "US")).Append('\n');   // bilgisayara bağlı, sıfırlanmaz
            sb.Append("TEXT_DELAY_MS=").Append(Al("TEXT_DELAY_MS", "0")).Append('\n');
            sb.Append("ENCODER_TERS=").Append(Al("ENCODER_TERS", "0")).Append('\n');
            sb.Append("ENCODER_ADIM=").Append(Al("ENCODER_ADIM", "1")).Append('\n');
            sb.Append("ENCODER_TEKRAR=").Append(Al("ENCODER_TEKRAR", "1")).Append('\n');
            sb.Append("TITRESIM_MS=").Append(Al("TITRESIM_MS", "0")).Append('\n');

            string hata = await ProfilUygulaAsync(sb.ToString(), "Fabrika ayarları");
            if (hata == null)
            {
                UygulamaAyarlari.AktifProfil = "";
                ProfilEtiketiGuncelle();
            }
            return hata;
        }

        void IAyarlarSahibi.IstatistikSifirla()
        {
            istatistik.Clear();
            istatistikDegisti = true;
            IstatistikKaydet();

            if (isiHaritasi)
            {
                KutuculariYenile();
                KutucukStilleri();
            }
        }

        // Arka plandaki kısa işlem (bağlantı kontrolü vb.) bitene kadar bekler; boşaldıysa true
        private async Task<bool> IslemSirasiniBekle(int ms = 3000)
        {
            DateTime bitis = DateTime.Now.AddMilliseconds(ms);
            while (islemde && DateTime.Now < bitis) await Task.Delay(50);
            return !islemde;
        }

        // =================================================================
        //  BAŞLIKTAKİ PROFİL SEÇİCİ
        // =================================================================
        private void ProfilEtiketiGuncelle()
        {
            ProfilListesiYenile();
        }

        // Klavyedeki şu anki ayarları profil olarak saklar. ad null ise sorar, verilirse o profili günceller.
        private void ProfilOlarakKaydet(string ad)
        {
            string metin = ProfilMetni();
            if (metin == null)
            {
                Bildir("Önce klavyeye bağlan. Profil, klavyedeki ayarlardan oluşturulur.");
                return;
            }

            if (ad == null)
            {
                ad = GirisKutusu.Sor(this, "Yeni profil",
                    "Klavyedeki şu anki ayarlar (tuşlar, LED, uygulama kısayolları, tuş adları) bu adla saklanır.",
                    "Profil adı (örn. Oyun)", "",
                    a => UygulamaAyarlari.AdTemizle(a).Length == 0 ? "Profil için bir ad yaz." : null);
                if (ad == null) return;

                ad = UygulamaAyarlari.AdTemizle(ad);

                if (System.IO.File.Exists(UygulamaAyarlari.ProfilYolu(ad)) &&
                    MessageBox.Show(this, "\"" + ad + "\" profili zaten var. Üzerine yazılsın mı?", "Profil",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            try
            {
                UygulamaAyarlari.ProfilYaz(ad, metin);
                UygulamaAyarlari.AktifProfil = ad;
                ProfilEtiketiGuncelle();
                Bildir("\"" + ad + "\" profili kaydedildi. Soldaki listeden istediğin zaman geçebilirsin.");
            }
            catch (Exception ex)
            {
                Bildir("Profil kaydedilemedi: " + ex.Message);
            }
        }

        private async void ProfilSec(string ad)
        {
            if (!await DegisiklikleriSor()) return;

            string hata;
            try { hata = await ProfilUygulaAsync(UygulamaAyarlari.ProfilOku(ad), "\"" + ad + "\" profiline geçiş"); }
            catch (Exception ex) { hata = ex.Message; }

            if (hata == null)
            {
                UygulamaAyarlari.AktifProfil = ad;
                ProfilEtiketiGuncelle();
                Bildir("\"" + ad + "\" profili yüklendi.");
            }
            else
            {
                Bildir(hata);
            }
        }

        // =================================================================
        //  GEÇMİŞ: ŞİMDİKİ HÂL -> YEDEK FARKI
        // =================================================================
        // "BUTON 3 (kısa basış): Ekran Görüntüsü → Kopyala" gibi satırlar; bağlı değilse null
        private List<string> ProfilFarki(string yedekMetni)
        {
            string simdiMetni = ProfilMetni();
            if (simdiMetni == null) return null;

            Dictionary<string, string> simdi = new Dictionary<string, string>(), yedek = new Dictionary<string, string>();
            Dictionary<int, KeyValuePair<string, string>> simdiUyg = new Dictionary<int, KeyValuePair<string, string>>(),
                                                           yedekUyg = new Dictionary<int, KeyValuePair<string, string>>();
            Dictionary<int, TusAdRenk> simdiAd = new Dictionary<int, TusAdRenk>(), yedekAd = new Dictionary<int, TusAdRenk>();

            ProfilCoz(simdiMetni, simdi, simdiUyg, simdiAd);
            ProfilCoz(yedekMetni, yedek, yedekUyg, yedekAd);

            List<string> satirlar = new List<string>();

            // Tuşlar ve encoder (kart sırasıyla)
            List<string> anahtarlar = new List<string>();
            for (int i = 0; i < atamalar.Count; i++)
            {
                anahtarlar.Add("BUTTON" + i);
                anahtarlar.Add("BUTTON" + i + "_LONG");
                anahtarlar.Add("BUTTON" + i + "_DOUBLE");
                anahtarlar.Add("BUTTON" + i + "_CW");
                anahtarlar.Add("BUTTON" + i + "_CCW");
            }
            anahtarlar.Add("ENCODER_CW");
            anahtarlar.Add("ENCODER_CCW");

            foreach (string k in anahtarlar)
            {
                string once = FarkDegeri(k, simdi, simdiUyg), sonra = FarkDegeri(k, yedek, yedekUyg);
                if (once != sonra) satirlar.Add(YerAdi(k) + ": " + once + "  →  " + sonra);
            }

            // Tuş adları / renkleri / simgeleri
            for (int i = 0; i < atamalar.Count; i++)
            {
                TusAdRenk a, b;
                simdiAd.TryGetValue(i, out a);
                yedekAd.TryGetValue(i, out b);

                string adA = a != null ? a.Ad : "", adB = b != null ? b.Ad : "";
                if (adA != adB)
                    satirlar.Add(TusEtiketi(i) + " adı: " + (adA.Length > 0 ? "\"" + adA + "\"" : "yok") +
                                 "  →  " + (adB.Length > 0 ? "\"" + adB + "\"" : "yok"));

                string renkA = a != null ? a.Renk : "", renkB = b != null ? b.Renk : "";
                string simgeA = a != null ? a.Simge : "", simgeB = b != null ? b.Simge : "";
                if (renkA != renkB || simgeA != simgeB)
                    satirlar.Add(TusEtiketi(i) + " rengi / simgesi değişir");
            }

            // LED
            string[,] led =
            {
                { "LED_MODE", "LED efekti" }, { "LED_COLOR", "LED rengi" }, { "LED_BRIGHTNESS", "LED parlaklığı" },
                { "LED_SPEED", "LED hızı" }, { "LED_FLOW", "Akış renkleri" },
                { "LONG_PRESS_MS", "Uzun basış süresi" }, { "DOUBLE_PRESS_MS", "Çift basış aralığı" },
                { "TEXT_DELAY_MS", "Harfler arası bekleme" }, { "KLAVYE_DUZENI", "Klavye düzeni" },
                { "ENCODER_TERS", "Encoder yönü ters" }, { "ENCODER_ADIM", "Encoder hassasiyeti (adım)" },
                { "ENCODER_TEKRAR", "Encoder tık başına tekrar" }, { "TITRESIM_MS", "Tuş titreşim filtresi" }
            };
            for (int i = 0; i < led.GetLength(0); i++)
            {
                string once, sonra;
                simdi.TryGetValue(led[i, 0], out once);
                yedek.TryGetValue(led[i, 0], out sonra);
                once = once ?? "";
                sonra = sonra ?? "";
                if (once == sonra) continue;

                if (led[i, 0] == "LED_MODE") { once = LedAyarForm.ModAdi(once); sonra = LedAyarForm.ModAdi(sonra); }
                else if (led[i, 0] == "LED_COLOR") { once = "#" + once; sonra = "#" + sonra; }
                else if (led[i, 0] == "LED_BRIGHTNESS") { once = "%" + once; sonra = "%" + sonra; }
                else if (led[i, 0] == "LED_SPEED") { once = "%" + once; sonra = "%" + sonra; }
                else if (led[i, 0].EndsWith("_MS"))
                {
                    once = once.Length > 0 ? once + " ms" : "varsayılan";
                    sonra = sonra.Length > 0 ? sonra + " ms" : "varsayılan";
                }

                satirlar.Add(led[i, 1] + ": " + (once.Length > 1 ? once : "—") + "  →  " + (sonra.Length > 1 ? sonra : "—"));
            }

            return satirlar;
        }

        // Fark listesinde bir atamanın okunur hâli (uygulama kısayolunda uygulamanın adı)
        private string FarkDegeri(string anahtar, Dictionary<string, string> ayar,
                                  Dictionary<int, KeyValuePair<string, string>> uyg)
        {
            string komut;
            if (!ayar.TryGetValue(anahtar, out komut) || komut.Length == 0)
            {
                if (anahtar == "ENCODER_CW") komut = "VOLUME_UP";
                else if (anahtar == "ENCODER_CCW") komut = "VOLUME_DOWN";
                else return "—";
            }

            Match m = Regex.Match(anahtar, @"^BUTTON(\d)$");
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value);
                KeyValuePair<string, string> u;
                if (HotkeyNorm(komut) == AyrilmisKisayol(n) && uyg.TryGetValue(n, out u))
                    return HedefTuru(u.Key) + ": " + u.Value;
            }

            return ListeMetni(komut);
        }

        // Kısayolun Windows'taki anlamı (bilinmiyorsa null). tehlikeli: kapatır, kilitler, siler...
        internal static string WindowsAnlami(string komut, out bool tehlikeli)
        {
            tehlikeli = false;
            string anlam;
            if (string.IsNullOrEmpty(komut) || !WindowsKisayollari.TryGetValue(HotkeyNorm(komut), out anlam))
                return null;

            if (anlam.StartsWith("!"))
            {
                tehlikeli = true;
                anlam = anlam.Substring(1);
            }
            return anlam;
        }

        // "CTRL+ALT+C" -> "Ctrl + Alt + C"; tek parçalı komutlar olduğu gibi kalır
        internal static string Guzel(string komut)
        {
            if (komut.IndexOf('+') < 0) return komut;

            string[] p = komut.Split('+');
            for (int i = 0; i < p.Length; i++)
                if (p[i].Length > 1) p[i] = p[i].Substring(0, 1) + p[i].Substring(1).ToLower();

            return string.Join(" + ", p);
        }

        private static string GosterimKisayol(string komut)
        {
            if (komut == "NONE") return "Bir şey yapmaz";
            if (komut.StartsWith("TEXT:"))
                return UygulamaAyarlari.GizliyseMaskele(komut.Substring(5), MetinCoz(komut.Substring(5)));
            if (komut.StartsWith("MACRO:")) return MakroForm.Ozet(komut, Hazirlar);
            return komut.Replace("+", " + ");
        }
    }
}
