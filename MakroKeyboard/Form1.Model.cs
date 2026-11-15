using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Form1'in parçası: klavye modeli.
    //   v1 (MK1): 9 tuş + 1 encoder.  Slot 0-8 = tuş 1-9, slot 9 = encoder'a basma (BUTTON9), ENCODER_CW/CCW.
    //   v2 (MK2): 6 tuş + 2 encoder + ekran.  Slot 0-5 = tuş 1-6, slot 6 = SAĞ encoder'a basma (BUTTON6,
    //             çevirme ENCODER2_CW/CCW), slot 7 = SOL encoder'a basma (BUTTON7, çevirme ENCODER_CW/CCW).
    // Her iki modelde de son slot "ana" encoder'dır (ENCODER_CW/CCW); v2'de ayrıca slot 6 ikinci encoder'dır.
    // Model bağlanınca kartın PING cevabından (MK1 / MK2) öğrenilir ve hatırlanır.
    public partial class Form1
    {
        private static readonly string[] PinlerV2 = { "GP6", "GP7", "GP8", "GP14", "GP15", "GP26", "GP5", "GP2" };

        // Kart bağlanana kadar gösterilen varsayılanlar (v2 code.py'deki DEFAULT_SETTINGS ile aynı)
        private static readonly string[] VarsayilanKomutlarV2 =
            { "MEDIA_PREVIOUS", "MEDIA_PLAY_PAUSE", "MEDIA_NEXT", "CTRL+C", "CTRL+V", "WINDOWS+D", "EKRAN", "MUTE" };

        private const int SagEncoderSlotu = 6;   // yalnızca v2

        private string model = UygulamaAyarlari.Model == SeriBaglanti.ModelV2 ? SeriBaglanti.ModelV2 : SeriBaglanti.ModelV1;

        private bool V2
        {
            get { return model == SeriBaglanti.ModelV2; }
        }

        private string[] ModelPinleri
        {
            get { return V2 ? PinlerV2 : Pinler; }
        }

        private string[] ModelVarsayilanlari
        {
            get { return V2 ? VarsayilanKomutlarV2 : VarsayilanKomutlar; }
        }

        // ---------- Slot bilgileri ----------
        private bool EncoderMi(int i)
        {
            return i == atamalar.Count - 1 || SagEncoderMi(i);
        }

        private bool SagEncoderMi(int i)
        {
            return V2 && i == SagEncoderSlotu;
        }

        // Çevirme ayarlarının öneki: ENCODER_CW / ENCODER2_CW
        private string EncOnek(int i)
        {
            return SagEncoderMi(i) ? "ENCODER2" : "ENCODER";
        }

        private string EncVarsayilan(int i, bool saga)
        {
            if (SagEncoderMi(i)) return saga ? "PROFIL_SONRAKI" : "PROFIL_ONCEKI";
            return saga ? "VOLUME_UP" : "VOLUME_DOWN";
        }

        private string EncSaga(int i)
        {
            return Al(EncOnek(i) + "_CW", EncVarsayilan(i, true));
        }

        private string EncSola(int i)
        {
            return Al(EncOnek(i) + "_CCW", EncVarsayilan(i, false));
        }

        // Kullanım sayacının öneki: ENC_CW / ENC2_CW
        private string EncSayacOneki(int i)
        {
            return SagEncoderMi(i) ? "ENC2" : "ENC";
        }

        // Encoder slotları: önce ana (sol) encoder, v2'de ardından sağ encoder
        private IEnumerable<int> EncoderSlotlari()
        {
            if (V2) return new[] { atamalar.Count - 1, SagEncoderSlotu };
            return new[] { atamalar.Count - 1 };
        }

        // "ENCODER", "SOL ENCODER", "SAĞ ENCODER"
        private string EncoderEtiketi(int i)
        {
            if (!V2) return "ENCODER";
            return SagEncoderMi(i) ? "SAĞ ENCODER" : "SOL ENCODER";
        }

        // Tuş kartındaki ad: "Encoder", "Sol encoder", "Sağ encoder"
        private string EncoderKisaAdi(int i)
        {
            if (!V2) return "Encoder";
            return SagEncoderMi(i) ? "Sağ encoder" : "Sol encoder";
        }

        // Seçili tuş encoder değilse ana encoder (çevirme modu menüsü için)
        private int SeciliEncoder
        {
            get { return EncoderMi(secili) ? secili : atamalar.Count - 1; }
        }

        // Tuş kartlarının yerleşimi (gerçek klavyeye benzer). Dönen değer: (sütun, satır)
        //   v1: üstte 1-2-3-4 + encoder, altta 5-6-7-8 + LED tuşu
        //   v2: üstte sol encoder · 1-2-3 · sağ encoder, altta 4-5-6
        private Point SlotKonumu(int i)
        {
            if (!V2) return KartKonumu(i);
            if (i == 7) return new Point(0, 0);   // sol encoder
            if (i == 6) return new Point(4, 0);   // sağ encoder
            return new Point(1 + i % 3, i / 3);
        }

        private void AtamalariOlustur()
        {
            atamalar.Clear();
            string[] pinler = ModelPinleri, varsayilan = ModelVarsayilanlari;
            int tusSayisi = V2 ? 6 : 9;
            for (int i = 0; i < pinler.Length; i++)
                atamalar.Add(new Atama(i < tusSayisi ? (i + 1).ToString() : "⟳", varsayilan[i], pinler[i]));
        }

        // =================================================================
        //  MODEL DEĞİŞİNCE (başka türde klavye bağlandı)
        // =================================================================
        // Döner: model değişti mi
        private bool ModelUygula(string yeni)
        {
            if (yeni != SeriBaglanti.ModelV2) yeni = SeriBaglanti.ModelV1;
            if (yeni == model) return false;

            // Kullanım sayaçları ve tuş adları modele göre ayrı dosyalarda durur
            IstatistikKaydet();
            model = yeni;
            UygulamaAyarlari.Model = yeni;

            istatistik.Clear();
            foreach (var kv in UygulamaAyarlari.IstatistikOku()) istatistik[kv.Key] = kv.Value;
            tusAdlari = UygulamaAyarlari.TusAdlariOku();
            kartAyarlari = new Dictionary<string, string>();
            geriAlYigini.Clear();
            pano = null;

            AtamalariOlustur();

            // Tuş kartlarını yeni düzene göre baştan kur
            SuspendLayout();
            try
            {
                if (atamaKarti != null)
                {
                    Controls.Remove(atamaKarti);
                    atamaKarti.Dispose();
                    atamaKarti = null;
                }
                kutucuklar.Clear();
                AtamaKartiOlustur();
                KlavyeResmiGuncelle();
            }
            finally { ResumeLayout(true); }

            secili = 0;
            basisTuru = 0;
            vurguIndex = -1;
            GeriAlGorunum();
            KutuculariYenile();
            SecimYap(0);

            UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukBaglanti,
                                        V2 ? "Makro Klavye v2 (6 tuş, 2 encoder, ekran) bağlandı"
                                           : "Makro Klavye v1 (9 tuş, encoder) bağlandı");
            return true;
        }

        // Üstteki klavye resmi: v1 fotoğrafı ya da v2 görseli
        private Image v1Resmi;

        private void KlavyeResmiGuncelle()
        {
            if (pictureBox1 == null) return;
            if (v1Resmi == null) v1Resmi = pictureBox1.Image;

            if (!V2)
            {
                if (v1Resmi != null) pictureBox1.Image = v1Resmi;
                return;
            }

            Image v2 = null;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MakroKeyboard.Resources.klavye_v2.png"))
                    if (s != null) v2 = new Bitmap(Image.FromStream(s));
            }
            catch (Exception) { v2 = null; }

            pictureBox1.Image = v2 ?? v1Resmi;
        }

        // =================================================================
        //  PROFİL / YEDEK METNİNİ MODELE ÇEVİRME
        // =================================================================
        // Profil metninde "#MODEL=MK2" satırı v2'de yazılmış demektir; satır yoksa v1'dir.
        // Başka modelin profili yüklenirken tuş numaraları bu klavyeye uyarlanır:
        //   v1 -> v2: tuş 1-6 aynı, encoder'a basma (9) -> sol encoder'a basma (7), tuş 7-9 atlanır
        //   v2 -> v1: tuş 1-6 aynı, sol encoder'a basma (7) -> encoder'a basma (9),
        //             sağ encoder (6 ve ENCODER2_*) atlanır
        private static readonly Regex SlotSatiri =
            new Regex(@"^(BUTTON|#APP|#AD|#RENK|#SIMGE|PC_APP|PC_AD|PC_RENK|PC_SIMGE)(\d+)(_LONG|_DOUBLE)?(=.*)$");

        internal static string ProfilModeli(string metin)
        {
            if (metin == null) return SeriBaglanti.ModelV1;
            foreach (string ham in metin.Split('\n'))
            {
                string s = ham.Trim();
                if (s.StartsWith("#MODEL=", StringComparison.Ordinal))
                    return s.Substring(7).Trim() == SeriBaglanti.ModelV2 ? SeriBaglanti.ModelV2 : SeriBaglanti.ModelV1;
            }
            return SeriBaglanti.ModelV1;
        }

        private string ModeleCevir(string metin)
        {
            return ModeleCevir(metin, model);
        }

        internal static string ModeleCevir(string metin, string hedefModel)
        {
            if (metin == null) return null;
            string kaynakModel = ProfilModeli(metin);
            if (kaynakModel == hedefModel) return metin;

            bool v1denV2ye = hedefModel == SeriBaglanti.ModelV2;
            StringBuilder sb = new StringBuilder();

            foreach (string ham in metin.Split('\n'))
            {
                string satir = ham.TrimEnd('\r');
                string s = satir.Trim();

                if (s.StartsWith("#MODEL=", StringComparison.Ordinal)) continue;

                // Sağ encoder'ın çevirme ayarları v1'de yok
                if (!v1denV2ye && s.StartsWith("ENCODER2_", StringComparison.Ordinal)) continue;

                Match m = SlotSatiri.Match(s);
                if (m.Success)
                {
                    int n = int.Parse(m.Groups[2].Value);
                    int yeni;
                    if (v1denV2ye)
                    {
                        if (n <= 5) yeni = n;
                        else if (n == 9) yeni = 7;
                        else continue;          // v1'in 7., 8. ve LED tuşu
                    }
                    else
                    {
                        if (n <= 5) yeni = n;
                        else if (n == 7) yeni = 9;
                        else continue;          // v2'nin sağ encoder'ı
                    }
                    satir = m.Groups[1].Value + yeni + m.Groups[3].Value + m.Groups[4].Value;
                }
                sb.Append(satir).Append('\n');
            }

            if (v1denV2ye)
            {
                string sonuc = sb.ToString();
                sonuc = MetneModelEkle(sonuc, SeriBaglanti.ModelV2);
                // v1 profilinde sağ encoder yok: v2'nin varsayılanları kalsın
                if (!Regex.IsMatch(sonuc, @"^BUTTON6=", RegexOptions.Multiline)) sonuc += "BUTTON6=EKRAN\n";
                if (!Regex.IsMatch(sonuc, @"^ENCODER2_CW=", RegexOptions.Multiline)) sonuc += "ENCODER2_CW=MEDIA_NEXT\n";
                if (!Regex.IsMatch(sonuc, @"^ENCODER2_CCW=", RegexOptions.Multiline)) sonuc += "ENCODER2_CCW=MEDIA_PREVIOUS\n";
                return sonuc;
            }
            return sb.ToString();
        }

        // İlk satırdan sonra "#MODEL=MK2" ekler (v1'de hiçbir şey eklenmez)
        internal static string MetneModelEkle(string metin, string modelAdi)
        {
            if (modelAdi != SeriBaglanti.ModelV2 || metin == null) return metin;
            if (ProfilModeli(metin) == SeriBaglanti.ModelV2) return metin;
            int ilk = metin.IndexOf('\n');
            if (ilk < 0) return metin + "\n#MODEL=" + modelAdi + "\n";
            return metin.Substring(0, ilk + 1) + "#MODEL=" + modelAdi + "\n" + metin.Substring(ilk + 1);
        }

        // =================================================================
        //  v2 EKRANI: çalan şarkı, kapak, ses ve profil adı
        // =================================================================
        private readonly EkranYayini ekranYayini = new EkranYayini();
        private readonly List<string> ekranKuyrugu = new List<string>();   // henüz gönderilmemiş satırlar
        private const int EkranTurBoyutu = 3000;   // bir turda (0,5 sn) en fazla bu kadar karakter: kart PING'e geç kalmasın
        private Timer ekranZamanlayici;
        private bool ekranGonderiliyor;
        private string sonEkranBasligi;

        private void EkranYayininiKur()
        {
            ekranZamanlayici = new Timer { Interval = 500 };
            ekranZamanlayici.Tick += async (s, e) => await EkranTik();
            ekranZamanlayici.Start();
        }

        // Bağlanınca / kart yeniden başlayınca her şey baştan gönderilsin
        private void EkranYayininiSifirla()
        {
            ekranYayini.Sifirla();
            sonEkranBasligi = null;
            ekranKuyrugu.Clear();
        }

        // Ekranın başlığı: etkin profilin adı (büyük harf), profil yoksa MAKRO KLAVYE
        private static string EkranBasligi(string profil)
        {
            return string.IsNullOrEmpty(profil) ? "MAKRO KLAVYE" : profil.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        // Ekranı beslenecek v2: düzenlenen klavye v2 ise o, değilse arka planda bağlı v2
        private SeriBaglanti sonEkranHedefi;

        private SeriBaglanti EkranHedefi()
        {
            if (!klavyeBagli) return null;
            if (V2 && seri.Bagli) return seri;
            return digerKlavyeler.FirstOrDefault(d => d.Model == SeriBaglanti.ModelV2 && d.Bagli);
        }

        private async Task EkranTik()
        {
            if (ekranGonderiliyor || UygulamaGuncelleme.KartGuncelleniyor) return;
            SeriBaglanti hedef = EkranHedefi();
            if (hedef == null) return;
            if (hedef != sonEkranHedefi)
            {
                EkranYayininiSifirla();       // başka bir v2'ye geçildi: her şey baştan
                sonEkranHedefi = hedef;
            }

            ekranGonderiliyor = true;
            try
            {
                List<string> satirlar = await ekranYayini.SatirlarAsync();

                string baslik = hedef == seri
                    ? EkranBasligi(profilHedefi ?? UygulamaAyarlari.AktifProfil)
                    : EkranBasligi(UygulamaAyarlari.KlavyeninProfili(hedef.KartKimlik));
                if (baslik != sonEkranBasligi)
                {
                    satirlar.Insert(0, "BASLIK\t" + baslik.Replace("\t", " "));
                    sonEkranBasligi = baslik;
                }

                // Yeni şarkı geldiyse eski şarkının gönderilmemiş kapak / konum satırları atılır
                if (satirlar.Any(s => s == "N" || s.StartsWith("M\t")))
                    ekranKuyrugu.RemoveAll(s => s.StartsWith("AR\t") || s.StartsWith("ART\t") || s.StartsWith("POS\t"));
                ekranKuyrugu.AddRange(satirlar);
                if (ekranKuyrugu.Count > 400) ekranKuyrugu.RemoveRange(0, ekranKuyrugu.Count - 400);
                if (ekranKuyrugu.Count == 0) return;

                // Kapak resmi (yaklaşık 100 satır) birkaç tura bölünür
                int adet = 0, boyut = 0;
                while (adet < ekranKuyrugu.Count && (adet == 0 || boyut + ekranKuyrugu[adet].Length < EkranTurBoyutu))
                {
                    boyut += ekranKuyrugu[adet].Length + 1;
                    adet++;
                }
                List<string> parca = ekranKuyrugu.GetRange(0, adet);
                bool gitti = await Task.Run(() => hedef.EkranaGonder(parca));
                if (gitti) ekranKuyrugu.RemoveRange(0, adet);   // gitmediyse sonraki turda yeniden denenir
            }
            catch (Exception) { }
            finally { ekranGonderiliyor = false; }
        }

        // =================================================================
        //  v2 SAĞ ENCODER: SEKMELER (PROFİLLER)
        //  Eski v2 kart yazılımında sağ encoder MEDYA / KISAYOL / SİSTEM / F13-F18 modları arasında
        //  geçiyordu. Artık her sekme bir profil: kart çevirince "PROFIL +1/-1" yazar, uygulama
        //  sıradaki profili yükler. Sekmeler Profil menüsünden düzenlenir / eklenir.
        // =================================================================
        private static readonly string[][] V2Sekmeler =
        {
            // ad, LED / ekran rengi, tuş 1-6
            new[] { "1 Medya",   "FF2878", "MEDIA_PREVIOUS", "MEDIA_PLAY_PAUSE", "MEDIA_NEXT", "MUTE", "MEDIA_STOP", "MEDIA_PLAYER" },
            new[] { "2 Kısayol", "00A0FF", "CTRL+C", "CTRL+V", "CTRL+X", "CTRL+Z", "CTRL+Y", "CTRL+S" },
            new[] { "3 Sistem",  "FF8C00", "WINDOWS+D", "WINDOWS+E", "SHIFT+WINDOWS+S", "CTRL+SHIFT+ESC", "ALT+TAB", "WINDOWS+L" },
            new[] { "4 F13-F18", "5AE63C", "F13", "F14", "F15", "F16", "F17", "F18" }
        };

        private static readonly Regex SekmeAnahtari = new Regex(@"^BUTTON[0-5](_LONG|_DOUBLE)?$");

        private string profilHedefi;          // sağ encoder çevrilirken gidilecek profil (geçiş bitene kadar)
        private bool profilGecisiSuruyor;
        private Timer profilZamanlayici;

        // Sağ encoder'ın gezdiği profiller: bu klavyenin (v2) profil klasöründekiler
        private List<string> SekmeProfilleri()
        {
            return UygulamaAyarlari.Profiller();
        }

        private void ProfilKaydir(int yon)
        {
            if (!V2 || !klavyeBagli) return;

            List<string> adlar = SekmeProfilleri();
            if (adlar.Count == 0)
            {
                Bildir("Sağ encoder profiller (sekmeler) arasında geçer. Önce Profil menüsünden bir profil kaydet.");
                return;
            }

            string su = profilHedefi ?? UygulamaAyarlari.AktifProfil;
            int i = adlar.FindIndex(a => string.Equals(a, su, StringComparison.CurrentCultureIgnoreCase));
            if (i < 0) i = yon > 0 ? -1 : 0;
            i = ((i + yon) % adlar.Count + adlar.Count) % adlar.Count;
            profilHedefi = adlar[i];

            // Başlık hemen değişsin; profil, çevirme durunca (0,5 sn) bir kez yüklenir
            if (seri.EkranaGonder(new[] { "BASLIK\t" + EkranBasligi(profilHedefi).Replace("\t", " ") }))
                sonEkranBasligi = EkranBasligi(profilHedefi);
            Bildir("Sekme: " + profilHedefi);

            if (profilZamanlayici == null)
            {
                profilZamanlayici = new Timer { Interval = 500 };
                profilZamanlayici.Tick += (s, e) =>
                {
                    profilZamanlayici.Stop();
                    ProfilKaydirBitir();
                };
            }
            profilZamanlayici.Stop();
            profilZamanlayici.Start();
        }

        private async void ProfilKaydirBitir()
        {
            string hedef = profilHedefi;
            if (hedef == null) return;

            if (profilGecisiSuruyor)
            {
                profilZamanlayici.Start();     // önceki geçiş bitince
                return;
            }

            profilGecisiSuruyor = true;
            try
            {
                if (!string.Equals(hedef, UygulamaAyarlari.AktifProfil, StringComparison.CurrentCultureIgnoreCase))
                    await ProfilSecAsync(hedef);
            }
            finally
            {
                profilGecisiSuruyor = false;
                if (profilHedefi == hedef) profilHedefi = null;
                sonEkranBasligi = null;        // başlık (geçiş olmadıysa eski profil) yeniden gönderilsin
            }
        }

        // v2 ilk bağlandığında eski kart yazılımındaki dört modu sekme (profil) olarak kurar.
        // Sekmeler şu anki ayarlardan türetilir: yalnızca tuş 1-6 ve renk farklıdır.
        private async Task V2SekmeleriKur()
        {
            if (!V2 || !klavyeBagli || kartAyarlari.Count == 0 || UygulamaAyarlari.V2SekmelerKuruldu) return;

            try
            {
                if (SekmeProfilleri().Count == 0)
                {
                    Dictionary<int, TusAdRenk> encoderAdlari = tusAdlari.Where(k => k.Key >= 6)
                                                                        .ToDictionary(k => k.Key, k => k.Value);
                    foreach (string[] sekme in V2Sekmeler)
                    {
                        if (File.Exists(UygulamaAyarlari.ProfilYolu(sekme[0]))) continue;

                        StringBuilder sb = new StringBuilder();
                        sb.Append("# Makro Klavye v2 sekmesi: ").Append(sekme[0]).Append('\n');
                        sb.Append("#MODEL=").Append(SeriBaglanti.ModelV2).Append('\n');
                        foreach (var kv in kartAyarlari.Where(k => !k.Key.StartsWith(TasinanOnek) && !SekmeAnahtari.IsMatch(k.Key) &&
                                                                    k.Key != "LED_COLOR" && !k.Key.StartsWith("ENCODER2_"))
                                                       .OrderBy(k => k.Key, StringComparer.Ordinal))
                            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                        for (int t = 0; t < 6; t++)
                            sb.Append("BUTTON").Append(t).Append('=').Append(sekme[2 + t]).Append('\n');
                        sb.Append("LED_COLOR=").Append(sekme[1]).Append('\n');
                        sb.Append("ENCODER2_CW=PROFIL_SONRAKI\nENCODER2_CCW=PROFIL_ONCEKI\n");
                        sb.Append(UygulamaAyarlari.TusAdSatirlari(encoderAdlari, "#"));
                        UygulamaAyarlari.ProfilYaz(sekme[0], sb.ToString());
                    }
                }
                UygulamaAyarlari.V2SekmelerKuruldu = true;
            }
            catch (Exception ex)
            {
                Bildir("Sekmeler kurulamadı: " + ex.Message);
                return;
            }

            // Sağ encoder hâlâ eski varsayılandaysa (şarkı değiştir) sekmelere geçsin
            string cw = Al("ENCODER2_CW", "");
            if (cw.Length == 0 || cw == "MEDIA_NEXT")
            {
                await ((IAyarlarSahibi)this).KartAyarlariniYaz(new Dictionary<string, string>
                {
                    { "ENCODER2_CW", "PROFIL_SONRAKI" },
                    { "ENCODER2_CCW", "PROFIL_ONCEKI" }
                }, "Sağ encoder: sekmeler", null);
                KutuculariYenile();
            }

            Bildir("Sağ encoder'ı çevir: Medya · Kısayol · Sistem · F13-F18 sekmeleri arasında geçer. " +
                   "Sekmeler profildir; Profil menüsünden düzenleyip yenisini ekleyebilirsin.");
        }
    }
}
