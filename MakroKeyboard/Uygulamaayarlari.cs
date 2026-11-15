using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MakroKeyboard
{
    // Bilgisayardaki uygulama ayarları (karttan bağımsız):
    //   %APPDATA%\MakroKeyboard\ayarlar.ini      -> TEPSI, AKTIF_PROFIL, PENCERE, ...
    //   %APPDATA%\MakroKeyboard\tuslar.ini       -> tuş adları ve renkleri
    //   %APPDATA%\MakroKeyboard\Profiller\*.txt  -> kayıtlı profiller
    //   %APPDATA%\MakroKeyboard\Gecmis\*.txt     -> otomatik yedekler (son 20)
    //   %APPDATA%\MakroKeyboard\Gecmis\<kimlik>\  -> birden çok klavyede her klavyenin kendi yedekleri
    //   %APPDATA%\MakroKeyboard\Makrolar\*.txt   -> Makrolarım
    //   %APPDATA%\MakroKeyboard\gunluk.txt        -> olay günlüğü (son 500 satır)
    //   HKCU\...\Run\MakroKlavye                 -> Windows ile başlat
    public static class UygulamaAyarlari
    {
        private const string RunAnahtari = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunDegeri = "MakroKlavye";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string Klasor
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MakroKeyboard");
            }
        }

        public static string ProfilKlasoru
        {
            get { return Path.Combine(Klasor, "Profiller"); }
        }

        private static string AyarDosyasi
        {
            get { return Path.Combine(Klasor, "ayarlar.ini"); }
        }

        // ---------------------------------------------------------
        // ayarlar.ini
        // ---------------------------------------------------------
        private static Dictionary<string, string> Oku()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();

            try
            {
                if (!File.Exists(AyarDosyasi)) return d;

                foreach (string ham in File.ReadAllLines(AyarDosyasi, Utf8))
                {
                    int eq = ham.IndexOf('=');
                    if (eq > 0) d[ham.Substring(0, eq).Trim()] = ham.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }

            return d;
        }

        private static void Yaz(string anahtar, string deger)
        {
            if (VeriKilitli) return;

            Dictionary<string, string> d = Oku();
            d[anahtar] = deger;

            Directory.CreateDirectory(Klasor);
            File.WriteAllLines(AyarDosyasi, d.Select(kv => kv.Key + "=" + kv.Value), Utf8);
        }

        private static string Al(string anahtar)
        {
            string deger;
            return Oku().TryGetValue(anahtar, out deger) ? deger : "";
        }

        // Kapatınca sistem tepsisinde çalışmaya devam et
        public static bool Tepsi
        {
            get { return Al("TEPSI") == "1"; }
            set { Yaz("TEPSI", value ? "1" : "0"); }
        }

        // Etkin profil her klavye için ayrı tutulur
        public static string AktifProfil
        {
            get { return Al(KlavyeAnahtari("AKTIF_PROFIL")); }
            set { Yaz(KlavyeAnahtari("AKTIF_PROFIL"), value ?? ""); }
        }

        // ---------------------------------------------------------
        // Birden çok klavye. Kart 2.4+ işlemcisinin seri numarasını (kimlik) söyler;
        // her klavyenin adı, geçmişi (otomatik yedekleri) ve etkin profili ayrı tutulur.
        // Kimliğini söylemeyen eski kart yazılımında kimlik "" (ortak).
        // ---------------------------------------------------------
        private static string aktifKlavye;

        // Bağlı ya da en son bağlanan klavye
        public static string AktifKlavye
        {
            get
            {
                if (aktifKlavye == null) aktifKlavye = GecerliKimlik(Al("SON_KLAVYE"));
                return aktifKlavye;
            }
        }

        public static string GecerliKimlik(string kimlik)
        {
            kimlik = (kimlik ?? "").Trim().ToUpperInvariant();
            return System.Text.RegularExpressions.Regex.IsMatch(kimlik, "^[0-9A-F]{4,40}$") ? kimlik : "";
        }

        private static string KlavyeAnahtari(string anahtar)
        {
            return AktifKlavye.Length > 0 ? anahtar + "_" + AktifKlavye : anahtar;
        }

        public static List<string> Klavyeler()
        {
            return Al("KLAVYELER").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(GecerliKimlik).Where(k => k.Length > 0).Distinct().ToList();
        }

        // Verilen ad, yoksa "Makro Klavye", "Makro Klavye 2" ...
        public static string KlavyeAdi(string kimlik)
        {
            kimlik = GecerliKimlik(kimlik);
            if (kimlik.Length == 0) return "Makro Klavye";
            string ad = Al("KLAVYE_ADI_" + kimlik);
            if (ad.Length > 0) return ad;
            int sira = Klavyeler().IndexOf(kimlik);
            return sira <= 0 ? "Makro Klavye" : "Makro Klavye " + (sira + 1);
        }

        public static void KlavyeAdiYaz(string kimlik, string ad)
        {
            kimlik = GecerliKimlik(kimlik);
            if (kimlik.Length == 0) return;
            Yaz("KLAVYE_ADI_" + kimlik, (ad ?? "").Replace("=", "").Replace("\r", "").Replace("\n", "").Trim());
        }

        // Klavyenin daha önce bağlandığı seri portlar (en yeni önce, en fazla 5): bağlanırken önce bunlar denenir
        public static List<string> BilinenPortlar()
        {
            return Al("BILINEN_PORTLAR").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
        }

        public static void PortuHatirla(string port)
        {
            if (string.IsNullOrEmpty(port)) return;
            List<string> l = BilinenPortlar();
            if (l.Count > 0 && string.Equals(l[0], port, StringComparison.OrdinalIgnoreCase)) return;   // zaten başta
            l.RemoveAll(p => string.Equals(p, port, StringComparison.OrdinalIgnoreCase));
            l.Insert(0, port);
            Yaz("BILINEN_PORTLAR", string.Join(",", l.Take(5)));
        }

        // Bağlanınca: klavyeyi etkin yapar. Döner: bu bilgisayarda ilk kez mi görüldü
        public static bool KlavyeyiSec(string kimlik)
        {
            kimlik = GecerliKimlik(kimlik);
            bool yeni = false;

            if (kimlik.Length > 0)
            {
                List<string> liste = Klavyeler();
                if (!liste.Contains(kimlik))
                {
                    yeni = true;
                    if (liste.Count == 0) IlkKlavyeyeTasi(kimlik);
                    liste.Add(kimlik);
                    Yaz("KLAVYELER", string.Join(",", liste));
                }
            }

            aktifKlavye = kimlik;
            if (Al("SON_KLAVYE") != kimlik) Yaz("SON_KLAVYE", kimlik);
            return yeni;
        }

        // Kimliği öğrenilen ilk klavye: o güne kadarki ortak geçmiş ve etkin profil onundur
        private static void IlkKlavyeyeTasi(string kimlik)
        {
            if (VeriKilitli) return;
            try
            {
                string kok = Path.Combine(Klasor, "Gecmis");
                if (Directory.Exists(kok))
                {
                    string hedef = Path.Combine(kok, kimlik);
                    Directory.CreateDirectory(hedef);
                    foreach (string yol in Directory.GetFiles(kok, "*.txt"))
                    {
                        string yeni = Path.Combine(hedef, Path.GetFileName(yol));
                        if (!File.Exists(yeni)) File.Move(yol, yeni);
                    }
                }
            }
            catch (Exception) { }

            string profil = Al("AKTIF_PROFIL");
            if (profil.Length > 0) Yaz("AKTIF_PROFIL_" + kimlik, profil);
        }

        // Tepsideyken klavye takılınca / çıkarılınca bildirim (varsayılan açık)
        public static bool BaglantiBildirimi
        {
            get { return Al("BAGLANTI_BILDIRIMI") != "0"; }
            set { Yaz("BAGLANTI_BILDIRIMI", value ? "1" : "0"); }
        }

        // Koyu tema (yeniden başlatınca uygulanır)
        public static bool KoyuTema
        {
            get { return Al("KOYU_TEMA") != "0"; }   // varsayılan koyu (16 tuşlu uygulamayla aynı görünüm)
            set { Yaz("KOYU_TEMA", value ? "1" : "0"); }
        }

        // Ana pencere her zaman diğer pencerelerin üstünde dursun
        public static bool HerZamanUstte
        {
            get { return Al("HER_ZAMAN_USTTE") == "1"; }
            set { Yaz("HER_ZAMAN_USTTE", value ? "1" : "0"); }
        }

        // Tuş kartlarını kullanım sayısına göre renklendir
        public static bool IsiHaritasi
        {
            get { return Al("ISI_HARITASI") == "1"; }
            set { Yaz("ISI_HARITASI", value ? "1" : "0"); }
        }

        // İlk açılış tanıtımı gösterildi mi
        public static bool RehberGoruldu
        {
            get { return Al("REHBER") == "1"; }
            set { Yaz("REHBER", value ? "1" : "0"); }
        }

        // LED penceresinde son kullanılan renkler ("FF0000,0066FF,...", en fazla 8)
        public static string SonLedRenkleri
        {
            get { return Al("LED_SON_RENKLER"); }
            set { Yaz("LED_SON_RENKLER", value ?? ""); }
        }

        // Ana pencerenin son konumu (kaydedilmemişse null)
        public static Point? PencereKonumu
        {
            get
            {
                string[] p = Al("PENCERE").Split(',');
                int x, y;
                if (p.Length == 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y))
                    return new Point(x, y);
                return null;
            }
            set { Yaz("PENCERE", value.HasValue ? value.Value.X + "," + value.Value.Y : ""); }
        }

        // Ana pencerenin son boyutu (kaydedilmemişse boş: en küçük boyutla açılır)
        public static Size PencereBoyutu
        {
            get
            {
                string[] p = Al("PENCERE_BOYUT").Split(',');
                int w, h;
                if (p.Length == 2 && int.TryParse(p[0], out w) && int.TryParse(p[1], out h)) return new Size(w, h);
                return Size.Empty;
            }
            set { Yaz("PENCERE_BOYUT", value.Width + "," + value.Height); }
        }

        // Pencere büyütülmüş (tam ekran) kapatıldıysa öyle açılır
        public static bool PencereTamEkran
        {
            get { return Al("PENCERE_TAM") == "1"; }
            set { Yaz("PENCERE_TAM", value ? "1" : "0"); }
        }

        // Komut menüsünde hangi öğenin kaç kez seçildiği ("CTRL+C:5;@METIN:2")
        public static Dictionary<string, int> MenuSayaclari()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();

            foreach (string parca in Al("MENU_SAYAC").Split(';'))
            {
                int i = parca.LastIndexOf(':');
                int n;
                if (i > 0 && int.TryParse(parca.Substring(i + 1), out n)) d[parca.Substring(0, i)] = n;
            }
            return d;
        }

        public static void MenuSayaciArtir(string anahtar)
        {
            try
            {
                Dictionary<string, int> d = MenuSayaclari();
                int n;
                d.TryGetValue(anahtar, out n);
                d[anahtar] = n + 1;

                Yaz("MENU_SAYAC", string.Join(";", d.OrderByDescending(kv => kv.Value).Take(30)
                                                     .Select(kv => kv.Key + ":" + kv.Value)));
            }
            catch (Exception) { }
        }

        // Güncellemeden sonra yeni sürüm ilk açılınca gösterilecek "sürüm|yayın notu"
        public static string BekleyenYenilik
        {
            get { return Al("BEKLEYEN_YENILIK"); }
            set { Yaz("BEKLEYEN_YENILIK", (value ?? "").Replace("\r", " ").Replace("\n", " ")); }
        }

        // Bağlanınca "klavye de Türkçe Q yazsın mı?" bir kez sorulur
        public static bool DuzenSoruldu
        {
            get { return Al("DUZEN_SORULDU") == "1"; }
            set { Yaz("DUZEN_SORULDU", value ? "1" : "0"); }
        }

        // Güncellemeleri sormadan kur
        public static bool OtomatikUygulamaGuncelle
        {
            get { return Al("OTOMATIK_UYGULAMA") == "1"; }
            set { Yaz("OTOMATIK_UYGULAMA", value ? "1" : "0"); }
        }

        public static bool OtomatikKartGuncelle
        {
            get { return Al("OTOMATIK_KART") == "1"; }
            set { Yaz("OTOMATIK_KART", value ? "1" : "0"); }
        }

        // Kaldırırken veriler silindiyse uygulama kapanırken hiçbir dosya yeniden yazılmasın
        public static void YazmayiDurdur()
        {
            VeriKilitli = true;
        }

        // "Bilgisayara kurulsun mu?" sorusuna Hayır denildi
        public static bool KurulumReddedildi
        {
            get { return Al("KURULUM_REDDEDILDI") == "1"; }
            set { Yaz("KURULUM_REDDEDILDI", value ? "1" : "0"); }
        }

        // "Windows açılınca başlat" kaydını başka bir exe'ye (kurulu kopya) çevirir
        public static void BaslatYolunuAyarla(string exe)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunAnahtari, true))
                    if (k != null) k.SetValue(RunDegeri, "\"" + exe + "\" --tepsi");
            }
            catch (Exception) { }
        }

        // "Hayır" denen uygulama sürümü: açılışta bir daha sorulmaz
        public static string UygulamaAtlananSurum
        {
            get { return Al("UYGULAMA_ATLANAN"); }
            set { Yaz("UYGULAMA_ATLANAN", value ?? ""); }
        }

        // "Hayır" denen internet sürümü: bir daha bağlanınca sorulmaz (Ayarlar > Kart'ta yine görünür)
        public static string KartAtlananSurum
        {
            get { return Al("KART_ATLANAN"); }
            set { Yaz("KART_ATLANAN", value ?? ""); }
        }

        // Kart yazılımı dosyalarının (code.py, ...) bulunduğu klasör
        public static string KartKlasoru
        {
            get
            {
                string k = Al("KART_KLASORU");
                return k.Length > 0 ? k : Path.Combine(Application.StartupPath, "Kart");
            }
            set { Yaz("KART_KLASORU", value ?? ""); }
        }

        // ---------------------------------------------------------
        // Kullanım istatistikleri: istatistik.ini (BUTTON0_SHORT=12 ...)
        // ---------------------------------------------------------
        private static string IstatistikDosyasi
        {
            get { return Path.Combine(Klasor, "istatistik.ini"); }
        }

        public static Dictionary<string, int> IstatistikOku()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();

            try
            {
                if (!File.Exists(IstatistikDosyasi)) return d;

                foreach (string ham in File.ReadAllLines(IstatistikDosyasi, Utf8))
                {
                    int eq = ham.IndexOf('=');
                    int sayi;
                    if (eq > 0 && int.TryParse(ham.Substring(eq + 1).Trim(), out sayi))
                        d[ham.Substring(0, eq).Trim()] = sayi;
                }
            }
            catch (Exception) { }

            return d;
        }

        public static void IstatistikYaz(Dictionary<string, int> d)
        {
            if (VeriKilitli) return;

            try
            {
                Directory.CreateDirectory(Klasor);
                File.WriteAllLines(IstatistikDosyasi,
                    d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value), Utf8);
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------
        // Windows ile başlat (oturum açılınca, --tepsi ile)
        // ---------------------------------------------------------
        public static bool WindowsIleBaslat
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunAnahtari, false))
                        return k != null && k.GetValue(RunDegeri) != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunAnahtari, true))
                {
                    if (k == null) return;

                    if (value)
                        k.SetValue(RunDegeri, "\"" + Application.ExecutablePath + "\" --tepsi");
                    else if (k.GetValue(RunDegeri) != null)
                        k.DeleteValue(RunDegeri);
                }
            }
        }

        // ---------------------------------------------------------
        // Profiller
        // ---------------------------------------------------------
        public static List<string> Profiller()
        {
            try
            {
                if (!Directory.Exists(ProfilKlasoru)) return new List<string>();

                return Directory.GetFiles(ProfilKlasoru, "*.txt")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public static string ProfilYolu(string ad)
        {
            return Path.Combine(ProfilKlasoru, ad + ".txt");
        }

        // Dosya adında kullanılamayan karakterleri atar
        public static string AdTemizle(string ad)
        {
            ad = (ad ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                ad = ad.Replace(c.ToString(), "");
            return ad.Trim().TrimEnd('.');
        }

        public static void ProfilYaz(string ad, string metin)
        {
            Directory.CreateDirectory(ProfilKlasoru);
            File.WriteAllText(ProfilYolu(ad), metin, Utf8);
            GunlukEkle(GunlukBilgi, "\"" + ad + "\" profili kaydedildi");
        }

        public static string ProfilOku(string ad)
        {
            return File.ReadAllText(ProfilYolu(ad), Utf8);
        }

        public static void DosyaYaz(string yol, string metin)
        {
            File.WriteAllText(yol, metin, Utf8);
        }

        public static string DosyaOku(string yol)
        {
            return File.ReadAllText(yol, Utf8);
        }

        // ---------------------------------------------------------
        // Gizli metinler (PIN, şifre): uygulamada ●●●●●● görünür, klavyeye aynen gider.
        // Metnin kendisi değil, kısa bir özeti (SHA-256'nın ilk 16 hanesi) saklanır.
        // ---------------------------------------------------------
        public const string Maske = "●●●●●●";
        private static HashSet<string> gizliler;

        private static HashSet<string> Gizliler
        {
            get
            {
                if (gizliler == null)
                    gizliler = new HashSet<string>(Al("GIZLI_METINLER")
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                return gizliler;
            }
        }

        private static string MetinOzeti(string metin)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(metin))).Replace("-", "").Substring(0, 16);
        }

        // metin: TEXT: önekinden sonraki kısım (karta giden hâli)
        public static bool GizliMi(string metin)
        {
            return !string.IsNullOrEmpty(metin) && Gizliler.Contains(MetinOzeti(metin));
        }

        public static void GizliAyarla(string metin, bool gizli)
        {
            if (string.IsNullOrEmpty(metin)) return;

            string ozet = MetinOzeti(metin);
            bool degisti = gizli ? Gizliler.Add(ozet) : Gizliler.Remove(ozet);
            if (!degisti) return;

            try { Yaz("GIZLI_METINLER", string.Join(",", Gizliler)); }
            catch (Exception) { }
        }

        // Ekranda gösterilecek hâli: gizliyse ●●●●●●
        public static string GizliyseMaskele(string metin, string gosterim)
        {
            return GizliMi(metin) ? Maske : gosterim;
        }

        // ---------------------------------------------------------
        // Saklanan makrolar: Makrolar\<ad>.txt  (içerik: MACRO:... komutu)
        // ---------------------------------------------------------
        public static string MakroKlasoru
        {
            get { return Path.Combine(Klasor, "Makrolar"); }
        }

        public static List<string> Makrolar()
        {
            try
            {
                if (!Directory.Exists(MakroKlasoru)) return new List<string>();

                return Directory.GetFiles(MakroKlasoru, "*.txt")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public static string MakroYolu(string ad)
        {
            return Path.Combine(MakroKlasoru, ad + ".txt");
        }

        public static void MakroYaz(string ad, string komut)
        {
            Directory.CreateDirectory(MakroKlasoru);
            File.WriteAllText(MakroYolu(ad), komut, Utf8);
            GunlukEkle(GunlukBilgi, "\"" + ad + "\" makrosu Makrolarım'a kaydedildi");
        }

        public static string MakroOku(string ad)
        {
            return File.ReadAllText(MakroYolu(ad), Utf8).Trim();
        }

        public static void MakroSil(string ad)
        {
            File.Delete(MakroYolu(ad));
        }

        // ---------------------------------------------------------
        // Tuşlara verilen ad ve renk (yalnızca uygulamada görünür): tuslar.ini
        //   AD0=Toplantı sesi
        //   RENK0=DB2777
        // ---------------------------------------------------------
        private static string TuslarDosyasi
        {
            get { return Path.Combine(Klasor, "tuslar.ini"); }
        }

        public static Dictionary<int, TusAdRenk> TusAdlariOku()
        {
            Dictionary<int, TusAdRenk> d = new Dictionary<int, TusAdRenk>();

            try
            {
                if (!File.Exists(TuslarDosyasi)) return d;

                foreach (string ham in File.ReadAllLines(TuslarDosyasi, Utf8))
                    TusAdSatiriIsle(ham, "", d);
            }
            catch (Exception) { }

            return d;
        }

        public static void TusAdlariYaz(Dictionary<int, TusAdRenk> d)
        {
            if (VeriKilitli) return;

            try
            {
                Directory.CreateDirectory(Klasor);
                File.WriteAllText(TuslarDosyasi, TusAdSatirlari(d, ""), Utf8);
            }
            catch (Exception) { }
        }

        // "AD3=..." / "RENK3=..." satırlarını (profilde "#AD3=..." önekiyle) okur. Satır uyduysa true.
        public static bool TusAdSatiriIsle(string satir, string onek, Dictionary<int, TusAdRenk> d)
        {
            satir = satir.Trim();
            if (!satir.StartsWith(onek)) return false;
            satir = satir.Substring(onek.Length);

            int eq = satir.IndexOf('=');
            if (eq <= 0) return false;

            string anahtar = satir.Substring(0, eq);
            string deger = satir.Substring(eq + 1).Trim();
            int n;

            if (anahtar.StartsWith("AD") && int.TryParse(anahtar.Substring(2), out n))
            {
                TusAdRenkAl(d, n).Ad = deger;
                return true;
            }
            if (anahtar.StartsWith("RENK") && int.TryParse(anahtar.Substring(4), out n))
            {
                TusAdRenkAl(d, n).Renk = deger.ToUpperInvariant();
                return true;
            }
            if (anahtar.StartsWith("SIMGE") && int.TryParse(anahtar.Substring(5), out n))
            {
                TusAdRenkAl(d, n).Simge = deger.ToUpperInvariant();
                return true;
            }
            return false;
        }

        public static string TusAdSatirlari(Dictionary<int, TusAdRenk> d, string onek)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var kv in d.OrderBy(k => k.Key))
            {
                if (!string.IsNullOrEmpty(kv.Value.Ad))
                    sb.Append(onek).Append("AD").Append(kv.Key).Append('=')
                      .Append(kv.Value.Ad.Replace("\r", " ").Replace("\n", " ")).Append('\n');
                if (!string.IsNullOrEmpty(kv.Value.Renk))
                    sb.Append(onek).Append("RENK").Append(kv.Key).Append('=').Append(kv.Value.Renk).Append('\n');
                if (!string.IsNullOrEmpty(kv.Value.Simge))
                    sb.Append(onek).Append("SIMGE").Append(kv.Key).Append('=').Append(kv.Value.Simge).Append('\n');
            }
            return sb.ToString();
        }

        private static TusAdRenk TusAdRenkAl(Dictionary<int, TusAdRenk> d, int n)
        {
            TusAdRenk t;
            if (!d.TryGetValue(n, out t)) d[n] = t = new TusAdRenk();
            return t;
        }

        // ---------------------------------------------------------
        // Otomatik yedekler (geçmiş): Gecmis\yyyy-MM-dd_HH-mm-ss-fff.txt
        // Her başarılı kayıttan sonra karttaki durum saklanır, son GecmisSiniri tanesi tutulur.
        // ---------------------------------------------------------
        public const int GecmisSiniri = 20;
        private const string GecmisBasligi = "# GECMIS=";
        private const string GecmisZamanBicimi = "yyyy-MM-dd_HH-mm-ss-fff";

        // Etkin klavyenin geçmişi (kimliği bilinmeyen klavyede ortak klasör)
        public static string GecmisKlasoru
        {
            get
            {
                string kok = Path.Combine(Klasor, "Gecmis");
                return AktifKlavye.Length > 0 ? Path.Combine(kok, AktifKlavye) : kok;
            }
        }

        public static void GecmisEkle(string aciklama, string metin)
        {
            if (string.IsNullOrEmpty(metin) || VeriKilitli) return;

            try
            {
                Directory.CreateDirectory(GecmisKlasoru);

                // Son yedekle aynı durumdaysa yeni dosya açma
                List<GecmisOgesi> mevcut = GecmisListesi();
                if (mevcut.Count > 0 && Govde(File.ReadAllText(mevcut[0].Yol, Utf8)) == Govde(metin))
                    return;

                string yol = Path.Combine(GecmisKlasoru, DateTime.Now.ToString(GecmisZamanBicimi) + ".txt");
                string baslik = GecmisBasligi + (aciklama ?? "").Replace("\r", " ").Replace("\n", " ") + "\n";
                File.WriteAllText(yol, baslik + metin, Utf8);

                foreach (GecmisOgesi eski in GecmisListesi().Skip(GecmisSiniri))
                    File.Delete(eski.Yol);
            }
            catch (Exception) { }
        }

        // En yeni en üstte
        public static List<GecmisOgesi> GecmisListesi()
        {
            return GecmisListesi(GecmisKlasoru);
        }

        // Başka bir klavyenin geçmişi (kimliğe göre)
        public static List<GecmisOgesi> KlavyeGecmisi(string kimlik)
        {
            kimlik = GecerliKimlik(kimlik);
            return kimlik.Length == 0 ? new List<GecmisOgesi>()
                                      : GecmisListesi(Path.Combine(Klasor, "Gecmis", kimlik));
        }

        private static List<GecmisOgesi> GecmisListesi(string klasor)
        {
            List<GecmisOgesi> sonuc = new List<GecmisOgesi>();

            try
            {
                if (!Directory.Exists(klasor)) return sonuc;

                foreach (string yol in Directory.GetFiles(klasor, "*.txt")
                                                .OrderByDescending(y => Path.GetFileName(y), StringComparer.Ordinal))
                {
                    DateTime zaman;
                    if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(yol), GecmisZamanBicimi,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out zaman))
                        zaman = File.GetLastWriteTime(yol);

                    string aciklama = "";
                    try
                    {
                        using (StreamReader r = new StreamReader(yol, Utf8))
                        {
                            string ilk = r.ReadLine() ?? "";
                            if (ilk.StartsWith(GecmisBasligi)) aciklama = ilk.Substring(GecmisBasligi.Length);
                        }
                    }
                    catch (Exception) { }

                    sonuc.Add(new GecmisOgesi { Yol = yol, Zaman = zaman, Aciklama = aciklama });
                }
            }
            catch (Exception) { }

            return sonuc;
        }

        // Karşılaştırma için tarih / açıklama yorum satırları atılmış içerik
        private static string Govde(string metin)
        {
            return string.Join("\n", metin.Replace("\r", "").Split('\n')
                                          .Where(s => s.Length > 0 && !s.StartsWith("# ")));
        }

        // ---------------------------------------------------------
        // Olay günlüğü: klavyeye ne yazıldı, bağlantı, kaydedilmeden atılanlar...
        // gunluk.txt satırı: "2026-10-16 14:05:09<TAB>YAZ<TAB>BUTON 3 · Kısa basış: Kopyala"
        // ---------------------------------------------------------
        public const string GunlukYazma = "YAZ";      // klavyeye yazıldı
        public const string GunlukBaglanti = "BAG";   // bağlandı / koptu
        public const string GunlukUyari = "UYARI";    // yazılamadı, kaydedilmeden atıldı
        public const string GunlukBilgi = "BILGI";    // profil / makro / yedek kaydı
        public const int GunlukSiniri = 500;
        private const string GunlukZamanBicimi = "yyyy-MM-dd HH:mm:ss";

        private static string GunlukDosyasi
        {
            get { return Path.Combine(Klasor, "gunluk.txt"); }
        }

        public static void GunlukEkle(string tur, string olay)
        {
            if (VeriKilitli || string.IsNullOrEmpty(olay)) return;

            try
            {
                Directory.CreateDirectory(Klasor);
                string satir = DateTime.Now.ToString(GunlukZamanBicimi, System.Globalization.CultureInfo.InvariantCulture) +
                               "\t" + tur + "\t" + olay.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ") + "\r\n";
                File.AppendAllText(GunlukDosyasi, satir, Utf8);

                // Dosya büyüyünce yalnızca son GunlukSiniri satırı bırak (her eklemede değil, ara sıra)
                if (new FileInfo(GunlukDosyasi).Length > 80 * 1024)
                {
                    string[] satirlar = File.ReadAllLines(GunlukDosyasi, Utf8);
                    if (satirlar.Length > GunlukSiniri)
                        File.WriteAllLines(GunlukDosyasi, satirlar.Skip(satirlar.Length - GunlukSiniri), Utf8);
                }
            }
            catch (Exception) { }
        }

        // En yeni en üstte
        public static List<GunlukOgesi> GunlukOku()
        {
            List<GunlukOgesi> sonuc = new List<GunlukOgesi>();

            try
            {
                if (!File.Exists(GunlukDosyasi)) return sonuc;

                foreach (string satir in File.ReadAllLines(GunlukDosyasi, Utf8).Reverse())
                {
                    string[] p = satir.Split(new[] { '\t' }, 3);
                    DateTime zaman;
                    if (p.Length < 3 || !DateTime.TryParseExact(p[0], GunlukZamanBicimi,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out zaman))
                        continue;

                    sonuc.Add(new GunlukOgesi { Zaman = zaman, Tur = p[1], Olay = p[2] });
                    if (sonuc.Count >= GunlukSiniri) break;
                }
            }
            catch (Exception) { }

            return sonuc;
        }

        public static void GunlukTemizle()
        {
            if (File.Exists(GunlukDosyasi)) File.Delete(GunlukDosyasi);
        }

        // ---------------------------------------------------------
        // Tam yedek (tek .zip):
        //   makro-klavye-yedek.txt   -> imza, tarih, bilgisayar adı
        //   klavye.txt               -> klavyedeki ayarlar (yedek alınırken bağlıysa)
        //   veri/ayarlar.ini, veri/tuslar.ini, veri/istatistik.ini, veri/gunluk.txt
        //   veri/Profiller/*.txt, veri/Makrolar/*.txt, veri/Gecmis/*.txt
        // ---------------------------------------------------------
        private const string YedekBilgiDosyasi = "makro-klavye-yedek.txt";
        private const string YedekImzasi = "MAKRO_KLAVYE_TAM_YEDEK=1";
        private const string YedekKlavyeDosyasi = "klavye.txt";
        private const string VeriOneki = "veri/";
        public const string OncekiHalDosyasi = "geri-yukleme-oncesi.zip";

        private static readonly string[] YedekDosyalari = { "ayarlar.ini", "tuslar.ini", "istatistik.ini", "gunluk.txt" };
        private static readonly string[] YedekKlasorleri = { "Profiller", "Makrolar", "Gecmis" };

        // Geri yüklemeden sonra uygulama yeniden başlarken kapanışta yazılan dosyalar
        // (pencere konumu, istatistik, tuş adları) geri yükleneni ezmesin
        public static bool VeriKilitli { get; private set; }

        // Geri yüklemeden önceki hâlin saklandığı dosya (yedeklere dahil edilmez)
        public static string OncekiHalYolu
        {
            get { return Path.Combine(Klasor, OncekiHalDosyasi); }
        }

        // Zip içindeki ad ("Profiller/Oyun.txt") -> diskteki yol
        private static List<KeyValuePair<string, string>> YedeklenecekDosyalar()
        {
            List<KeyValuePair<string, string>> l = new List<KeyValuePair<string, string>>();

            foreach (string ad in YedekDosyalari)
            {
                string yol = Path.Combine(Klasor, ad);
                if (File.Exists(yol)) l.Add(new KeyValuePair<string, string>(ad, yol));
            }

            foreach (string alt in YedekKlasorleri)
            {
                string klasor = Path.Combine(Klasor, alt);
                if (!Directory.Exists(klasor)) continue;

                foreach (string yol in Directory.GetFiles(klasor, "*.txt").OrderBy(y => y, StringComparer.OrdinalIgnoreCase))
                    l.Add(new KeyValuePair<string, string>(alt + "/" + Path.GetFileName(yol), yol));

                // Geçmiş: her klavyenin kendi klasörü (Gecmis/<kimlik>/...)
                if (alt != "Gecmis") continue;
                foreach (string altKlasor in Directory.GetDirectories(klasor))
                {
                    string kimlik = GecerliKimlik(Path.GetFileName(altKlasor));
                    if (kimlik.Length == 0) continue;
                    foreach (string yol in Directory.GetFiles(altKlasor, "*.txt").OrderBy(y => y, StringComparer.OrdinalIgnoreCase))
                        l.Add(new KeyValuePair<string, string>(alt + "/" + kimlik + "/" + Path.GetFileName(yol), yol));
                }
            }
            return l;
        }

        // klavyeMetni: klavyedeki ayarlar (bağlı değilse null)
        public static TamYedek TamYedekOlustur(string yol, string klavyeMetni)
        {
            TamYedek y = new TamYedek
            {
                Tarih = DateTime.Now,
                Bilgisayar = Environment.MachineName,
                KlavyeMetni = klavyeMetni
            };

            List<KeyValuePair<string, byte[]>> zip = new List<KeyValuePair<string, byte[]>>();
            string bilgi = YedekImzasi + "\r\n" +
                           "TARIH=" + y.Tarih.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
                           "BILGISAYAR=" + y.Bilgisayar + "\r\n";
            zip.Add(new KeyValuePair<string, byte[]>(YedekBilgiDosyasi, Utf8.GetBytes(bilgi)));

            if (klavyeMetni != null)
                zip.Add(new KeyValuePair<string, byte[]>(YedekKlavyeDosyasi, Utf8.GetBytes(klavyeMetni)));

            foreach (var kv in YedeklenecekDosyalar())
            {
                byte[] icerik = File.ReadAllBytes(kv.Value);
                y.Dosyalar[kv.Key] = icerik;
                zip.Add(new KeyValuePair<string, byte[]>(VeriOneki + kv.Key, icerik));
            }

            // Yarıda kalırsa (disk dolu vb.) aynı adlı eski yedek bozulmasın: önce geçici dosyaya yaz
            string gecici = yol + ".yaziliyor";
            KucukZip.Yaz(gecici, zip);
            if (File.Exists(yol)) File.Delete(yol);
            File.Move(gecici, yol);

            return y;
        }

        public static TamYedek TamYedekOku(string yol)
        {
            Dictionary<string, byte[]> hepsi = KucukZip.Oku(yol);

            byte[] bilgi;
            if (!hepsi.TryGetValue(YedekBilgiDosyasi, out bilgi) || !Utf8.GetString(bilgi).Contains(YedekImzasi))
                throw new InvalidDataException("Bu zip bir Makro Klavye tam yedeği değil.");

            TamYedek y = new TamYedek();
            foreach (string satir in Utf8.GetString(bilgi).Replace("\r", "").Split('\n'))
            {
                DateTime t;
                if (satir.StartsWith("TARIH=") &&
                    DateTime.TryParseExact(satir.Substring(6), "yyyy-MM-dd HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out t))
                    y.Tarih = t;
                else if (satir.StartsWith("BILGISAYAR="))
                    y.Bilgisayar = satir.Substring(11);
            }

            byte[] klavye;
            if (hepsi.TryGetValue(YedekKlavyeDosyasi, out klavye)) y.KlavyeMetni = Utf8.GetString(klavye).TrimStart('\uFEFF');

            foreach (var kv in hepsi)
            {
                if (!kv.Key.StartsWith(VeriOneki, StringComparison.OrdinalIgnoreCase)) continue;
                string ad = GecerliVeriAdi(kv.Key.Substring(VeriOneki.Length));
                if (ad != null) y.Dosyalar[ad] = kv.Value;
            }

            return y;
        }

        // Yalnızca bilinen dosyalar kabul edilir ("../" gibi klasör dışına çıkan adlar atılır).
        // Döner: düzgün yazılmış ad ("profiller/a.txt" -> "Profiller/a.txt"), geçersizse null
        private static string GecerliVeriAdi(string ad)
        {
            string[] p = ad.Split('/');

            if (p.Length == 1)
                return YedekDosyalari.FirstOrDefault(d => string.Equals(d, p[0], StringComparison.OrdinalIgnoreCase));

            // Gecmis/<kimlik>/dosya.txt: klavyeye özel geçmiş
            string altKlasor = "";
            if (p.Length == 3 && string.Equals(p[0], "Gecmis", StringComparison.OrdinalIgnoreCase))
            {
                string kimlik = GecerliKimlik(p[1]);
                if (kimlik.Length == 0) return null;
                altKlasor = kimlik + "/";
                p = new[] { p[0], p[2] };
            }

            if (p.Length != 2) return null;

            string klasor = YedekKlasorleri.FirstOrDefault(d => string.Equals(d, p[0], StringComparison.OrdinalIgnoreCase));
            string dosya = p[1];
            if (klasor == null || dosya.Length < 5 || !dosya.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                dosya.StartsWith(".") || dosya.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;

            return klasor + "/" + altKlasor + dosya;
        }

        // Şimdiki hâli OncekiHalYolu'na saklar (geri yüklemeden hemen önce)
        public static void OncekiHaliSakla(string klavyeMetni)
        {
            Directory.CreateDirectory(Klasor);
            TamYedekOlustur(OncekiHalYolu, klavyeMetni);
        }

        // Bu bilgisayardaki verileri yedektekiyle değiştirir. Sonrasında uygulama yeniden başlatılmalı
        // (VeriKilitli açılır, kapanırken hiçbir ayar dosyası yazılmaz).
        public static void TamYedekGeriYukle(TamYedek y)
        {
            string kok = Path.GetFullPath(Klasor).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(kok);

            // Önce eskiler silinir: yedekte olmayan profil / makro geride kalmasın
            foreach (string ad in YedekDosyalari)
            {
                string yol = Path.Combine(kok, ad);
                if (File.Exists(yol)) File.Delete(yol);
            }
            foreach (string alt in YedekKlasorleri)
            {
                string klasor = Path.Combine(kok, alt);
                if (!Directory.Exists(klasor)) continue;
                foreach (string yol in Directory.GetFiles(klasor, "*.txt", SearchOption.AllDirectories)) File.Delete(yol);
            }

            foreach (var kv in y.Dosyalar)
            {
                string hedef = Path.GetFullPath(Path.Combine(kok, kv.Key.Replace('/', Path.DirectorySeparatorChar)));
                if (!hedef.StartsWith(kok, StringComparison.OrdinalIgnoreCase)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(hedef));
                File.WriteAllBytes(hedef, kv.Value);
            }

            gizliler = null;
            aktifKlavye = null;   // ayarlar.ini değişti: son klavye yeniden okunsun
            GunlukEkle(GunlukBilgi, "Tam yedek geri yüklendi" +
                       (y.Tarih.HasValue ? " (" + y.Tarih.Value.ToString("dd.MM.yyyy HH:mm") + " tarihli)" : ""));
            VeriKilitli = true;
        }
    }

    // Kartın metinleri hangi Windows klavye düzenine göre yazdığı
    // (KLAVYE_DUZENI=US | TR: Türkçe Q, kart 2.0+ | TRF: Türkçe F, kart 2.3+)
    public static class KlavyeDuzeni
    {
        public static string Kod = "US";   // ana pencere karttaki ayarları okuyunca günceller

        // Kart 2.5+: düzende olmayan harfleri (é, ä, ñ, ß, ^ ...) Windows Alt koduyla yazar
        public static bool AltKodu;

        public static bool Turkce { get { return Kod != "US"; } }
        public static string Adi { get { return Ad(Kod); } }

        private const string TurkceQEk = "çÇğĞıİöÖşŞüÜé€₺£½";
        private const string TurkceFEk = "çÇğĞıİöÖşŞüÜ€₺£½¼¾";

        // settings.txt'deki değer -> US / TR / TRF
        public static string Coz(string ayar)
        {
            string a = (ayar ?? "").Trim().ToUpperInvariant();
            return a == "TR" || a == "TRF" ? a : "US";
        }

        public static string Ad(string kod)
        {
            return kod == "TR" ? "Türkçe Q" : kod == "TRF" ? "Türkçe F" : "İngilizce (US)";
        }

        // Windows() sonucunun karttaki karşılığı (desteklenmeyen düzende null)
        public static string WindowsKodu(string w)
        {
            return w == "TR_Q" ? "TR" : w == "TR_F" ? "TRF" : w == "US" ? "US" : null;
        }

        // Seçili düzende yazılamayan ilk karakter (hepsi yazılabiliyorsa null)
        public static string Yazilamayan(string metin, string kod)
        {
            string ek = kod == "TR" ? TurkceQEk : kod == "TRF" ? TurkceFEk : "";
            foreach (char c in metin ?? "")
            {
                bool olur = kod != "US" ? (c < 127 && "^`~".IndexOf(c) < 0) || ek.IndexOf(c) >= 0
                                        : c <= 127;
                if (!olur && AltKodu) olur = AnsiVar(c, kod != "US");
                if (!olur) return c.ToString();
            }
            return null;
        }

        // Karakter Windows'un ANSI kod sayfasında var mı (Türkçe Windows 1254, diğeri 1252): Alt koduyla yazılabilir
        private static bool AnsiVar(char c, bool turkce)
        {
            if (c >= 32 && c < 127) return true;
            try
            {
                Encoding e = Encoding.GetEncoding(turkce ? 1254 : 1252, EncoderFallback.ExceptionFallback,
                                                  DecoderFallback.ExceptionFallback);
                byte[] b = e.GetBytes(new[] { c });
                return b.Length == 1 && b[0] >= 128;
            }
            catch (Exception) { return false; }
        }

        // Metin seçili düzende yazılamıyorsa kullanıcıya gösterilecek açıklama, yazılabiliyorsa null
        public static string Hata(string metin)
        {
            string c = Yazilamayan(metin, Kod);
            if (c == null) return null;

            if (!Turkce && TurkceQEk.IndexOf(c[0]) >= 0)
                return "Türkçe harf (" + c + ") yazmak için Ayarlar > Kart > KLAVYE AYARLARI'ndan düzeni " +
                       "Türkçe Q ya da Türkçe F yap (bilgisayarındakiyle aynı).";
            if (AltKodu)
                return "\"" + c + "\" klavyeyle yazılamıyor (emoji ve bazı özel işaretler yazılamaz).";
            return "\"" + c + "\" karakteri " + Adi + " düzeninde yazılamıyor. " +
                   "é, ä, ñ gibi harfler için kart yazılımını 2.5'e güncelle.";
        }

        // Windows'ta şu an seçili klavye: "TR_Q", "TR_F", "US" ya da düzenin adı
        public static string Windows()
        {
            try
            {
                InputLanguage dil = InputLanguage.CurrentInputLanguage;
                string ad = dil.LayoutName ?? "";
                if (dil.Culture.Name == "tr-TR")
                    return ad.EndsWith(" F") || ad.EndsWith("-F") || ad.Contains(" F ") ? "TR_F" : "TR_Q";
                if (ad == "US" || ad.StartsWith("United States") || ad == "ABD") return "US";
                return ad;
            }
            catch (Exception) { return ""; }
        }

        public static string WindowsAdi()
        {
            string w = Windows();
            return w == "TR_Q" ? "Türkçe Q" : w == "TR_F" ? "Türkçe F" : w == "US" ? "İngilizce (US)" : w;
        }
    }

    // Tam yedeğin içeriği
    public class TamYedek
    {
        public DateTime? Tarih;
        public string Bilgisayar = "";
        public string KlavyeMetni;    // yedek alınırken klavye bağlı değilse null
        public Dictionary<string, byte[]> Dosyalar = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private int Say(string klasor)
        {
            return Dosyalar.Keys.Count(k => k.StartsWith(klasor + "/", StringComparison.OrdinalIgnoreCase));
        }

        // "3 profil, 2 makro, 18 geçmiş kaydı, tuş adları, klavyedeki ayarlar"
        public string Ozet()
        {
            List<string> p = new List<string>();
            int profil = Say("Profiller"), makro = Say("Makrolar"), gecmis = Say("Gecmis");

            if (profil > 0) p.Add(profil + " profil");
            if (makro > 0) p.Add(makro + " makro");
            if (gecmis > 0) p.Add(gecmis + " geçmiş kaydı");
            if (Dosyalar.ContainsKey("tuslar.ini")) p.Add("tuş adları");
            if (Dosyalar.ContainsKey("ayarlar.ini")) p.Add("uygulama ayarları");
            if (Dosyalar.ContainsKey("gunluk.txt")) p.Add("olay günlüğü");
            if (KlavyeMetni != null) p.Add("klavyedeki ayarlar");

            return p.Count > 0 ? string.Join(", ", p) : "boş";
        }
    }

    // Olay günlüğünde bir satır
    public class GunlukOgesi
    {
        public DateTime Zaman;
        public string Tur;     // UygulamaAyarlari.GunlukYazma / GunlukBaglanti / GunlukUyari / GunlukBilgi
        public string Olay;
    }

    // Bir tuşa verilen ad, renk ve simge (renk: "DB2777", simge: Segoe MDL2 kodu "E720"; boş olabilir)
    public class TusAdRenk
    {
        public string Ad = "";
        public string Renk = "";
        public string Simge = "";

        public bool Bos
        {
            get { return string.IsNullOrEmpty(Ad) && string.IsNullOrEmpty(Renk) && string.IsNullOrEmpty(Simge); }
        }

        // "E720" -> simge karakteri (geçersizse boş)
        public string SimgeKarakteri
        {
            get
            {
                int kod;
                if (string.IsNullOrEmpty(Simge) || Simge.Length != 4 ||
                    !int.TryParse(Simge, System.Globalization.NumberStyles.HexNumber, null, out kod))
                    return "";
                return ((char)kod).ToString();
            }
        }

        public TusAdRenk Kopya()
        {
            return new TusAdRenk { Ad = Ad, Renk = Renk, Simge = Simge };
        }
    }

    // Geçmiş listesinde bir otomatik yedek
    public class GecmisOgesi
    {
        public string Yol;
        public DateTime Zaman;
        public string Aciklama;
    }

    // İstatistik sekmesinde bir satır
    public class IstatistikSatiri
    {
        public string Ad;        // "BUTON 1"
        public string Aciklama;  // "Masaüstü"
        public string Detay;     // "kısa 10 · uzun 2"
        public int Sayi;
    }
}