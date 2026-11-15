using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using IWshRuntimeLibrary;   // masaüstü / Başlat menüsü kısayolu
using File = System.IO.File;
using System.Drawing;

namespace MakroKeyboard
{
    internal static class Program
    {
        // Aynı anda tek kopya: ikinci kopya iki uygulamanın aynı seri portu kapışmasına yol açar.
        // İkinci kez açılınca yeni kopya kapanır, çalışan kopyanın penceresi (tepsideyse de) öne gelir.
        private const string MutexAdi = "Local\\MakroKlavye_TekKopya";
        private const string YenidenArgumani = "--yeniden";
        public const string KurArgumani = "--kur";        // %TEMP%'teki yeni sürüm: --kur "<asıl exe>"
        private const string SilArgumani = "--sil";       // kurulu kopya, indirilen exe'yi siler: --sil "<yol>"
        public const string KaldirArgumani = "--kaldir";  // Windows'un uygulama listesindeki Kaldır

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string ad);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
        private const int ASFW_ANY = -1;

        // Çalışan kopyaya "pencereni göster" mesajı (Form1.WndProc yakalar)
        public static readonly int GosterMesaji = RegisterWindowMessage("MakroKlavye_PencereyiGoster");

        /// <summary>
        /// Uygulamanın ana girdi noktası.
        /// </summary>
        [STAThread]
        static void Main()
        {
            string[] argumanlar = Environment.GetCommandLineArgs();

            // Beklenmedik hatalar: sessizce kapanmak yerine pencere + günlük
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => HataPenceresi.Goster(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => HataPenceresi.Goster(e.ExceptionObject as Exception, true);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                HataPenceresi.Kaydet(e.Exception);
                e.SetObserved();
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Windows'un "Yüklü uygulamalar" listesinden Kaldır
            if (argumanlar.Contains(KaldirArgumani))
            {
                using (Mutex kilit = new Mutex(false, MutexAdi))
                {
                    bool sahip;
                    try { sahip = kilit.WaitOne(0); }
                    catch (AbandonedMutexException) { sahip = true; }

                    if (!sahip)
                    {
                        MessageBox.Show("Makro Klavye şu an açık. Önce kapat (tepsideyse sağ tık > Çıkış), sonra tekrar kaldır.",
                                        "Makro Klavye", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    try
                    {
                        using (KaldirForm f = new KaldirForm())
                            if (f.ShowDialog() == DialogResult.OK) Kurulum.Kaldir(f.VerileriSil);
                    }
                    finally { kilit.ReleaseMutex(); }
                }
                return;
            }

            // Güncelleme kurucusu olarak açıldıysa: asıl exe'yi güncelle, onu başlat, çık
            int kur = Array.IndexOf(argumanlar, KurArgumani);
            if (kur >= 0 && kur + 1 < argumanlar.Length)
            {
                GuncellemeyiKur(argumanlar[kur + 1]);
                return;
            }

            using (Mutex kilit = new Mutex(false, MutexAdi))
            {
                // Yeniden başlatılırken eski kopyanın kapanması beklenir
                bool sahip;
                try { sahip = kilit.WaitOne(argumanlar.Contains(YenidenArgumani) ? 10000 : 0); }
                catch (AbandonedMutexException) { sahip = true; }   // önceki kopya çöktü

                if (!sahip)
                {
                    // Windows açılışında (--tepsi) sessizce çık; elle açıldıysa çalışan pencereyi öne getir
                    if (!argumanlar.Contains("--tepsi"))
                    {
                        AllowSetForegroundWindow(ASFW_ANY);
                        PostMessage(HWND_BROADCAST, GosterMesaji, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }

                // Güncellemeden kalan eski exe: önceki kopya kilidi bıraktıktan sonra da dosyayı
                // bir süre kullanımda tutabildiği için birkaç saniye boyunca yeniden denenir
                EskiExeyiTemizle();

                // Kurulumdan sonra: indirilen exe silinir (yerine masaüstünde kısayol var)
                int sil = Array.IndexOf(argumanlar, SilArgumani);
                if (sil >= 0 && sil + 1 < argumanlar.Length) Kurulum.IndirileniSil(argumanlar[sil + 1]);

                if (Kurulum.KuruluMu) Kurulum.KaydiGuncelle();   // Windows'un uygulama listesi (sürüm vb.)

                // İndirilen exe'den açıldıysa: bilgisayara kur, kurulu kopyayı aç, bu kopya kapansın
                bool kuruldu = false;
                try { kuruldu = Kurulum.GerekirseKur(argumanlar); }
                catch (Exception ex)
                {
                    MessageBox.Show("Kurulamadı: " + ex.Message + "\n\nUygulama bu dosyadan açılacak.", "Makro Klavye",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (kuruldu)
                {
                    kilit.ReleaseMutex();
                    Process.Start(Kurulum.ExeYolu, YenidenArgumani + " " + SilArgumani + " \"" + Application.ExecutablePath + "\"");
                    return;
                }

                try
                {
                    Application.Run(new Form1());
                }
                finally
                {
                    kilit.ReleaseMutex();
                }
            }
        }

        // Eski uygulama kapanınca (kilidi bırakınca) asıl exe'nin içeriğini bu kopyayla değiştirir
        private static void GuncellemeyiKur(string hedef)
        {
            Exception sonHata = null;
            bool tamam = false;

            using (Mutex kilit = new Mutex(false, MutexAdi))
            {
                bool sahip;
                try { sahip = kilit.WaitOne(20000); }
                catch (AbandonedMutexException) { sahip = true; }

                try
                {
                    byte[] yeni = System.IO.File.ReadAllBytes(Application.ExecutablePath);

                    // Eski kopya kapanmış olsa da Windows dosyayı kısa bir süre kullanımda tutabilir
                    for (int deneme = 0; deneme < 40 && !tamam; deneme++)
                    {
                        try
                        {
                            System.IO.File.WriteAllBytes(hedef, yeni);   // aynı dosyanın içi yenilenir
                            tamam = true;
                        }
                        catch (Exception ex)
                        {
                            sonHata = ex;
                            Thread.Sleep(500);
                        }
                    }
                }
                catch (Exception ex) { sonHata = ex; }
                finally
                {
                    if (sahip) kilit.ReleaseMutex();
                }
            }

            if (!tamam)
                MessageBox.Show("Güncelleme kurulamadı: " + (sonHata != null ? sonHata.Message : "bilinmeyen hata") +
                                "\n\nUygulamanın önceki sürümü açılacak.", "Makro Klavye",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            try { Process.Start(hedef, YenidenArgumani); }
            catch (Exception) { }
        }

        private static void EskiExeyiTemizle()
        {
            string exe = Application.ExecutablePath;
            string[] dosyalar;
            try
            {
                dosyalar = System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(exe), System.IO.Path.GetFileName(exe) + "*.eski")
                                              .Concat(new[] { exe + ".yeni", UygulamaGuncelleme.KurucuYolu }).ToArray();
            }
            catch (Exception) { return; }
            if (!dosyalar.Any(System.IO.File.Exists)) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                for (int deneme = 0; deneme < 40; deneme++)   // en fazla ~20 sn
                {
                    bool kalan = false;
                    foreach (string d in dosyalar)
                    {
                        if (!System.IO.File.Exists(d)) continue;
                        try
                        {
                            System.IO.File.SetAttributes(d, System.IO.FileAttributes.Normal);
                            System.IO.File.Delete(d);
                        }
                        catch (Exception) { kalan = true; }
                    }
                    if (!kalan) return;
                    Thread.Sleep(500);
                }
            });
        }

        // Application.Restart yerine: yeni kopya, bu kopya kapanıp kilidi bırakana kadar bekler
        public static void YenidenBaslat()
        {
            Process.Start(Application.ExecutablePath, YenidenArgumani);
            Application.Exit();
        }
    }

    // =====================================================================
    //  Bilgisayara kurulum: exe %LOCALAPPDATA%\Programs\MakroKlavye içine kopyalanır,
    //  masaüstüne ve Başlat menüsüne kısayol konur. Güncellemeler yalnızca kurulu exe'yi
    //  değiştirir; masaüstündeki kısayol aynı kaldığı için yeri de değişmez.
    //  (Yönetici izni gerekmez, kurulum programı yoktur: uygulama kendini kopyalar.)
    // =====================================================================
    internal static class Kurulum
    {
        private const string KisayolAdi = "Makro Klavye.lnk";

        public static string Klasor
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Programs", "MakroKlavye");
            }
        }

        public static string ExeYolu
        {
            get { return Path.Combine(Klasor, "MakroKlavye.exe"); }
        }

        public static bool KuruluMu
        {
            get
            {
                return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(ExeYolu),
                                     StringComparison.OrdinalIgnoreCase);
            }
        }

        // true: kurulu kopya hazır, onu başlatıp bu kopyayı kapatmalı
        public static bool GerekirseKur(string[] argumanlar)
        {
            if (KuruluMu) return false;
            if (argumanlar.Contains("--tepsi")) return false;                 // Windows açılışı: soru sorma
            if (UygulamaGuncelleme.ProjeKlasoru() != null) return false;      // Visual Studio'dan çalışıyor (geliştirici)
            if (UygulamaAyarlari.KurulumReddedildi && !File.Exists(ExeYolu)) return false;

            Version bu = Assembly.GetExecutingAssembly().GetName().Version, kurulu = null;
            bool kuruluVar = File.Exists(ExeYolu);
            if (kuruluVar)
            {
                try { kurulu = AssemblyName.GetAssemblyName(ExeYolu).Version; }
                catch (Exception) { }
            }
            bool kopyala = !kuruluVar || kurulu == null || bu > kurulu;

            string soru = kuruluVar
                ? "Makro Klavye bu bilgisayarda zaten kurulu" + (kopyala ? " (bu dosya daha yeni, kurulu olanı güncellerim)" : "") + ".\n\n" +
                  "Kurulu olan açılsın ve bu indirilen dosya silinsin mi?"
                : "Makro Klavye bilgisayarına kurulsun mu?\n\n" +
                  "Uygulama kendi klasörüne kopyalanır, masaüstüne ve Başlat menüsüne \"Makro Klavye\" kısayolu konur, " +
                  "bu indirilen dosya da silinir. Böylece güncellemelerden sonra masaüstündeki yeri değişmez.\n\n" +
                  "Hayır dersen uygulama bu dosyadan çalışmaya devam eder.";

            if (MessageBox.Show(soru, "Makro Klavye", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                if (!kuruluVar) UygulamaAyarlari.KurulumReddedildi = true;
                return false;
            }

            Kopyala(kopyala);
            return true;
        }

        // Exe'yi kurulum klasörüne kopyalar (kopyala=false: yalnızca kısayol ve kayıtlar), kısayolları koyar
        public static void Kopyala(bool kopyala = true)
        {
            if (kopyala)
            {
                Directory.CreateDirectory(Klasor);
                File.Copy(Application.ExecutablePath, ExeYolu, true);
            }

            KisayollariOlustur();
            KaydiGuncelle();
            if (UygulamaAyarlari.WindowsIleBaslat) UygulamaAyarlari.BaslatYolunuAyarla(ExeYolu);
            UygulamaAyarlari.KurulumReddedildi = false;
        }

        // Ayarlar > Uygulama > BİLGİSAYARA KUR: kopyalar, kurulu kopyayı açar, bu kopyayı kapatır
        public static void KurVeYenidenAc()
        {
            Kopyala(true);
            Process.Start(ExeYolu, "--yeniden --sil \"" + Application.ExecutablePath + "\"");
            Application.Exit();
        }

        // ---------- Windows "Yüklü uygulamalar" kaydı (kullanıcıya özel, yönetici izni gerekmez) ----------
        private const string KaldirmaAnahtari = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MakroKlavye";

        public static void KaydiGuncelle()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KaldirmaAnahtari))
                {
                    if (k == null) return;
                    k.SetValue("DisplayName", "Makro Klavye");
                    k.SetValue("DisplayVersion", UygulamaGuncelleme.Surum);
                    k.SetValue("DisplayIcon", ExeYolu + ",0");
                    k.SetValue("Publisher", KartGuncelleme.Kullanici);
                    k.SetValue("InstallLocation", Klasor);
                    k.SetValue("UninstallString", "\"" + ExeYolu + "\" " + Program.KaldirArgumani);
                    k.SetValue("URLInfoAbout", "https://github.com/" + KartGuncelleme.Kullanici + "/" + KartGuncelleme.Depo);
                    k.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    if (File.Exists(ExeYolu))
                        k.SetValue("EstimatedSize", (int)(new FileInfo(ExeYolu).Length / 1024), Microsoft.Win32.RegistryValueKind.DWord);
                    if (k.GetValue("InstallDate") == null)
                        k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
            }
            catch (Exception) { }
        }

        // Kısayollar, kayıtlar ve kurulum klasörü silinir; verileriSil: ayarlar, profiller, makrolar da.
        // Kurulu exe çalışırken silinemediği için klasör, uygulama kapandıktan sonra arka planda silinir.
        public static void Kaldir(bool verileriSil)
        {
            string[] kisayollar =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), KisayolAdi),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), KisayolAdi)
            };
            foreach (string k in kisayollar)
                try { if (File.Exists(k)) File.Delete(k); } catch (Exception) { }

            // Tuşlara atanmış uygulama kısayolları (Ctrl+Alt+harf) da kalkar
            try
            {
                string tusKisayollari = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MakroKeyboard");
                if (Directory.Exists(tusKisayollari)) Directory.Delete(tusKisayollari, true);
            }
            catch (Exception) { }

            try { UygulamaAyarlari.WindowsIleBaslat = false; } catch (Exception) { }
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(KaldirmaAnahtari, false); } catch (Exception) { }

            if (verileriSil)
            {
                UygulamaAyarlari.YazmayiDurdur();   // kapanırken ayar dosyası yeniden yazılmasın
                try { Directory.Delete(UygulamaAyarlari.Klasor, true); } catch (Exception) { }
            }

            // Kurulum klasörü: bu uygulama kapandıktan birkaç saniye sonra silinir
            string silinecek = KuruluMu || Directory.Exists(Klasor) ? Klasor : null;
            if (silinecek != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe",
                        "/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"" + silinecek + "\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                catch (Exception) { }
            }

            MessageBox.Show("Makro Klavye kaldırıldı." +
                            (verileriSil ? "" : "\n\nAyarların, profillerin ve makroların bu bilgisayarda duruyor; yeniden kurarsan geri gelir.") +
                            "\n\nKlavyedeki tuş ayarları klavyenin kendisinde kayıtlı, onlara dokunulmadı.",
                            "Makro Klavye", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Exit();
        }

        // Masaüstü ve Başlat menüsü kısayolu (varsa üzerine yazılır: masaüstündeki yeri korunur)
        public static void KisayollariOlustur()
        {
            WshShell kabuk = new WshShell();
            string[] yerler =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), KisayolAdi),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), KisayolAdi)
            };

            foreach (string yol in yerler)
            {
                IWshShortcut k = (IWshShortcut)kabuk.CreateShortcut(yol);
                k.TargetPath = ExeYolu;
                k.WorkingDirectory = Klasor;
                k.IconLocation = ExeYolu + ",0";
                k.Description = "Makro Klavye";
                k.Save();
            }
        }

        // Kurulumdan sonra eski (indirilen) exe silinir; yalnızca gerçekten bu uygulamanın exe'siyse
        public static void IndirileniSil(string yol)
        {
            try
            {
                if (!yol.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(yol)) return;
                if (string.Equals(Path.GetFullPath(yol), Path.GetFullPath(ExeYolu), StringComparison.OrdinalIgnoreCase)) return;
                if (AssemblyName.GetAssemblyName(yol).Name != Assembly.GetExecutingAssembly().GetName().Name) return;
            }
            catch (Exception) { return; }

            System.Threading.Tasks.Task.Run(() =>
            {
                for (int deneme = 0; deneme < 40 && File.Exists(yol); deneme++)   // eski kopya kapanana kadar
                {
                    try { File.Delete(yol); }
                    catch (Exception) { Thread.Sleep(500); }
                }
            });
        }
    }

    // =====================================================================
    //  Beklenmedik hata penceresi: ayrıntı hatalar.txt'ye ve günlüğe yazılır, kopyalanabilir
    // =====================================================================
    internal class HataPenceresi : TemaliPencere
    {
        private static bool acik;
        private readonly string ayrinti;

        public static string Dosya
        {
            get { return Path.Combine(UygulamaAyarlari.Klasor, "hatalar.txt"); }
        }

        private static string Ayrinti(Exception ex)
        {
            return DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "  Makro Klavye " + UygulamaGuncelleme.Surum +
                   "  Windows " + Environment.OSVersion.Version + "\r\n" + (ex != null ? ex.ToString() : "bilinmeyen hata");
        }

        // Yalnızca kaydeder (pencere açmadan)
        public static string Kaydet(Exception ex)
        {
            string metin = Ayrinti(ex);
            try
            {
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari,
                    "Uygulama hatası: " + (ex != null ? ex.GetType().Name + ": " + ex.Message : "bilinmeyen"));

                Directory.CreateDirectory(UygulamaAyarlari.Klasor);
                if (File.Exists(Dosya) && new FileInfo(Dosya).Length > 200 * 1024) File.Delete(Dosya);
                File.AppendAllText(Dosya, metin + "\r\n\r\n");
            }
            catch (Exception) { }
            return metin;
        }

        public static void Goster(Exception ex, bool olumcul)
        {
            string metin = Kaydet(ex);
            if (acik) return;          // arka arkaya gelen hatalar için tek pencere
            acik = true;
            try
            {
                using (HataPenceresi f = new HataPenceresi(metin, olumcul))
                    f.ShowDialog();
            }
            catch (Exception) { }
            finally { acik = false; }
        }

        private HataPenceresi(string metin, bool olumcul)
            : base("Bir hata oldu", "\uE783", 520, 380)
        {
            ayrinti = metin;
            TopMost = true;

            Yazi(olumcul
                    ? "Makro Klavye beklenmedik bir hatayla karşılaştı ve kapanması gerekiyor."
                    : "Makro Klavye beklenmedik bir hatayla karşılaştı. Çalışmaya devam edebilirsin.",
                 22, 58, 476, 22, 10.5f, true, Tema.Yazi);
            Yazi("Ayarların güvende. Aşağıdaki ayrıntıyı kopyalayıp gönderirsen sorunu bulmak kolaylaşır " +
                 "(hatalar.txt dosyasına da yazıldı).", 22, 84, 476, 36, 9f, false, Tema.SolukYazi);

            YuvarlakPanel kutu = new YuvarlakPanel
            {
                Location = new Point(20, 126),
                Size = new Size(480, 180),
                Yaricap = 10,
                DolguRengi = Tema.Kart,
                KenarRengi = Tema.Kenar
            };
            TextBox txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Tema.Kart,
                ForeColor = Tema.Yazi,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(10, 8),
                Size = new Size(460, 164),
                Text = metin,
                TabStop = false
            };
            kutu.Controls.Add(txt);
            Controls.Add(kutu);

            Dugme("KOPYALA", 20, 320, 120, 42, false, () =>
            {
                try { Clipboard.SetText(ayrinti); } catch (Exception) { }
            });
            if (!olumcul)
            {
                Dugme("UYGULAMAYI KAPAT", 230, 320, 160, 42, false, () => { DialogResult = DialogResult.Abort; Application.Exit(); });
                Dugme("DEVAM ET", 400, 320, 100, 42, true, () => DialogResult = DialogResult.OK);
            }
            else
            {
                Dugme("KAPAT", 380, 320, 120, 42, true, () => DialogResult = DialogResult.OK);
            }
        }
    }

    // =====================================================================
    //  Kaldırma onayı
    // =====================================================================
    internal class KaldirForm : TemaliPencere
    {
        private readonly CheckBox chkVeri;

        public bool VerileriSil
        {
            get { return chkVeri.Checked; }
        }

        public KaldirForm()
            : base("Makro Klavye'yi kaldır", "\uE74D", 440, 270)
        {
            Yazi("Uygulama, masaüstü ve Başlat menüsü kısayolları, tuşlara atanmış uygulama kısayolları ve " +
                 "\"Windows açılınca başlat\" kaydı silinecek.", 22, 58, 396, 54, 9.5f, false, Tema.Yazi);
            Yazi("Klavyedeki tuş ayarlarına dokunulmaz; klavye başka bilgisayarda aynen çalışır.",
                 22, 116, 396, 36, 9f, false, Tema.SolukYazi);

            chkVeri = new CheckBox
            {
                Text = "Ayarlarımı, profillerimi, makrolarımı ve yedeklerimi de sil",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Tema.Yazi,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(22, 160),
                Cursor = Cursors.Hand
            };
            Controls.Add(chkVeri);

            Dugme("İptal", 196, 210, 96, 42, false, () => DialogResult = DialogResult.Cancel);
            Dugme("KALDIR", 300, 210, 120, 42, true, () => DialogResult = DialogResult.OK);
        }
    }
}
