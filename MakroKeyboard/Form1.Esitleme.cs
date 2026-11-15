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
    // Form1'in parçası: Ayarların klavyeyle taşınması ve uygulama kısayolları
    public partial class Form1
    {
        // =================================================================
        //  AYARLAR KLAVYEYLE BİRLİKTE TAŞINIR
        // =================================================================
        // Uygulama kısayolları ve tuş adları / renkleri klavyenin settings.txt dosyasında da saklanır:
        //   PC_SURUM=1
        //   PC_APP3=<base64: hedef|ad>      (kullanıcı klasörü %USERPROFILE% olarak yazılır)
        //   PC_AD3=<base64: ad>             PC_RENK3=DB2777
        // Kart bu satırları tanımaz, yalnızca saklar ve OKU ile geri gönderir (kart yazılımı değişmedi).
        // Türkçe harfler seri bağlantıda bozulmasın diye metinler base64 ile yazılır.
        // Klavye başka bir bilgisayara takılıp uygulama açılınca bunlar oraya kendiliğinden kurulur.
        private const string TasinanOnek = "PC_";

        // Bu bilgisayarda tuşlara bağlı uygulama / klasör / site kısayolları: tuş -> (hedef, ad)
        private Dictionary<int, KeyValuePair<string, string>> MevcutUygulamalar()
        {
            Dictionary<int, KeyValuePair<string, string>> d = new Dictionary<int, KeyValuePair<string, string>>();

            for (int i = 0; i < atamalar.Count; i++)
            {
                string kisayol = AyrilmisKisayol(i);
                string lnk;

                if (HotkeyNorm(atamalar[i].Kisayol) != kisayol || !uygulamalar.TryGetValue(kisayol, out lnk))
                    continue;

                string hedef = LnkHedefi(lnk);
                if (hedef != null)
                    d[i] = new KeyValuePair<string, string>(hedef, Path.GetFileNameWithoutExtension(lnk));
            }
            return d;
        }

        private static string B64Yaz(string s)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
        }

        private static string B64Oku(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s ?? "")); }
            catch (Exception) { return null; }
        }

        // C:\Users\Ali\... -> %USERPROFILE%\...   (başka bilgisayarda kullanıcı adı farklı olabilir)
        private static string YolKisalt(string hedef)
        {
            string ev = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(ev) && hedef.StartsWith(ev, StringComparison.OrdinalIgnoreCase) &&
                (hedef.Length == ev.Length || hedef[ev.Length] == '\\'))
                return "%USERPROFILE%" + hedef.Substring(ev.Length);
            return hedef;
        }

        private static string YolAc(string hedef)
        {
            const string onek = "%USERPROFILE%";
            if (!hedef.StartsWith(onek, StringComparison.OrdinalIgnoreCase)) return hedef;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + hedef.Substring(onek.Length);
        }

        // Klavyeye yazılacak PC_ satırları
        private static Dictionary<string, string> TasinanAnahtarlar(
            Dictionary<int, KeyValuePair<string, string>> uyg, Dictionary<int, TusAdRenk> adlar)
        {
            Dictionary<string, string> d = new Dictionary<string, string> { { TasinanOnek + "SURUM", "1" } };

            foreach (var kv in uyg)
                d[TasinanOnek + "APP" + kv.Key] = B64Yaz(YolKisalt(kv.Value.Key) + "|" + kv.Value.Value);

            foreach (var kv in adlar)
            {
                if (!string.IsNullOrEmpty(kv.Value.Ad))
                    d[TasinanOnek + "AD" + kv.Key] = B64Yaz(kv.Value.Ad);
                if (!string.IsNullOrEmpty(kv.Value.Renk) && Regex.IsMatch(kv.Value.Renk, "^[0-9A-F]{6}$"))
                    d[TasinanOnek + "RENK" + kv.Key] = kv.Value.Renk;
                if (!string.IsNullOrEmpty(kv.Value.Simge) && Regex.IsMatch(kv.Value.Simge, "^[0-9A-F]{4}$"))
                    d[TasinanOnek + "SIMGE" + kv.Key] = kv.Value.Simge;
            }
            return d;
        }

        // Klavyeden gelen PC_ satırlarını çözer. Klavyede hiç kopya yoksa (eski kart / ilk kez) false.
        private static bool TasinanCoz(Dictionary<string, string> ayar,
                                       out Dictionary<int, KeyValuePair<string, string>> uyg,
                                       out Dictionary<int, TusAdRenk> adlar)
        {
            uyg = new Dictionary<int, KeyValuePair<string, string>>();
            adlar = new Dictionary<int, TusAdRenk>();

            foreach (var kv in ayar)
            {
                Match m = Regex.Match(kv.Key, "^" + TasinanOnek + @"(APP|AD|RENK|SIMGE)(\d)$");
                if (!m.Success) continue;

                int n = int.Parse(m.Groups[2].Value);
                string tur = m.Groups[1].Value;

                if (tur == "RENK")
                {
                    if (!Regex.IsMatch(kv.Value, "^[0-9A-Fa-f]{6}$")) continue;
                    if (!adlar.ContainsKey(n)) adlar[n] = new TusAdRenk();
                    adlar[n].Renk = kv.Value.ToUpperInvariant();
                    continue;
                }

                if (tur == "SIMGE")
                {
                    if (!Regex.IsMatch(kv.Value, "^[0-9A-Fa-f]{4}$")) continue;
                    if (!adlar.ContainsKey(n)) adlar[n] = new TusAdRenk();
                    adlar[n].Simge = kv.Value.ToUpperInvariant();
                    continue;
                }

                string metin = B64Oku(kv.Value);
                if (string.IsNullOrEmpty(metin)) continue;

                if (tur == "AD")
                {
                    if (!adlar.ContainsKey(n)) adlar[n] = new TusAdRenk();
                    adlar[n].Ad = metin;
                }
                else
                {
                    int ayrac = metin.IndexOf('|');
                    if (ayrac <= 0 || ayrac == metin.Length - 1) continue;
                    uyg[n] = new KeyValuePair<string, string>(YolAc(metin.Substring(0, ayrac)), metin.Substring(ayrac + 1));
                }
            }

            return ayar.ContainsKey(TasinanOnek + "SURUM");
        }

        private static bool AyniUygulamalar(Dictionary<int, KeyValuePair<string, string>> a,
                                            Dictionary<int, KeyValuePair<string, string>> b)
        {
            if (a.Count != b.Count) return false;

            foreach (var kv in a)
            {
                KeyValuePair<string, string> o;
                if (!b.TryGetValue(kv.Key, out o) ||
                    !string.Equals(o.Key, kv.Value.Key, StringComparison.OrdinalIgnoreCase) ||
                    o.Value != kv.Value.Value)
                    return false;
            }
            return true;
        }

        private static bool AyniAdlar(Dictionary<int, TusAdRenk> a, Dictionary<int, TusAdRenk> b)
        {
            return UygulamaAyarlari.TusAdSatirlari(a.Where(kv => !kv.Value.Bos).ToDictionary(kv => kv.Key, kv => kv.Value), "") ==
                   UygulamaAyarlari.TusAdSatirlari(b.Where(kv => !kv.Value.Bos).ToDictionary(kv => kv.Key, kv => kv.Value), "");
        }

        // Bağlanınca: klavyede kopya varsa o esastır, bu bilgisayara kurulur.
        // Yoksa (eski kart / ilk kez) bu bilgisayardakiler klavyeye yazılır.
        private async Task KlavyeyleEsitle()
        {
            Dictionary<int, KeyValuePair<string, string>> kartUyg;
            Dictionary<int, TusAdRenk> kartAd;

            if (!TasinanCoz(kartAyarlari, out kartUyg, out kartAd))
            {
                await TasinanKopyayiGuncelle();
                return;
            }

            bool uygFarkli = !AyniUygulamalar(kartUyg, MevcutUygulamalar());
            bool adFarkli = !AyniAdlar(kartAd, tusAdlari);
            if (!uygFarkli && !adFarkli) return;

            string hata = null;
            if (uygFarkli)
            {
                try { ProfilUygulamalariniKur(kartUyg); }
                catch (Exception ex) { hata = ex.Message; }
            }
            if (adFarkli)
            {
                tusAdlari = kartAd;
                UygulamaAyarlari.TusAdlariYaz(tusAdlari);
            }

            KutuculariYenile();
            SecimYap(secili);

            // Bu bilgisayarda bulunmayan program / dosya / klasör
            List<string> eksik = kartUyg.Values
                .Where(u => !WebAdresiMi(u.Key) &&
                            !u.Key.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) &&
                            !System.IO.File.Exists(u.Key) && !Directory.Exists(u.Key))
                .Select(u => u.Value)
                .ToList();

            List<string> parcalar = new List<string>();
            if (uygFarkli && kartUyg.Count > 0) parcalar.Add(kartUyg.Count + " uygulama / klasör / site kısayolu");
            if (adFarkli && kartAd.Count > 0) parcalar.Add(kartAd.Count + " tuş adı");

            string mesaj = parcalar.Count > 0
                ? "Klavyedeki ayarlar bu bilgisayara kuruldu: " + string.Join(", ", parcalar) + "."
                : "Bu bilgisayardaki eski uygulama kısayolları ve tuş adları klavyeye göre güncellendi.";
            if (eksik.Count > 0) mesaj += " Bu bilgisayarda bulunamayanlar: " + string.Join(", ", eksik) + ".";
            if (hata != null) mesaj = "Uygulama kısayolları kurulamadı: " + hata;

            UygulamaAyarlari.GunlukEkle(hata != null || eksik.Count > 0 ? UygulamaAyarlari.GunlukUyari : UygulamaAyarlari.GunlukBilgi,
                                        mesaj.TrimEnd('.'));
            Bildir(mesaj);
            if (tepsi != null && tepsi.Visible && !Visible)
                tepsi.ShowBalloonTip(4000, "Makro Klavye", mesaj, eksik.Count > 0 || hata != null ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        // Klavyedeki kopya bu bilgisayardakinden farklıysa klavyeye yazar (aynıysa hiçbir şey yapmaz)
        private async Task TasinanKopyayiGuncelle()
        {
            if (!klavyeBagli) return;

            Dictionary<string, string> istenen = TasinanAnahtarlar(MevcutUygulamalar(), tusAdlari);
            Dictionary<string, string> mevcut = kartAyarlari
                .Where(kv => kv.Key.StartsWith(TasinanOnek))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            if (istenen.Count == mevcut.Count &&
                istenen.All(kv => mevcut.ContainsKey(kv.Key) && mevcut[kv.Key] == kv.Value))
                return;

            if (!await IslemSirasiniBekle()) return;   // sonraki değişiklikte yeniden denenir

            foreach (string k in mevcut.Keys) kartAyarlari.Remove(k);
            foreach (var kv in istenen) kartAyarlari[kv.Key] = kv.Value;

            string hata = await KartaYaz();
            if (hata != null)
            {
                // Yazılamadı: eski hâline dön ki sonraki değişiklikte yeniden denensin
                foreach (string k in istenen.Keys) kartAyarlari.Remove(k);
                foreach (var kv in mevcut) kartAyarlari[kv.Key] = kv.Value;
            }
        }

        // Klavyeye yazma başarılı: olay günlüğüne yazar ve karttaki güncel durumu Geçmiş'e
        // (otomatik yedek) ekler; son yedekle aynıysa yedek eklenmez. ayrinti: günlükte açıklamanın devamı
        private void OtomatikYedek(string aciklama, string ayrinti = null)
        {
            UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukYazma,
                                        aciklama + (string.IsNullOrEmpty(ayrinti) ? "" : ": " + ayrinti));
            GecmiseEkle(aciklama);
        }

        private void GecmiseEkle(string aciklama)
        {
            string metin = ProfilMetni();
            if (metin != null) UygulamaAyarlari.GecmisEkle(aciklama, metin);
        }

        // Kayıt başarısız olunca son noktayı geri çeker
        private void GeriAlIptal()
        {
            if (geriAliniyor || geriAlYigini.Count == 0) return;
            geriAlYigini.RemoveAt(geriAlYigini.Count - 1);
            GeriAlGorunum();
        }

        private async void GeriAl()
        {
            if (geriAlYigini.Count == 0)
            {
                Bildir("Geri alınacak bir şey yok.");
                return;
            }
            if (!klavyeBagli)
            {
                Bildir("Geri almak için önce klavyeye bağlan.");
                return;
            }

            KeyValuePair<string, string> son = geriAlYigini[geriAlYigini.Count - 1];
            geriAlYigini.RemoveAt(geriAlYigini.Count - 1);
            Bildir("Geri alınıyor: " + son.Key + "...");

            string hata;
            geriAliniyor = true;
            try { hata = await ProfilUygulaAsync(son.Value, "Geri al: " + son.Key); }
            catch (Exception ex) { hata = ex.Message; }
            finally { geriAliniyor = false; }

            if (hata != null)
            {
                geriAlYigini.Add(son);
                Bildir("Geri alınamadı: " + hata);
            }
            else
            {
                Bildir("Geri alındı: " + son.Key);
            }

            GeriAlGorunum();
        }

        private void GeriAlGorunum()
        {
            if (btnGeriAl == null) return;

            bool dolu = geriAlYigini.Count > 0;
            lblGeriAl.ForeColor = dolu ? Lacivert : Tema.Pasif;

            string ipucu = dolu
                ? "Geri al: " + geriAlYigini[geriAlYigini.Count - 1].Key + "  (Ctrl+Z)"
                : "Geri alınacak bir şey yok";
            baloncuk.SetToolTip(btnGeriAl, ipucu);
            foreach (Control c in btnGeriAl.Controls) baloncuk.SetToolTip(c, ipucu);
        }

        // Başlık satırındaki küçük simgeli düğme
        private YuvarlakPanel KucukDugme(Control ebeveyn, string ikon, string yazi, int x, int y, int w, Action tik)
        {
            YuvarlakPanel p = new YuvarlakPanel
            {
                Location = new Point(x, y),
                Size = new Size(w, 28),
                Yaricap = 14,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar2,
                HoverRengi = Tema.Hover
            };
            Etiket(p, ikon, Ikon(9.5f), Lacivert, 8, 0, 22, 28, ContentAlignment.MiddleCenter);
            Etiket(p, yazi, new Font("Segoe UI", 9f, FontStyle.Bold), Lacivert, 30, 0, w - 34, 28,
                   ContentAlignment.MiddleLeft);
            HoverBagla(p);
            TiklaBagla(p, (s, e) => tik());
            ebeveyn.Controls.Add(p);
            return p;
        }

        // =================================================================
        //  UYGULAMA KISAYOLU (.lnk + CTRL+ALT+harf)
        // =================================================================
        // CTRL+ALT+Y, Ctrl+Alt+Y, ALT+CTRL+Y ... hepsini aynı biçime getirir
        private static string HotkeyNorm(string komut)
        {
            if (string.IsNullOrEmpty(komut) || komut.StartsWith("TEXT:")) return komut ?? "";

            bool ctrl = false, shift = false, alt = false, win = false;
            string tus = "";

            foreach (string ham in komut.Split('+'))
            {
                string p = ham.Trim().ToUpperInvariant();
                if (p == "CTRL" || p == "CONTROL") ctrl = true;
                else if (p == "SHIFT") shift = true;
                else if (p == "ALT") alt = true;
                else if (p == "WINDOWS" || p == "WIN") win = true;
                else tus = p;
            }

            List<string> sonuc = new List<string>();
            if (ctrl) sonuc.Add("CTRL");
            if (shift) sonuc.Add("SHIFT");
            if (alt) sonuc.Add("ALT");
            if (win) sonuc.Add("WINDOWS");
            sonuc.Add(tus);
            return string.Join("+", sonuc);
        }

        // Klasördeki .lnk dosyalarını okur: kısayol tuşu -> dosya yolu
        private void UygulamalariTara()
        {
            uygulamalar.Clear();
            uygulamaTurleri.Clear();
            if (!Directory.Exists(UygulamaKlasoru)) return;

            WshShell shell = new WshShell();

            foreach (string dosya in Directory.GetFiles(UygulamaKlasoru, "*.lnk"))
            {
                try
                {
                    IWshShortcut k = (IWshShortcut)shell.CreateShortcut(dosya);
                    if (!string.IsNullOrEmpty(k.Hotkey))
                    {
                        string hk = HotkeyNorm(k.Hotkey);
                        uygulamalar[hk] = dosya;
                        uygulamaTurleri[hk] = HedefTuru(KisayolHedefi(k));
                    }
                }
                catch (Exception) { }
            }
        }

        // "Klasör: İndirilenler", "Web sitesi: Youtube", "Uygulama: Spotify"
        private string UygulamaAciklamasi(string komut)
        {
            string tur;
            if (!uygulamaTurleri.TryGetValue(HotkeyNorm(komut), out tur)) tur = "Uygulama";
            return tur + ": " + UygulamaAdiBul(komut);
        }

        private string UygulamaAdiBul(string komut)
        {
            string yol;
            return uygulamalar.TryGetValue(HotkeyNorm(komut), out yol)
                ? Path.GetFileNameWithoutExtension(yol)
                : "Uygulama";
        }

        // "Uygulama" seçilince: bilgisayardan .exe seç, tuşa otomatik kısayol ata
        private async void UygulamaSec()
        {
            if (basisTuru != 0)
            {
                Bildir("Uygulama, klasör ve site yalnızca kısa basışa (encoder'da basmaya) atanabilir.");
                ModuGeriYukle();
                return;
            }

            string hedef, ad;

            // Yüklü uygulamalar (Microsoft Store dahil) ya da dosyadan .exe
            using (UygulamaSecici secici = new UygulamaSecici())
            {
                if (secici.ShowDialog(this) != DialogResult.OK)
                {
                    ModuGeriYukle();
                    return;
                }
                hedef = secici.SecilenHedef;
                ad = secici.SecilenAd;
            }

            // Bu tuşun önceden belirlenmiş kısayolu (BUTON 1 -> Ctrl+Alt+Y ...)
            string kisayol = AyrilmisKisayol(secili);

            // Başka bir tuşa elle aynı kombinasyon verilmişse çakışır
            for (int j = 0; j < atamalar.Count; j++)
            {
                if (j != secili && HotkeyNorm(atamalar[j].Kisayol) == kisayol)
                {
                    Bildir(Guzel(kisayol) + " şu an BUTON " + (j + 1) + " tarafından kullanılıyor. Önce onu değiştir.");
                    ModuGeriYukle();
                    return;
                }
            }

            bekleyenExe = hedef;
            bekleyenAd = ad;
            komutHam = kisayol;
            GosterUygulama();
            await HemenKaydet(ad);        // ayrıca AYARI KAYDET'e basmaya gerek kalmasın
        }

        // Makro / uygulama penceresinde KAYDET'e basılınca doğrudan klavyeye yaz
        private async Task HemenKaydet(string ne)
        {
            if (!klavyeBagli)
            {
                Bildir(ne + " hazır. Klavyeye bağlanınca AYARI KAYDET'e bas.");
                KaydetDurumu();
                return;
            }
            if (!await IslemSirasiniBekle())
            {
                Bildir(ne + " hazır ama başka bir işlem sürüyor; birazdan AYARI KAYDET'e bas.");
                KaydetDurumu();
                return;
            }

            await AyariKaydetAsync();
        }

        // Kaydedilmemiş değişiklik varsa AYARI KAYDET turuncu olur
        private Label lblKaydedilmedi;

        private void KaydetDurumu()
        {
            if (btnKaydet == null || lblKaydetYazi == null) return;

            bool bekliyor = DegisiklikVar();
            if (lblKaydedilmedi != null) lblKaydedilmedi.Visible = bekliyor;
            lblKaydetYazi.Text = "AYARI KAYDET";
            btnKaydet.Invalidate(true);
        }

        // Göz simgesi: düzenlenen metni gizli yap / göster
        private void MetinGizliDegistir()
        {
            if (!metinModu) return;

            metinGizli = !metinGizli;

            // Kutudaki metin için hemen uygula (kartta kayıtlı olan aynı metinse tuş kartında da gizlenir)
            if (txtKisayol.Text.Length > 0)
                UygulamaAyarlari.GizliAyarla(MetinKodla(txtKisayol.Text), metinGizli);

            KutuGorunumu();
            KutuculariYenile();
            Bildir(metinGizli
                ? "Metin gizlendi: uygulamada ●●●●●● görünür, klavye aynen yazar."
                : "Metin artık görünür.");
        }

        // Tuşun sabit uygulama kısayolu: BUTON 1 -> CTRL+ALT+Y, BUTON 2 -> CTRL+ALT+U ...
        private static string AyrilmisKisayol(int tus)
        {
            return "CTRL+ALT+" + UygulamaTuslari[tus];
        }

        // Komut, uygulama kısayolları için ayrılmış 10 kombinasyondan biri mi?
        private static bool Ayrilmis(string komut)
        {
            string norm = HotkeyNorm(komut);

            for (int i = 0; i < UygulamaTuslari.Length; i++)
                if (norm == AyrilmisKisayol(i)) return true;

            return false;
        }

        private string KisayolDosyasiOlustur(string hedef, string uygulamaAdi, string hotkey)
        {
            Directory.CreateDirectory(UygulamaKlasoru);

            string ad = DosyaAdiTemizle(uygulamaAdi);
            string yol = Path.Combine(UygulamaKlasoru, ad + ".lnk");
            WshShell shell = new WshShell();

            // Aynı uygulama başka bir tuşa da atandıysa o dosyanın üzerine yazma
            for (int n = 2; System.IO.File.Exists(yol); n++)
            {
                IWshShortcut onceki = (IWshShortcut)shell.CreateShortcut(yol);
                if (HotkeyNorm(onceki.Hotkey) == HotkeyNorm(hotkey)) break;
                yol = Path.Combine(UygulamaKlasoru, ad + " (" + n + ").lnk");
            }

            IWshShortcut k = (IWshShortcut)shell.CreateShortcut(yol);

            if (hedef.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) || WebAdresiMi(hedef))
            {
                // Microsoft Store (UWP) uygulamalarının çalıştırılabilir dosyası yoktur:
                // explorer.exe ile Uygulamalar klasöründeki girdi açılır.
                // Web sitesi de explorer.exe ile varsayılan tarayıcıda açılır.
                k.TargetPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                k.Arguments = hedef;
            }
            else
            {
                // .exe, belge ya da klasör: doğrudan açılır
                k.TargetPath = hedef;
                k.Arguments = "";
                k.WorkingDirectory = Directory.Exists(hedef) ? hedef : (Path.GetDirectoryName(hedef) ?? "");
            }

            k.Hotkey = hotkey;
            k.Save();
            return yol;
        }

        private static string DosyaAdiTemizle(string ad)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                ad = ad.Replace(c.ToString(), "");

            ad = ad.Trim();
            return ad.Length == 0 ? "Uygulama" : ad;
        }

        // Kayıttan sonra: yeni uygulama için .lnk oluştur, tuşun ESKİ uygulama kısayolunu sil
        private void UygulamaKisayoluGuncelle(string eskiLnk, string yeniHedef, string yeniAd,
                                              string hotkey, bool uygulamaTusu)
        {
            try
            {
                string yeniLnk = null;
                if (yeniHedef != null) yeniLnk = KisayolDosyasiOlustur(yeniHedef, yeniAd, hotkey);

                // Tuş artık başka bir şey yapıyor ya da uygulaması değişti: eski kısayolu sil
                bool ayniUygulama = uygulamaTusu && yeniHedef == null;
                if (eskiLnk != null && !ayniUygulama &&
                    !string.Equals(eskiLnk, yeniLnk, StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Delete(eskiLnk);
                }
            }
            finally
            {
                bekleyenExe = null;
                bekleyenAd = null;
                UygulamalariTara();
                KutuculariYenile();
                SecimYap(secili);
            }
        }

        private string AdBul(string komut)
        {
            if (komut.Length == 0) return "Atanmadı";
            if (komut.StartsWith("TEXT:")) return "Metin";
            if (komut.StartsWith("MACRO:")) return "Makro";

            int n = HazirIndeks(komut);
            int hazir = Hazirlar.GetLength(0);

            if (n < hazir) return Hazirlar[n, 0];
            if (n == hazir + 2) return UygulamaAdiBul(komut);
            return "Özel";
        }
    }
}
