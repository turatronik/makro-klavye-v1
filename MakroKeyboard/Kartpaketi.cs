using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;   // DeflateStream (System.dll içinde, ek başvuru gerekmez)
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MakroKeyboard
{
    // Kart yazılımı dosyalarını bir zip dosyasından ya da klasörden okur.
    // Zip'in içinde alt klasör olabilir (örn. "MakroKlavye-Kart/code.py"); dosya adına göre bulunur.
    // settings.txt gibi diğer dosyalar yok sayılır. boot.py kart 2.3'ten beri güncellenir (USB adı).
    public static class KartPaketi
    {
        public static readonly string[] Dosyalar =
            { "code.py", "macro_keys.py", "led_animations.py", "settings_server.py", "boot.py" };

        // kaynak: .zip dosyası, .py dosyası (bulunduğu klasör kullanılır) ya da klasör.
        // Döner: bulunan kart dosyaları (ad -> içerik). Zip okunamazsa hata fırlatır.
        public static Dictionary<string, byte[]> Oku(string kaynak)
        {
            Dictionary<string, byte[]> sonuc = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(kaynak)) return sonuc;

            if (kaynak.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(kaynak)) return ZiptenSec(KucukZip.Oku(kaynak));
                return sonuc;
            }

            string klasor = File.Exists(kaynak) ? Path.GetDirectoryName(kaynak) : kaynak;
            if (!Directory.Exists(klasor)) return sonuc;

            foreach (string ad in Dosyalar)
            {
                string yol = Path.Combine(klasor, ad);
                if (File.Exists(yol)) sonuc[ad] = File.ReadAllBytes(yol);
            }
            return sonuc;
        }

        // code.py içindeki SURUM = "1.7" (code.py yoksa null, sürüm satırı yoksa "?")
        public static string Surum(Dictionary<string, byte[]> paket)
        {
            byte[] kod;
            if (paket == null || !paket.TryGetValue("code.py", out kod)) return null;

            Match m = Regex.Match(Encoding.UTF8.GetString(kod), "^SURUM\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : "?";
        }

        // Zip'teki tüm dosyalardan kart dosyalarını seçer.
        // Aynı ad birden fazla klasörde varsa en üstteki (en kısa yol) alınır.
        private static Dictionary<string, byte[]> ZiptenSec(Dictionary<string, byte[]> hepsi)
        {
            Dictionary<string, byte[]> sonuc = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> derinlik = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in hepsi)
            {
                string tamAd = kv.Key;
                if (tamAd.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;

                // Kartın beklediği ad (büyük / küçük harf farkı olsa da "code.py" olarak gönderilir)
                string zipAdi = tamAd.Substring(tamAd.LastIndexOf('/') + 1);
                int sira = Array.FindIndex(Dosyalar, d => string.Equals(d, zipAdi, StringComparison.OrdinalIgnoreCase));
                if (sira < 0) continue;
                string ad = Dosyalar[sira];

                int seviye = tamAd.Count(c => c == '/');
                int onceki;
                if (derinlik.TryGetValue(ad, out onceki) && onceki <= seviye) continue;

                sonuc[ad] = kv.Value;
                derinlik[ad] = seviye;
            }
            return sonuc;
        }
    }

    // =====================================================================
    //  Küçük zip okuyucu / yazıcı (ek kütüphane gerekmez).
    //  Okur: sıkıştırmasız (0) ve deflate (8), şifresiz zip'ler
    //        (Windows "Sıkıştırılmış klasör", 7-Zip, WinRAR zip'leri bu biçimdedir).
    //  Yazar: deflate ile sıkıştırılmış, UTF-8 adlı standart zip.
    // =====================================================================
    public static class KucukZip
    {
        // Tüm dosyalar: "klasör/ad.txt" -> içerik (klasör kayıtları atlanır)
        public static Dictionary<string, byte[]> Oku(string yol)
        {
            byte[] v = File.ReadAllBytes(yol);

            // Merkezi dizinin sonu (EOCD): imza 0x06054b50, dosyanın sonundan geriye doğru aranır
            int eocd = -1;
            for (int i = v.Length - 22; i >= 0 && i >= v.Length - 22 - 65535; i--)
            {
                if (U32(v, i) == 0x06054b50) { eocd = i; break; }
            }
            if (eocd < 0) throw new InvalidDataException("Seçilen dosya geçerli bir zip değil.");

            int adet = U16(v, eocd + 10);
            int p = (int)U32(v, eocd + 16);    // merkezi dizinin başı

            Dictionary<string, byte[]> sonuc = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            for (int n = 0; n < adet; n++)
            {
                if (p + 46 > v.Length || U32(v, p) != 0x02014b50)
                    throw new InvalidDataException("Zip dosyası bozuk.");

                int bayrak = U16(v, p + 8);
                int yontem = U16(v, p + 10);
                int sikisik = (int)U32(v, p + 20);
                int acik = (int)U32(v, p + 24);
                int adUz = U16(v, p + 28), ekUz = U16(v, p + 30), yorumUz = U16(v, p + 32);
                int yerel = (int)U32(v, p + 42);
                string ad = Encoding.UTF8.GetString(v, p + 46, adUz).Replace('\\', '/');

                p += 46 + adUz + ekUz + yorumUz;

                if (ad.EndsWith("/")) continue;   // klasör kaydı
                if ((bayrak & 1) != 0) throw new InvalidDataException("Şifreli zip desteklenmiyor.");

                // Yerel başlık: içerik, adın ve ek alanın hemen ardından başlar
                if (yerel + 30 > v.Length || U32(v, yerel) != 0x04034b50)
                    throw new InvalidDataException("Zip dosyası bozuk.");
                int basla = yerel + 30 + U16(v, yerel + 26) + U16(v, yerel + 28);
                if (basla + sikisik > v.Length) throw new InvalidDataException("Zip dosyası eksik.");

                byte[] icerik;
                if (yontem == 0)
                {
                    icerik = new byte[acik];
                    Buffer.BlockCopy(v, basla, icerik, 0, acik);
                }
                else if (yontem == 8)
                {
                    using (MemoryStream giris = new MemoryStream(v, basla, sikisik))
                    using (DeflateStream ds = new DeflateStream(giris, CompressionMode.Decompress))
                    using (MemoryStream cikis = new MemoryStream())
                    {
                        ds.CopyTo(cikis);
                        icerik = cikis.ToArray();
                    }
                }
                else
                {
                    throw new InvalidDataException(ad + " desteklenmeyen bir yöntemle sıkıştırılmış. " +
                                                   "Zip'i Windows'ta sağ tık > Sıkıştırılmış klasöre gönder ile yeniden oluştur.");
                }

                if (icerik.Length != acik) throw new InvalidDataException(ad + " zip içinde bozuk.");
                sonuc[ad] = icerik;
            }

            return sonuc;
        }

        // dosyalar: "klasör/ad.txt" -> içerik
        public static void Yaz(string yol, IEnumerable<KeyValuePair<string, byte[]>> dosyalar)
        {
            DateTime simdi = DateTime.Now;
            int dosSaat = (simdi.Hour << 11) | (simdi.Minute << 5) | (simdi.Second / 2);
            int dosTarih = ((simdi.Year - 1980) << 9) | (simdi.Month << 5) | simdi.Day;

            using (FileStream fs = new FileStream(yol, FileMode.Create, FileAccess.Write))
            using (MemoryStream merkez = new MemoryStream())
            {
                int adet = 0;

                foreach (var kv in dosyalar)
                {
                    byte[] ad = Encoding.UTF8.GetBytes(kv.Key.Replace('\\', '/'));
                    byte[] veri = kv.Value ?? new byte[0];
                    uint crc = Crc32(veri);

                    byte[] sikisik;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                            ds.Write(veri, 0, veri.Length);
                        sikisik = ms.ToArray();
                    }

                    int yontem = 8;
                    if (sikisik.Length >= veri.Length)   // sıkışmıyorsa olduğu gibi sakla
                    {
                        sikisik = veri;
                        yontem = 0;
                    }

                    uint yerel = (uint)fs.Position;

                    // Yerel başlık
                    Y32(fs, 0x04034b50);
                    Y16(fs, 20);                 // gereken sürüm
                    Y16(fs, 0x0800);             // bayrak: ad UTF-8
                    Y16(fs, yontem);
                    Y16(fs, dosSaat);
                    Y16(fs, dosTarih);
                    Y32(fs, crc);
                    Y32(fs, (uint)sikisik.Length);
                    Y32(fs, (uint)veri.Length);
                    Y16(fs, ad.Length);
                    Y16(fs, 0);                  // ek alan
                    fs.Write(ad, 0, ad.Length);
                    fs.Write(sikisik, 0, sikisik.Length);

                    // Merkezi dizin kaydı
                    Y32(merkez, 0x02014b50);
                    Y16(merkez, 20);             // oluşturan sürüm
                    Y16(merkez, 20);             // gereken sürüm
                    Y16(merkez, 0x0800);
                    Y16(merkez, yontem);
                    Y16(merkez, dosSaat);
                    Y16(merkez, dosTarih);
                    Y32(merkez, crc);
                    Y32(merkez, (uint)sikisik.Length);
                    Y32(merkez, (uint)veri.Length);
                    Y16(merkez, ad.Length);
                    Y16(merkez, 0);              // ek alan
                    Y16(merkez, 0);              // yorum
                    Y16(merkez, 0);              // disk
                    Y16(merkez, 0);              // iç öznitelik
                    Y32(merkez, 0);              // dış öznitelik
                    Y32(merkez, yerel);
                    merkez.Write(ad, 0, ad.Length);

                    adet++;
                }

                uint merkezBasi = (uint)fs.Position;
                merkez.WriteTo(fs);
                uint merkezBoyu = (uint)(fs.Position - merkezBasi);

                // Merkezi dizinin sonu
                Y32(fs, 0x06054b50);
                Y16(fs, 0);
                Y16(fs, 0);
                Y16(fs, adet);
                Y16(fs, adet);
                Y32(fs, merkezBoyu);
                Y32(fs, merkezBasi);
                Y16(fs, 0);
            }
        }

        // ---------- yardımcılar ----------
        private static int U16(byte[] v, int i)
        {
            return v[i] | (v[i + 1] << 8);
        }

        private static uint U32(byte[] v, int i)
        {
            return (uint)(v[i] | (v[i + 1] << 8) | (v[i + 2] << 16) | (v[i + 3] << 24));
        }

        private static void Y16(Stream s, int deger)
        {
            s.WriteByte((byte)(deger & 0xFF));
            s.WriteByte((byte)((deger >> 8) & 0xFF));
        }

        private static void Y32(Stream s, uint deger)
        {
            s.WriteByte((byte)(deger & 0xFF));
            s.WriteByte((byte)((deger >> 8) & 0xFF));
            s.WriteByte((byte)((deger >> 16) & 0xFF));
            s.WriteByte((byte)((deger >> 24) & 0xFF));
        }

        private static uint[] crcTablosu;

        private static uint Crc32(byte[] veri)
        {
            if (crcTablosu == null)
            {
                uint[] t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                crcTablosu = t;
            }

            uint crc = 0xFFFFFFFF;
            foreach (byte b in veri) crc = crcTablosu[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }

    // =====================================================================
    //  Kart yazılımını internetten (GitHub) güncelleme.
    //  Depoda iki dosya durur:
    //    surum.txt  ->  SURUM=1.8
    //                   ZIP=MakroKlavye-Kart-1.8.zip
    //                   SHA256=<zip'in SHA-256 özeti>   (indirme bozulmasın diye)
    //                   NOT=Bu sürümde neler var (tek satır)
    //    MakroKlavye-Kart-1.8.zip
    //  Uygulama yalnızca açıkken ve klavyeye bağlanınca bakar; internet yoksa sessizce geçer.
    // =====================================================================
    public class UzaktakiSurum
    {
        public string Surum, Zip, Sha256, Not;
    }

    public static class KartGuncelleme
    {
        // Herkese açık GitHub deposu
        public const string Kullanici = "Turan167";
        public const string Depo = "makro-klavye-kart";
        public const string Dal = "main";

        // Kullanıcı adı yazılmadıysa internetten denetleme kapalı
        public static bool Ayarli
        {
            get { return Kullanici != "KULLANICI_ADI" && Kullanici.Length > 0; }
        }

        private static HttpClient istemci;
        private static UzaktakiSurum onbellek;
        private static DateTime onbellekZamani;

        internal static HttpClient Istemci
        {
            get
            {
                if (istemci == null)
                {
                    // .NET Framework 4.7.2 eski Windows'ta TLS 1.2'yi kendiliğinden açmayabilir (GitHub ister)
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    istemci = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    istemci.DefaultRequestHeaders.UserAgent.ParseAdd("MakroKlavye");
                }
                return istemci;
            }
        }

        internal static string Adres(string dosya)
        {
            return "https://raw.githubusercontent.com/" + Kullanici + "/" + Depo + "/" + Dal + "/" + Uri.EscapeDataString(dosya);
        }

        // İnternetteki son sürüm. İnternet yok, depo yok ya da dosya bozuksa null.
        public static async Task<UzaktakiSurum> DenetleAsync(bool tazele = false)
        {
            if (!Ayarli) return null;
            if (!tazele && onbellek != null && DateTime.Now - onbellekZamani < TimeSpan.FromMinutes(10)) return onbellek;

            string metin = await MetinOku("surum.txt");
            UzaktakiSurum s = metin != null ? Coz(metin) : null;
            if (s == null) return null;

            onbellek = s;
            onbellekZamani = DateTime.Now;
            return s;
        }

        // Depodaki küçük bir metin dosyası (surum.txt, uygulama.txt). Önce GitHub API'den okunur:
        // yükler yüklemez güncel gelir. API sınırına takılırsa (saatte 60 istek) ham adrese döner;
        // o adres GitHub'da birkaç dakika eski kalabilir. Okunamazsa null.
        internal static async Task<string> MetinOku(string dosya)
        {
            string api = "https://api.github.com/repos/" + Kullanici + "/" + Depo + "/contents/" +
                         Uri.EscapeDataString(dosya) + "?ref=" + Dal;
            try
            {
                using (HttpRequestMessage istek = new HttpRequestMessage(HttpMethod.Get, api))
                using (CancellationTokenSource iptal = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                {
                    istek.Headers.Accept.ParseAdd("application/vnd.github.raw");
                    istek.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    using (HttpResponseMessage cevap = await Istemci.SendAsync(istek, iptal.Token))
                        if (cevap.IsSuccessStatusCode) return await cevap.Content.ReadAsStringAsync();
                }
            }
            catch (Exception) { }

            try
            {
                using (CancellationTokenSource iptal = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                using (HttpResponseMessage cevap = await Istemci.GetAsync(Adres(dosya) + "?t=" + DateTime.UtcNow.Ticks, iptal.Token))
                    return cevap.IsSuccessStatusCode ? await cevap.Content.ReadAsStringAsync() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static UzaktakiSurum Coz(string metin)
        {
            UzaktakiSurum s = new UzaktakiSurum();
            foreach (string ham in metin.Replace("\r", "").Split('\n'))
            {
                int eq = ham.IndexOf('=');
                if (eq <= 0) continue;
                string anahtar = ham.Substring(0, eq).Trim().TrimStart('\uFEFF'), deger = ham.Substring(eq + 1).Trim();

                if (anahtar == "SURUM") s.Surum = deger;
                else if (anahtar == "ZIP") s.Zip = deger;
                else if (anahtar == "SHA256") s.Sha256 = deger.ToLowerInvariant();
                else if (anahtar == "NOT") s.Not = deger;
            }

            Version v;
            if (s.Surum == null || !Version.TryParse(s.Surum, out v)) return null;
            if (s.Zip == null || !s.Zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                s.Zip.Contains("/") || s.Zip.Contains("\\") || s.Zip.Contains(".."))
                return null;
            if (s.Sha256 != null && !Regex.IsMatch(s.Sha256, "^[0-9a-f]{64}$")) return null;
            return s;
        }

        // internet sürümü karttakinden yeni mi (kart sürümü bilinmiyorsa: yeni sayılır)
        public static bool DahaYeni(string internet, string kart)
        {
            Version a, b;
            if (internet == null || !Version.TryParse(internet, out a)) return false;
            if (kart == null || !Version.TryParse(kart, out b)) return true;
            return a > b;
        }

        // Zip'i indirir, doğrular, kart dosyalarını döndürür. Sorun olursa hata fırlatır.
        public static async Task<Dictionary<string, byte[]>> IndirAsync(UzaktakiSurum s)
        {
            byte[] zip;
            try { zip = await Istemci.GetByteArrayAsync(Adres(s.Zip)); }
            catch (Exception ex) { throw new IOException("İndirilemedi: " + ex.Message, ex); }

            if (s.Sha256 != null)
            {
                string ozet;
                using (SHA256 sha = SHA256.Create())
                    ozet = string.Concat(sha.ComputeHash(zip).Select(b => b.ToString("x2")));
                if (ozet != s.Sha256)
                    throw new InvalidDataException("İndirilen dosya bozuk (doğrulama tutmadı). Biraz sonra tekrar dene.");
            }

            string gecici = Path.Combine(Path.GetTempPath(), "makroklavye-kart-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                File.WriteAllBytes(gecici, zip);
                Dictionary<string, byte[]> paket = KartPaketi.Oku(gecici);

                if (!paket.ContainsKey("code.py"))
                    throw new InvalidDataException("İndirilen zip'te kart yazılımı yok.");
                if (KartPaketi.Surum(paket) != s.Surum)
                    throw new InvalidDataException("İndirilen zip'teki sürüm (" + KartPaketi.Surum(paket) +
                                                   ") surum.txt'deki ile (" + s.Surum + ") aynı değil.");
                return paket;
            }
            finally
            {
                try { File.Delete(gecici); } catch (Exception) { }
            }
        }
    }

    // =====================================================================
    //  Uygulamanın kendisini internetten (aynı GitHub deposundan) güncelleme.
    //    uygulama.txt -> SURUM=1.2
    //                    EXE=MakroKlavye-1.2.exe
    //                    SHA256=<exe'nin SHA-256 özeti>
    //                    IMZA=<geliştiricinin gizli anahtarıyla imza (base64)>
    //                    NOT=Yenilikler (tek satır)
    //  İmza GuncellemeAnahtari.Acik ile doğrulanır: depo ele geçirilse bile sahte exe kurulmaz.
    //  Kurulum: çalışan exe'nin adı .eski yapılır, yenisi yerine konur, uygulama yeniden başlar.
    // =====================================================================
    public class UzaktakiUygulama
    {
        public string Surum, Exe, Sha256, Imza, Not;
    }

    public static class UygulamaGuncelleme
    {
        private const int CspTuru = 24;   // PROV_RSA_AES: SHA-256 imzayı destekler

        // İki güncelleme aynı anda yapılmasın: uygulama kapanırken karta dosya gönderimi yarıda kalırsa
        // kartta eski ve yeni dosyalar karışabilir. Kart güncellenirken uygulama güncellemesi bekler,
        // uygulama güncellenirken kart güncellemesi başlamaz (yeni sürüm açılınca yapılır).
        public static volatile bool KartGuncelleniyor, UygulamaGuncelleniyor;

        // Kart güncellemesi bitene kadar bekler (en fazla ~3 dk). Döner: bitti mi
        public static async Task<bool> KartGuncellemesiniBekle(Action bekleniyor = null)
        {
            if (!KartGuncelleniyor) return true;
            if (bekleniyor != null) bekleniyor();
            for (int i = 0; i < 360 && KartGuncelleniyor; i++) await Task.Delay(500);
            return !KartGuncelleniyor;
        }
        private static UzaktakiUygulama onbellek;
        private static DateTime onbellekZamani;

        public static bool Ayarli
        {
            get { return KartGuncelleme.Ayarli && GuncellemeAnahtari.Acik.Length > 0; }
        }

        // Çalışan uygulamanın sürümü: "1.1" (yapı numarası varsa "1.1.2")
        public static string Surum
        {
            get { return Yazi(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        private static string Yazi(Version v)
        {
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }

        private static Version Normal(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        // a, b'den büyük mü ("1.2" > "1.1.0.0")
        public static bool Buyuk(string a, string b)
        {
            Version x, y;
            return Version.TryParse(a, out x) && Version.TryParse(b, out y) && Normal(x) > Normal(y);
        }

        public static string ExeSurumu(string exeYolu)
        {
            return Yazi(AssemblyName.GetAssemblyName(exeYolu).Version);
        }

        public static bool DahaYeni(string internet)
        {
            Version a;
            if (internet == null || !Version.TryParse(internet, out a)) return false;
            return Normal(a) > Normal(Assembly.GetExecutingAssembly().GetName().Version);
        }

        private static string ImzaMetni(string surum, string sha256)
        {
            return "MAKRO_KLAVYE_UYGULAMA|" + surum + "|" + sha256;
        }

        private static string Sha256(byte[] veri)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(veri).Select(b => b.ToString("x2")));
        }

        public static async Task<UzaktakiUygulama> DenetleAsync(bool tazele = false)
        {
            if (!Ayarli) return null;
            if (!tazele && onbellek != null && DateTime.Now - onbellekZamani < TimeSpan.FromMinutes(10)) return onbellek;

            string metin = await KartGuncelleme.MetinOku("uygulama.txt");
            if (metin == null) return null;

            try
            {
                {
                    UzaktakiUygulama u = new UzaktakiUygulama();
                    foreach (string ham in metin.Replace("\r", "").Split('\n'))
                    {
                        int eq = ham.IndexOf('=');
                        if (eq <= 0) continue;
                        string anahtar = ham.Substring(0, eq).Trim().TrimStart('\uFEFF'), deger = ham.Substring(eq + 1).Trim();
                        if (anahtar == "SURUM") u.Surum = deger;
                        else if (anahtar == "EXE") u.Exe = deger;
                        else if (anahtar == "SHA256") u.Sha256 = deger.ToLowerInvariant();
                        else if (anahtar == "IMZA") u.Imza = deger;
                        else if (anahtar == "NOT") u.Not = deger;
                    }

                    Version v;
                    if (u.Surum == null || !Version.TryParse(u.Surum, out v) ||
                        u.Exe == null || !u.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        u.Exe.Contains("/") || u.Exe.Contains("\\") || u.Exe.Contains("..") ||
                        u.Sha256 == null || !Regex.IsMatch(u.Sha256, "^[0-9a-f]{64}$") || string.IsNullOrEmpty(u.Imza))
                        return null;

                    onbellek = u;
                    onbellekZamani = DateTime.Now;
                    return u;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // İndirir ve doğrular (özet + imza). Sorun varsa hata fırlatır.
        public static async Task<byte[]> IndirAsync(UzaktakiUygulama u)
        {
            byte[] exe;
            try { exe = await KartGuncelleme.Istemci.GetByteArrayAsync(KartGuncelleme.Adres(u.Exe)); }
            catch (Exception ex) { throw new IOException("İndirilemedi: " + ex.Message, ex); }

            if (Sha256(exe) != u.Sha256)
                throw new InvalidDataException("İndirilen dosya bozuk (doğrulama tutmadı). Biraz sonra tekrar dene.");

            bool gecerli;
            try
            {
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(new CspParameters(CspTuru)))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(GuncellemeAnahtari.Acik);
                    gecerli = rsa.VerifyData(Encoding.UTF8.GetBytes(ImzaMetni(u.Surum, u.Sha256)), "SHA256",
                                             Convert.FromBase64String(u.Imza));
                }
            }
            catch (Exception) { gecerli = false; }

            if (!gecerli)
                throw new InvalidDataException("Güncellemenin imzası tutmuyor, güvenlik için kurulmadı.");
            return exe;
        }

        // Çalışan exe'yi yenisiyle değiştirip uygulamayı yeniden başlatır
        // Güncellemeyi kuran geçici kopya (%TEMP% içinde; yeni sürüm açılınca silinir)
        public static string KurucuYolu
        {
            get { return Path.Combine(Path.GetTempPath(), "MakroKlavye-guncelleme.exe"); }
        }

        // Yeni exe %TEMP%'e yazılır ve "--kur" ile başlatılır. O, bu kopya kapanınca asıl exe'nin
        // İÇERİĞİNİ yenisiyle değiştirir (dosya silinip yeniden oluşturulmadığı için masaüstündeki
        // yeri, kısayollar ve görev çubuğu sabitlemesi bozulmaz), sonra uygulamayı yeniden açar.
        public static void Kur(byte[] exe, string surum, string not = null)
        {
            string hedef = Application.ExecutablePath;

            // Exe'ye yazma izni var mı (Program Files gibi yerlerde yönetici izni gerekir).
            // Çalışan exe kullanımda olduğu için açılamaz (IOException); bu normal, izin hatası değil.
            try
            {
                using (new FileStream(hedef, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
            }
            catch (UnauthorizedAccessException)
            {
                throw new IOException("Uygulamanın dosyasına yazılamıyor (" + Path.GetDirectoryName(hedef) +
                                      "). Exe'yi Masaüstü ya da Belgeler gibi bir klasöre taşıyıp tekrar dene.");
            }
            catch (IOException) { }

            string kurucu = KurucuYolu;
            File.WriteAllBytes(kurucu, exe);

            // Gerçekten bu uygulamanın beklenen sürümü mü
            Version v;
            try { v = AssemblyName.GetAssemblyName(kurucu).Version; }
            catch (Exception) { v = null; }
            Version beklenen;
            if (v == null || !Version.TryParse(surum, out beklenen) || Normal(v) != Normal(beklenen))
            {
                try { File.Delete(kurucu); } catch (Exception) { }
                throw new InvalidDataException("İndirilen dosyanın sürümü uygulama.txt ile aynı değil.");
            }

            UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukYazma, "Uygulama " + surum + " sürümüne güncellendi");
            UygulamaAyarlari.BekleyenYenilik = Yazi(Normal(beklenen)) + "|" + (not ?? "");   // yeni sürüm açılınca gösterilir

            System.Diagnostics.Process.Start(kurucu, Program.KurArgumani + " \"" + hedef + "\"");
            Application.Exit();
        }

        // ---------------------------------------------------------
        // Yayın hazırlama (yalnızca geliştiricinin bilgisayarında)
        // ---------------------------------------------------------
        public static string AnahtarDosyasi
        {
            get { return Path.Combine(UygulamaAyarlari.Klasor, "yayin-anahtari.xml"); }
        }

        // Exe'nin bulunduğu yerden yukarı doğru MakroKeyboard.csproj'un klasörünü arar (bulamazsa null)
        public static string ProjeKlasoru()
        {
            string k = Application.StartupPath;
            for (int i = 0; i < 6 && k != null; i++)
            {
                if (File.Exists(Path.Combine(k, "MakroKeyboard.csproj"))) return k;
                k = Path.GetDirectoryName(k);
            }
            return null;
        }

        // Yeni anahtar çifti: gizlisi AnahtarDosyasi'na, açığı projedeki GuncellemeAnahtari.cs'ye.
        // Döner: açık anahtar. projeyeYazildi: GuncellemeAnahtari.cs güncellendi mi
        public static string AnahtarOlustur(out bool projeyeYazildi)
        {
            string acik;
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048, new CspParameters(CspTuru)))
            {
                rsa.PersistKeyInCsp = false;
                Directory.CreateDirectory(UygulamaAyarlari.Klasor);
                File.WriteAllText(AnahtarDosyasi, rsa.ToXmlString(true));
                acik = rsa.ToXmlString(false);
            }

            projeyeYazildi = false;
            string proje = ProjeKlasoru();
            string dosya = proje != null ? Path.Combine(proje, "GuncellemeAnahtari.cs") : null;
            if (dosya != null && File.Exists(dosya))
            {
                string metin = File.ReadAllText(dosya);
                string yeni = Regex.Replace(metin, "public const string Acik = \"[^\"]*\";",
                                            "public const string Acik = \"" + acik + "\";");
                if (yeni != metin)
                {
                    File.WriteAllText(dosya, yeni, new UTF8Encoding(true));
                    projeyeYazildi = true;
                }
            }
            return acik;
        }

        // Gizli anahtardaki açık kısım (anahtar yoksa null)
        public static string AcikAnahtar()
        {
            if (!File.Exists(AnahtarDosyasi)) return null;
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(new CspParameters(CspTuru)))
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(File.ReadAllText(AnahtarDosyasi));
                return rsa.ToXmlString(false);
            }
        }

        // exe'yi imzalar; çıkış klasörüne MakroKlavye-<sürüm>.exe ve uygulama.txt yazar. Döner: sürüm
        public static string YayinHazirla(string exeYolu, string klasor, string not)
        {
            string surum = Yazi(AssemblyName.GetAssemblyName(exeYolu).Version);
            byte[] exe = File.ReadAllBytes(exeYolu);
            string sha = Sha256(exe);

            string imza;
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(new CspParameters(CspTuru)))
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(File.ReadAllText(AnahtarDosyasi));
                imza = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(ImzaMetni(surum, sha)), "SHA256"));
            }

            string ad = "MakroKlavye-" + surum + ".exe";
            Directory.CreateDirectory(klasor);

            // Önceki yayınlardan kalan exe'ler silinir; klasörde yalnızca yüklenecek iki dosya kalır
            foreach (string eski in Directory.GetFiles(klasor, "MakroKlavye-*.exe"))
            {
                if (string.Equals(Path.GetFileName(eski), ad, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(eski); } catch (Exception) { }
            }
            File.Copy(exeYolu, Path.Combine(klasor, ad), true);
            File.WriteAllText(Path.Combine(klasor, "uygulama.txt"),
                "SURUM=" + surum + "\n" +
                "EXE=" + ad + "\n" +
                "SHA256=" + sha + "\n" +
                "IMZA=" + imza + "\n" +
                "NOT=" + (not ?? "").Replace("\r", " ").Replace("\n", " ").Trim() + "\n",
                new UTF8Encoding(false));
            return surum;
        }
    }
}
