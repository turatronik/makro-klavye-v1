using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MakroKeyboard
{
    // Klavyeyle USB seri (CircuitPython konsolu) üzerinden konuşur.
    // Arka planda sürekli satır okur:
    //   "TUS n" / "ENC +1|-1" satırları canlı olay olarak yayınlanır,
    //   diğer satırlar (MK1, OK, HATA, KEY=değer, SON) istek-cevap için sıraya girer.
    // Protokol kart tarafındaki settings_server.py ile aynı.
    public class SeriBaglanti : IDisposable
    {
        public const string Kimlik = "MK1";   // kartın PING cevabı (settings_server.py ile aynı)

        private static readonly Regex AyarSatiri = new Regex(@"^([A-Z][A-Z0-9_]*)=(.*)$");

        private SerialPort port;
        private Thread okuyucu;
        private volatile bool calisiyor;
        private readonly object istekKilidi = new object();
        private readonly BlockingCollection<string> yanitlar = new BlockingCollection<string>();

        public event Action<int, string> TusBasildi;   // tuşa basıldı (0-9, SHORT/LONG/DOUBLE)
        public event Action<int> EncoderDondu;     // +1 / -1
        public event Action<string> LedModDegisti; // kartta LED tuşuyla mod değişti
        public event Action<bool> UykuDegisti;     // kartta uyku engelleyici açıldı / kapandı (kart 2.3+)
        public event Action Koptu;                 // bağlantı beklenmedik şekilde koptu

        public bool Bagli { get { return calisiyor && port != null && port.IsOpen; } }
        public string PortAdi { get { SerialPort p = port; return p != null ? p.PortName : ""; } }

        // ---------------------------------------------------------
        // Bağlanma: tüm seri portlara PING atar, MK1 cevabı veren ilk porta bağlanır
        // ---------------------------------------------------------
        public bool Baglan()
        {
            return Baglan(SerialPort.GetPortNames());
        }

        // Daha önce klavyenin bağlandığı portlar (en yeni önce): önce bunlar denenir, bağlanma hızlanır
        public IList<string> BilinenPortlar = new List<string>();

        // Yalnızca verilen portları dener (örn. yeni takılan kart)
        public bool Baglan(IEnumerable<string> portlar)
        {
            Kapat();

            // Bilinen portlar öne: kart oradaysa diğer portlarda beklemeden hemen bağlanır
            List<string> liste = portlar.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<string> bilinen = (BilinenPortlar ?? new List<string>())
                .Where(b => liste.Contains(b, StringComparer.OrdinalIgnoreCase)).ToList();
            liste = bilinen.Concat(liste.Where(p => !bilinen.Contains(p, StringComparer.OrdinalIgnoreCase))).ToList();

            foreach (string ad in liste)
            {
                SerialPort p = null;

                try
                {
                    p = new SerialPort(ad, 115200);
                    p.Encoding = new UTF8Encoding(false);   // kart Türkçe harfleri UTF-8 gönderir
                    p.ReadTimeout = 300;
                    p.WriteTimeout = 500;
                    p.NewLine = "\n";
                    p.DtrEnable = true;
                    p.Open();

                    Thread.Sleep(150);
                    p.DiscardInBuffer();
                    p.WriteLine("PING");

                    if (SatirBekle(p, Kimlik, 1200))
                    {
                        port = p;
                        OkuyucuyuBaslat();
                        return true;
                    }
                }
                catch (Exception) { }

                if (p != null)
                {
                    try { p.Dispose(); } catch (Exception) { }
                }
            }

            return false;
        }

        private static bool SatirBekle(SerialPort p, string hedef, int ms)
        {
            DateTime bitis = DateTime.Now.AddMilliseconds(ms);

            while (DateTime.Now < bitis)
            {
                try
                {
                    if (p.ReadLine().Trim() == hedef) return true;
                }
                catch (TimeoutException) { }
            }
            return false;
        }

        // ---------------------------------------------------------
        // Arka plan okuyucusu
        // ---------------------------------------------------------
        private void OkuyucuyuBaslat()
        {
            Temizle();
            calisiyor = true;

            okuyucu = new Thread(OkuDongusu);
            okuyucu.IsBackground = true;
            okuyucu.Name = "SeriOkuyucu";
            okuyucu.Start();
        }

        private void OkuDongusu()
        {
            SerialPort p = port;

            while (calisiyor && p != null)
            {
                try
                {
                    string satir = p.ReadLine().TrimEnd('\r', '\n');
                    Isle(satir);
                }
                catch (TimeoutException) { }
                catch (Exception)
                {
                    bool beklenmedik = calisiyor;   // Kapat() çağrıldıysa zaten false
                    Kapat();

                    if (beklenmedik)
                    {
                        Action h = Koptu;
                        if (h != null) h();
                    }
                    return;
                }
            }
        }

        private void Isle(string satir)
        {
            int n;

            if (satir.StartsWith("TUS "))
            {
                string[] parca = satir.Substring(4).Trim().Split(' ');
                if (int.TryParse(parca[0], out n))
                {
                    Action<int, string> h = TusBasildi;
                    if (h != null) h(n, parca.Length > 1 ? parca[1] : "SHORT");
                }
                return;
            }

            if (satir.StartsWith("LEDMOD "))
            {
                Action<string> h = LedModDegisti;
                if (h != null) h(satir.Substring(7).Trim());
                return;
            }

            if (satir.StartsWith("UYKU "))
            {
                Action<bool> h = UykuDegisti;
                if (h != null) h(satir.Substring(5).Trim() == "1");
                return;
            }

            if (satir.StartsWith("ENC "))
            {
                if (int.TryParse(satir.Substring(4).Trim(), out n))
                {
                    Action<int> h = EncoderDondu;
                    if (h != null) h(n);
                }
                return;
            }

            yanitlar.Add(satir);
        }

        // ---------------------------------------------------------
        // İstek - cevap
        // ---------------------------------------------------------
        public bool Ping()
        {
            if (!Bagli) return false;

            lock (istekKilidi)
            {
                Temizle();

                if (Yaz("PING") && YanitBekle(s => s == Kimlik, 1500) != null)
                    return true;

                Kapat();
                return false;
            }
        }

        // Karttaki tüm ayarları okur (KEY=değer). Hata olursa null.
        public Dictionary<string, string> Oku()
        {
            return SatirlariOku("OKU", 3000);
        }

        // Kart sağlığı (boş alan, açılış nedeni ...). Eski kart yazılımı TANI bilmez: null
        public Dictionary<string, string> Tanilama()
        {
            return SatirlariOku("TANI", 2500);
        }

        // Komutu gönderir, SON gelene kadar KEY=değer satırlarını toplar. Zaman aşımında null.
        private Dictionary<string, string> SatirlariOku(string komut, int ms)
        {
            if (!Bagli) return null;

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz(komut)) return null;

                Dictionary<string, string> sonuc = new Dictionary<string, string>();
                DateTime bitis = DateTime.Now.AddMilliseconds(ms);

                while (true)
                {
                    int kalan = (int)(bitis - DateTime.Now).TotalMilliseconds;
                    if (kalan <= 0) return null;

                    string satir;
                    if (!yanitlar.TryTake(out satir, kalan)) return null;

                    if (satir == "SON") return sonuc;

                    Match m = AyarSatiri.Match(satir);   // koddan gelen diğer print satırları atlanır
                    if (m.Success) sonuc[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
        }

        // Ayarları karta yazar. Başarılıysa null, değilse hata metni döner.
        public string Kaydet(Dictionary<string, string> ayarlar)
        {
            if (!Bagli) return "Bağlantı yok";

            StringBuilder sb = new StringBuilder();

            foreach (var kv in ayarlar.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!Regex.IsMatch(kv.Key, @"^[A-Z][A-Z0-9_]*$")) continue;
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            }

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz("KAYDET") || !Yaz(Kodla(sb.ToString()))) return "Yazılamadı";
                return OkBekle(4000);
            }
        }

        // LED'i kaydetmeden anında ayarlar (önizleme). mod: rainbow, color...; renk: RRGGBB; parlaklık: 0-100;
        // hız: % (100 normal); akış: 3 renk akışının renkleri "RRGGBB,RRGGBB,RRGGBB".
        // Eski kart yazılımı fazladan gelen hız / akış kısmını yok sayar.
        public string LedUygula(string mod, string renkHex, int parlaklik, int hiz = 100, string akis = null)
        {
            if (!Bagli) return "Bağlantı yok";

            string satir = "LED " + mod + " " + renkHex + " " + parlaklik + " " + hiz;
            if (!string.IsNullOrEmpty(akis)) satir += " " + akis.Replace(" ", "");

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz(satir)) return "Yazılamadı";
                return OkBekle(2000);
            }
        }

        // ---------------------------------------------------------
        // Kart yazılımı güncelleme
        // ---------------------------------------------------------
        // Karttaki yazılım sürümü; eski yazılım SURUM bilmiyorsa null
        public string Surum()
        {
            if (!Bagli) return null;

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz("SURUM")) return null;

                string s = YanitBekle(x => x.StartsWith("SURUM "), 1500);
                return s == null ? null : s.Substring(6).Trim();
            }
        }

        // Kartın kimliği (işlemci seri numarası, kart 2.4+). Eski yazılımda null.
        public string KartKimligi()
        {
            if (!Bagli) return null;

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz("KIMLIK")) return null;

                string s = YanitBekle(x => x.StartsWith("KIMLIK "), 1500);
                return s == null ? null : s.Substring(7).Trim();
            }
        }

        // Dosyayı base64 parçalar halinde karta yazar. null = başarılı
        public string DosyaGonder(string ad, byte[] veri, Action<int> yuzde)
        {
            if (!Bagli) return "Bağlantı yok";

            const int ParcaBoyutu = 384;      // base64 karşılığı 512 karakter

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz("DOSYA " + ad)) return "Yazılamadı";

                string hata = OkBekle(3000);
                if (hata != null) return hata;

                for (int i = 0; i < veri.Length; i += ParcaBoyutu)
                {
                    int n = Math.Min(ParcaBoyutu, veri.Length - i);
                    if (!Yaz("PARCA " + Convert.ToBase64String(veri, i, n))) return "Yazılamadı";

                    hata = OkBekle(3000);
                    if (hata != null)
                    {
                        Yaz("DOSYA_IPTAL");
                        return hata;
                    }

                    if (yuzde != null) yuzde((i + n) * 100 / Math.Max(1, veri.Length));
                }

                if (!Yaz("DOSYA_BITIR " + veri.Length)) return "Yazılamadı";
                return OkBekle(5000);
            }
        }

        // Kart yazılımını yeniden başlatır (yeni dosyalar yüklenir)
        // Karttaki yedek yazılımın (son güncellemeden önceki) sürümü; yedek yoksa "" , eski kartta null (kart 2.4+)
        public string YedekSurumu()
        {
            if (!Bagli) return null;

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz("YEDEK")) return null;

                string s = YanitBekle(x => x.StartsWith("YEDEK "), 1500);
                if (s == null) return null;
                s = s.Substring(6).Trim();
                return s == "YOK" ? "" : s;
            }
        }

        // Kart yedekteki yazılıma döner ve yeniden başlar. null = başarılı
        public string GeriAl()
        {
            if (!Bagli) return "Klavye bağlı değil";

            lock (istekKilidi)
            {
                Temizle();
                if (!Yaz("GERI_AL")) return "Klavyeye yazılamadı";
                return OkBekle(3000);
            }
        }

        public void YenidenBaslat()
        {
            if (!Bagli) return;

            lock (istekKilidi)
            {
                Temizle();
                if (Yaz("YENIDEN")) OkBekle(2000);
            }
        }

        // ---------------------------------------------------------
        // Yardımcılar
        // ---------------------------------------------------------
        // Tek satıra kodla: \ -> \\ ,  yeni satır -> \n
        // ASCII dışı harfler \uXXXX olarak gider (kart 2.0 çözer)
        private static string Kodla(string metin)
        {
            string k = metin.Replace("\r", "").Replace("\\", "\\\\").Replace("\n", "\\n");
            StringBuilder sb = new StringBuilder(k.Length);
            foreach (char c in k)
            {
                if (c < 128) sb.Append(c);
                else sb.Append("\\u").Append(((int)c).ToString("x4"));
            }
            return sb.ToString();
        }

        private bool Yaz(string satir)
        {
            try
            {
                port.WriteLine(satir);
                return true;
            }
            catch (Exception)
            {
                Kapat();
                return false;
            }
        }

        // OK ya da HATA satırını bekler: null = başarılı, aksi halde hata metni
        private string OkBekle(int ms)
        {
            string s = YanitBekle(x => x == "OK" || x.StartsWith("HATA"), ms);

            if (s == null) return "Klavye yanıt vermedi";
            return s == "OK" ? null : s;
        }

        private string YanitBekle(Func<string, bool> uygun, int ms)
        {
            DateTime bitis = DateTime.Now.AddMilliseconds(ms);

            while (true)
            {
                int kalan = (int)(bitis - DateTime.Now).TotalMilliseconds;
                if (kalan <= 0) return null;

                string satir;
                if (!yanitlar.TryTake(out satir, kalan)) return null;
                if (uygun(satir)) return satir;
            }
        }

        private void Temizle()
        {
            string x;
            while (yanitlar.TryTake(out x)) { }
        }

        private void Kapat()
        {
            calisiyor = false;

            SerialPort p = port;
            port = null;

            if (p != null)
            {
                try { p.Dispose(); } catch (Exception) { }
            }
        }

        public void Dispose()
        {
            Kapat();
        }
    }
}