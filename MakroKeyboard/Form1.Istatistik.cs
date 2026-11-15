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
    // Form1'in parçası: Kullanım istatistikleri ve ısı haritası
    public partial class Form1
    {
        // =================================================================
        //  KULLANIM İSTATİSTİKLERİ
        // =================================================================
        private void IstatistikArtir(string anahtar)
        {
            int sayi;
            istatistik.TryGetValue(anahtar, out sayi);
            istatistik[anahtar] = sayi + 1;
            istatistikDegisti = true;
        }

        private void IstatistikKaydet()
        {
            if (!istatistikDegisti) return;
            istatistikDegisti = false;
            UygulamaAyarlari.IstatistikYaz(istatistik);
        }

        private int Sayac(string anahtar)
        {
            int sayi;
            return istatistik.TryGetValue(anahtar, out sayi) ? sayi : 0;
        }

        private List<IstatistikSatiri> IstatistikSatirlari()
        {
            List<IstatistikSatiri> satirlar = new List<IstatistikSatiri>();

            for (int i = 0; i < atamalar.Count; i++)
            {
                int kisa = Sayac("BUTTON" + i + "_SHORT");
                int uzun = Sayac("BUTTON" + i + "_LONG");
                int cift = Sayac("BUTTON" + i + "_DOUBLE");

                List<string> detay = new List<string>();
                if (uzun > 0) detay.Add("uzun " + uzun);
                if (cift > 0) detay.Add("çift " + cift);

                satirlar.Add(new IstatistikSatiri
                {
                    Ad = i < atamalar.Count - 1 ? "BUTON " + (i + 1) : "ENCODER (bas)",
                    Aciklama = AdBul(atamalar[i].Kisayol),
                    Detay = detay.Count > 0 ? "kısa " + kisa + " · " + string.Join(" · ", detay) : "",
                    Sayi = kisa + uzun + cift
                });
            }

            int sag = Sayac("ENC_CW"), sol = Sayac("ENC_CCW");
            satirlar.Add(new IstatistikSatiri
            {
                Ad = "ENCODER (çevir)",
                Aciklama = "Çevirme adımları",
                Detay = (sag + sol) > 0 ? "sağa " + sag + " · sola " + sol : "",
                Sayi = sag + sol
            });

            return satirlar;
        }

        // Karttaki ayarları profil / yedek metnine çevirir (bağlı değilse null).
        // Uygulama kısayolları "#APPn=hedef|ad" satırlarıyla saklanır (kart bunları yorum sayar).
        private string ProfilMetni()
        {
            if (!klavyeBagli || kartAyarlari.Count == 0) return null;

            StringBuilder sb = new StringBuilder();
            sb.Append("# Makro Klavye profili - ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append('\n');

            // PC_ ile başlayanlar klavyede taşınan kopyadır; profilde #APP / #AD satırları olarak durur
            foreach (var kv in kartAyarlari.Where(k => !k.Key.StartsWith(TasinanOnek))
                                           .OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');

            foreach (var kv in MevcutUygulamalar().OrderBy(k => k.Key))
                sb.Append("#APP").Append(kv.Key).Append('=').Append(kv.Value.Key).Append('|')
                  .Append(kv.Value.Value).Append('\n');

            // Tuş adları ve renkleri: "#AD3=Toplantı sesi", "#RENK3=DB2777" (kart bunları da yorum sayar)
            sb.Append(UygulamaAyarlari.TusAdSatirlari(tusAdlari, "#"));

            return sb.ToString();
        }

        // .lnk'nin açtığı şey: Store uygulamasında shell:AppsFolder\..., normal programda .exe yolu
        private static string LnkHedefi(string lnk)
        {
            try
            {
                return KisayolHedefi((IWshShortcut)new WshShell().CreateShortcut(lnk));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Store uygulaması ve web sitesi explorer.exe'ye argüman olarak verilir; diğerleri doğrudan hedeftir
        private static string KisayolHedefi(IWshShortcut k)
        {
            if (!string.IsNullOrEmpty(k.Arguments) &&
                (k.Arguments.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) || WebAdresiMi(k.Arguments)))
                return k.Arguments;

            return string.IsNullOrEmpty(k.TargetPath) ? null : k.TargetPath;
        }

        private static bool WebAdresiMi(string hedef)
        {
            return hedef != null &&
                   (hedef.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    hedef.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        // Hedefin türü: tuş açıklamalarında "Klasör: İndirilenler" gibi gösterilir
        private static string HedefTuru(string hedef)
        {
            if (string.IsNullOrEmpty(hedef)) return "Uygulama";
            if (WebAdresiMi(hedef)) return "Web sitesi";
            if (hedef.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) return "Uygulama";
            if (hedef.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Uygulama";

            try { if (Directory.Exists(hedef)) return "Klasör"; }
            catch (Exception) { }

            return "Dosya";
        }

        private static void ProfilCoz(string metin, Dictionary<string, string> kart,
                                      Dictionary<int, KeyValuePair<string, string>> uygulamaHedefleri,
                                      Dictionary<int, TusAdRenk> gorunumler)
        {
            Dictionary<string, string> tasinan = new Dictionary<string, string>();

            foreach (string ham in metin.Split('\n'))
            {
                string satir = ham.Trim();
                if (satir.Length == 0) continue;

                Match m = Regex.Match(satir, @"^#APP(\d)=(.+)\|(.+)$");
                if (m.Success)
                {
                    uygulamaHedefleri[int.Parse(m.Groups[1].Value)] =
                        new KeyValuePair<string, string>(m.Groups[2].Value, m.Groups[3].Value);
                    continue;
                }

                if (satir.StartsWith("#"))
                {
                    UygulamaAyarlari.TusAdSatiriIsle(satir, "#", gorunumler);   // #ADn / #RENKn
                    continue;
                }

                int eq = satir.IndexOf('=');
                if (eq <= 0) continue;

                string anahtar = satir.Substring(0, eq).Trim();
                if (anahtar.StartsWith(TasinanOnek))
                    tasinan[anahtar] = satir.Substring(eq + 1).Trim();
                else if (Regex.IsMatch(anahtar, @"^[A-Z][A-Z0-9_]*$"))
                    kart[anahtar] = satir.Substring(eq + 1).Trim();
            }

            // Profilde #APP / #AD satırı yok ama klavyede taşınan kopya varsa onu kullan
            if (uygulamaHedefleri.Count == 0 && gorunumler.Count == 0)
            {
                Dictionary<int, KeyValuePair<string, string>> u;
                Dictionary<int, TusAdRenk> a;
                if (TasinanCoz(tasinan, out u, out a))
                {
                    foreach (var kv in u) uygulamaHedefleri[kv.Key] = kv.Value;
                    foreach (var kv in a) gorunumler[kv.Key] = kv.Value;
                }
            }
        }

        // Profil / yedek metnini karta yazar, uygulama kısayollarını kurar. null = başarılı
        private static readonly string[] UykuAnahtarlari = { "UYKU_ENGEL", "UYKU_ARALIK" };

        private async Task<string> ProfilUygulaAsync(string metin, string geriAlAciklama = "Profil / yedek yükleme")
        {
            if (!klavyeBagli) return "Klavye bağlı değil.";
            if (!await IslemSirasiniBekle()) return "Başka bir işlem sürüyor, biraz sonra tekrar dene.";

            Dictionary<string, string> kart = new Dictionary<string, string>();
            Dictionary<int, KeyValuePair<string, string>> uygulamaHedefleri =
                new Dictionary<int, KeyValuePair<string, string>>();
            Dictionary<int, TusAdRenk> gorunumler = new Dictionary<int, TusAdRenk>();
            ProfilCoz(metin, kart, uygulamaHedefleri, gorunumler);

            if (!kart.Keys.Any(k => k.StartsWith("BUTTON")))
                return "Bu dosyada Makro Klavye ayarı bulunamadı.";

            // Uygulama kısayolları ve tuş adları klavyede de saklansın (ayrı bir kayıt gerekmesin)
            foreach (var kv in TasinanAnahtarlar(uygulamaHedefleri, gorunumler)) kart[kv.Key] = kv.Value;

            // Uyku engelleyici profile değil bilgisayara bağlı: profil / yedek değişince olduğu gibi kalır
            foreach (string k in UykuAnahtarlari)
            {
                string deger;
                if (kartAyarlari.TryGetValue(k, out deger)) kart[k] = deger;
                else kart.Remove(k);
            }

            string hata;
            Dictionary<string, string> okunan = null;

            GeriAlNoktasi(geriAlAciklama);

            islemde = true;
            try
            {
                hata = await Task.Run(() => seri.Kaydet(kart));
                if (hata == null) okunan = await Task.Run(() => seri.Oku());
            }
            finally { islemde = false; }

            if (hata != null)
            {
                GeriAlIptal();
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari, geriAlAciklama + " klavyeye yazılamadı: " + hata);
                if (hata.IndexOf("Read-only", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Kart salt okunur. boot.py'deki storage.remount satırını kontrol et.";
                if (!seri.Bagli) DurumGuncelle(false);
                return "Yazılamadı: " + hata;
            }

            string kisayolHatasi = null;
            try { ProfilUygulamalariniKur(uygulamaHedefleri); }
            catch (Exception ex) { kisayolHatasi = ex.Message; }

            // Profildeki tuş adları / renkleri (profilde yoksa adlar temizlenir)
            tusAdlari = gorunumler;
            UygulamaAyarlari.TusAdlariYaz(tusAdlari);

            AyarlariYukle(okunan ?? kart);
            OtomatikYedek(geriAlAciklama);

            return kisayolHatasi == null
                ? null
                : "Ayarlar yüklendi ama uygulama kısayolları kurulamadı: " + kisayolHatasi;
        }

        // Profildeki uygulamalar için .lnk'leri yeniden kurar, profilde olmayan eski uygulama kısayollarını siler
        private void ProfilUygulamalariniKur(Dictionary<int, KeyValuePair<string, string>> uygulamaHedefleri)
        {
            UygulamalariTara();

            for (int i = 0; i < atamalar.Count; i++)
            {
                string kisayol = AyrilmisKisayol(i);
                string mevcutLnk;
                uygulamalar.TryGetValue(kisayol, out mevcutLnk);

                if (mevcutLnk != null) System.IO.File.Delete(mevcutLnk);

                KeyValuePair<string, string> uyg;
                if (uygulamaHedefleri.TryGetValue(i, out uyg))
                    KisayolDosyasiOlustur(uyg.Key, uyg.Value, kisayol);
            }

            UygulamalariTara();
        }

        // =================================================================
        //  ISI HARİTASI
        // =================================================================
        private void IsiHaritasiDegistir()
        {
            isiHaritasi = !isiHaritasi;
            try { UygulamaAyarlari.IsiHaritasi = isiHaritasi; }
            catch (Exception) { }

            IsiDugmesiGorunum();
            KutuculariYenile();
            KutucukStilleri();
            Bildir(isiHaritasi ? IsiOzeti() : VarsayilanBilgi);
        }

        private void IsiDugmesiGorunum()
        {
            if (btnIsi == null) return;
            btnIsi.DolguRengi = isiHaritasi ? Tema.Secili : Tema.Kart;
            btnIsi.KenarRengi = isiHaritasi ? Mavi : Tema.Kenar2;
            btnIsi.HoverRengi = isiHaritasi ? Color.Empty : Tema.Hover;
            btnIsi.Invalidate(true);
        }

        // Bir tuşun toplam kullanımı (encoder'da basma + çevirme adımları)
        private int TusKullanimi(int i)
        {
            int toplam = Sayac("BUTTON" + i + "_SHORT") + Sayac("BUTTON" + i + "_LONG") + Sayac("BUTTON" + i + "_DOUBLE");
            if (i == atamalar.Count - 1) toplam += Sayac("ENC_CW") + Sayac("ENC_CCW");
            return toplam;
        }

        private int EnCokKullanim()
        {
            int enCok = 0;
            for (int i = 0; i < atamalar.Count; i++) enCok = Math.Max(enCok, TusKullanimi(i));
            return enCok;
        }

        private string KullanimYazisi(int i)
        {
            if (i == atamalar.Count - 1)
            {
                int cevirme = Sayac("ENC_CW") + Sayac("ENC_CCW");
                return (TusKullanimi(i) - cevirme) + " basış · " + cevirme + " çevirme";
            }
            return TusKullanimi(i) + " basış";
        }

        // Az kullanılan: normal zemin, çok kullanılan: turuncu
        private static Color IsiRengiHesapla(double oran)
        {
            Color sicak = Tema.Koyu ? Color.FromArgb(160, 64, 20) : Color.FromArgb(255, 160, 96);
            double t = Math.Sqrt(Math.Max(0, Math.Min(1, oran)));   // az kullanılan da görünür olsun

            return Color.FromArgb(
                (int)(KutuDolgu.R + (sicak.R - KutuDolgu.R) * t),
                (int)(KutuDolgu.G + (sicak.G - KutuDolgu.G) * t),
                (int)(KutuDolgu.B + (sicak.B - KutuDolgu.B) * t));
        }

        private Color IsiRengi(int i, int enCok)
        {
            return enCok <= 0 ? KutuDolgu : IsiRengiHesapla(TusKullanimi(i) / (double)enCok);
        }

        private void IsiYazisiGuncelle(int n)
        {
            if (isiHaritasi && n >= 0 && n < kutucuklar.Count)
                kutucuklar[n].Kisayol.Text = KullanimYazisi(n);
        }

        private string IsiOzeti()
        {
            int enCok = EnCokKullanim();
            if (enCok == 0)
                return "Isı haritası açık. Henüz sayım yok; uygulama açıkken tuşlara bastıkça renklenir.";

            int en = Enumerable.Range(0, atamalar.Count).First(i => TusKullanimi(i) == enCok);
            return "Isı haritası: en çok " + TusEtiketi(en) + " kullanılmış (" + enCok + "). " +
                   "Turuncu = çok, açık = az.";
        }

        // Listeden hazır komut seçilince kısayol kutusunu doldur
        private void ComboDegisti(object sender, EventArgs e)
        {
            if (yukleniyor) return;

            int n = cmbUygulama.SelectedIndex;
            int hazir = Hazirlar.GetLength(0);

            if (metinModu) metinHam = txtKisayol.Text;

            if (n == hazir + 3)                       // Makro
            {
                MakroAc();
                return;
            }

            if (n == hazir + 2)                       // Uygulama
            {
                UygulamaSec();
                return;
            }

            bekleyenExe = null;

            if (n == hazir + 1)                       // Metin Yaz
            {
                if (!metinModu)
                {
                    metinGizli = false;
                    GosterMetin();
                }
                txtKisayol.Focus();
                return;
            }

            komutHam = n < hazir ? Hazirlar[n, 1] : "";   // hazır komut ya da Özel Kısayol
            GosterKisayol();

            if (komutHam.Length == 0) YakalamayiAc();  // Özel: hemen kombinasyona basılabilsin
        }

        private static void SekmeStil(YuvarlakPanel p, Label l, bool aktif)
        {
            p.DolguRengi = aktif ? Mavi : Tema.Kart;
            p.KenarRengi = aktif ? Mavi : Tema.Kenar;
            p.HoverRengi = aktif ? Color.Empty : Tema.Hover;
            l.ForeColor = aktif ? Color.White : Lacivert;
            p.Invalidate(true);
        }

        // Kısayol görünümü: kutu salt okunur, tuşlara basınca kombinasyon yakalanır
        private void GosterKisayol()
        {
            ModAyarla(false);
            yukleniyor = true;
            txtKisayol.Text = komutHam == "NONE" ? "Boş (bir şey yapmaz)" : Guzel(komutHam);
            cmbUygulama.SelectedIndex = HazirIndeks(komutHam);
            yukleniyor = false;
            KutuGorunumu();
        }

        // Metin görünümü: kutuya düz yazı yazılır, TEXT: kaydederken eklenir
        private void GosterMetin()
        {
            ModAyarla(true);
            yukleniyor = true;
            txtKisayol.Text = metinHam;
            cmbUygulama.SelectedIndex = Hazirlar.GetLength(0) + 1;
            yukleniyor = false;
            KaydetDurumu();
        }

        // Uygulama görünümü: seçilen uygulama + otomatik atanan kısayol
        private void GosterUygulama()
        {
            ModAyarla(false, true);
            yukleniyor = true;

            string ad = bekleyenExe != null
                ? bekleyenAd
                : UygulamaAdiBul(komutHam);

            txtKisayol.Text = ad;   // sadece uygulama adı (kısayol arka planda sabit)
            cmbUygulama.SelectedIndex = Hazirlar.GetLength(0) + 2;
            yukleniyor = false;
        }

        // Seçim iptal edilince / hata olunca ekranı mevcut moda göre eski haline getirir
        private void ModuGeriYukle()
        {
            if (metinModu) GosterMetin();
            else if (uygulamaModu) GosterUygulama();
            else if (makroModu) GosterMakro();
            else GosterKisayol();
        }

        private void ModAyarla(bool metin, bool uygulama = false, bool makro = false)
        {
            metinModu = metin;
            uygulamaModu = uygulama;
            makroModu = makro;
            txtKisayol.ReadOnly = !metin;
            txtKisayol.Cursor = metin ? Cursors.IBeam : Cursors.Hand;

            yakalamaAcik = false;
            kanca.Durdur();

            KutuGorunumu();
        }
    }
}
