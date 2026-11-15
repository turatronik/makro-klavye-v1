using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Düşük seviye (WH_KEYBOARD_LL) klavye kancası.
    // Çalışırken basılan her tuşu Windows'a ULAŞMADAN yakalar; böylece kısayol kaydederken
    // Alt+F4 uygulamayı kapatmaz, Alt+Tab pencere değiştirmez, Windows+D masaüstüne gitmez.
    // Sadece "Etkin" true döndürürken tuşları yutar, aksi halde hiçbir şeye karışmaz.
    //
    // Yakalanamayanlar: Ctrl+Alt+Del ve Windows+L (Windows bunları kancalardan korur).
    public class KlavyeKancasi : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint LLKHF_INJECTED = 0x10;

        private delegate IntPtr DusukSeviyeIslem(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, DusukSeviyeIslem lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Çöp toplayıcı temsilciyi silmesin diye alanda tutulur
        private readonly DusukSeviyeIslem islem;
        private IntPtr kanca = IntPtr.Zero;
        private bool ctrl, shift, alt, win;

        // true dönerse tuşlar yutulur ve TusBasildi tetiklenir
        public Func<bool> Etkin;

        // (ctrl, shift, alt, win, tuş) - yalnızca değiştirici olmayan bir tuşa basılınca
        public event Action<bool, bool, bool, bool, Keys> TusBasildi;

        public bool Acik { get { return kanca != IntPtr.Zero; } }

        public KlavyeKancasi()
        {
            islem = KancaIslemi;
        }

        public void Baslat()
        {
            if (Acik) return;

            // O an basılı olan değiştiricilerden başla
            Keys m = Control.ModifierKeys;
            ctrl = (m & Keys.Control) != 0;
            shift = (m & Keys.Shift) != 0;
            alt = (m & Keys.Alt) != 0;
            win = false;

            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule modul = p.MainModule)
            {
                kanca = SetWindowsHookEx(WH_KEYBOARD_LL, islem, GetModuleHandle(modul.ModuleName), 0);
            }
        }

        public void Durdur()
        {
            if (kanca == IntPtr.Zero) return;

            UnhookWindowsHookEx(kanca);
            kanca = IntPtr.Zero;
        }

        public void Dispose()
        {
            Durdur();
        }

        private IntPtr KancaIslemi(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (Etkin == null || Etkin()))
            {
                KBDLLHOOKSTRUCT kb = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));

                // Başka programların gönderdiği (sentetik) tuşlara karışma
                if ((kb.flags & LLKHF_INJECTED) == 0)
                {
                    int mesaj = wParam.ToInt32();
                    bool basildi = (mesaj == WM_KEYDOWN || mesaj == WM_SYSKEYDOWN);
                    Keys tus = (Keys)kb.vkCode;
                    bool degistirici = true;

                    switch (tus)
                    {
                        case Keys.LControlKey:
                        case Keys.RControlKey:
                        case Keys.ControlKey:
                            ctrl = basildi; break;

                        case Keys.LShiftKey:
                        case Keys.RShiftKey:
                        case Keys.ShiftKey:
                            shift = basildi; break;

                        case Keys.LMenu:
                        case Keys.RMenu:
                        case Keys.Menu:
                            alt = basildi; break;

                        case Keys.LWin:
                        case Keys.RWin:
                            win = basildi; break;

                        default:
                            degistirici = false; break;
                    }

                    if (basildi && !degistirici && TusBasildi != null)
                        TusBasildi(ctrl, shift, alt, win, tus);

                    return (IntPtr)1;    // Windows'a iletme: tuş yutuldu
                }
            }

            return CallNextHookEx(kanca, nCode, wParam, lParam);
        }
    }

    // Düşük seviye (WH_MOUSE_LL) fare kancası: makro kaydında tıklama ve tekerleği yakalar.
    // Etkin true iken tıklamalar ve tekerlek Windows'a ULAŞMAZ (kayıt sırasında başka yere tıklanmaz);
    // Gecir(nokta) true dönen yerlere (örn. Durdur düğmesi) tıklama olduğu gibi gider.
    public class FareKancasi : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const uint LLMHF_INJECTED = 0x01;

        private delegate IntPtr DusukSeviyeIslem(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NOKTA { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public NOKTA pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, DusukSeviyeIslem lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly DusukSeviyeIslem islem;
        private IntPtr kanca = IntPtr.Zero;
        private bool solYutuldu, sagYutuldu, ortaYutuldu;   // basışı yutulan tuşun bırakılması da yutulur
        private int tekerlekBirikim;

        public Func<bool> Etkin;
        public Func<System.Drawing.Point, bool> Gecir;

        public event Action<MouseButtons> Tiklandi;   // Left / Right / Middle
        public event Action<int> TekerlekDondu;       // + yukarı, - aşağı (çentik)

        public bool Acik { get { return kanca != IntPtr.Zero; } }

        public FareKancasi()
        {
            islem = KancaIslemi;
        }

        public void Baslat()
        {
            if (Acik) return;
            solYutuldu = sagYutuldu = ortaYutuldu = false;
            tekerlekBirikim = 0;
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule modul = p.MainModule)
                kanca = SetWindowsHookEx(WH_MOUSE_LL, islem, GetModuleHandle(modul.ModuleName), 0);
        }

        public void Durdur()
        {
            if (kanca == IntPtr.Zero) return;
            UnhookWindowsHookEx(kanca);
            kanca = IntPtr.Zero;
        }

        public void Dispose()
        {
            Durdur();
        }

        private IntPtr KancaIslemi(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int mesaj = wParam.ToInt32();

                // Bırakma: basışı yutulduysa bu da yutulur (Etkin o arada kapansa bile)
                if (mesaj == WM_LBUTTONUP && solYutuldu) { solYutuldu = false; return (IntPtr)1; }
                if (mesaj == WM_RBUTTONUP && sagYutuldu) { sagYutuldu = false; return (IntPtr)1; }
                if (mesaj == WM_MBUTTONUP && ortaYutuldu) { ortaYutuldu = false; return (IntPtr)1; }

                bool ilgili = mesaj == WM_LBUTTONDOWN || mesaj == WM_RBUTTONDOWN || mesaj == WM_MBUTTONDOWN ||
                              mesaj == WM_MOUSEWHEEL;

                if (ilgili && (m.flags & LLMHF_INJECTED) == 0 && (Etkin == null || Etkin()) &&
                    (Gecir == null || !Gecir(new System.Drawing.Point(m.pt.x, m.pt.y))))
                {
                    switch (mesaj)
                    {
                        case WM_LBUTTONDOWN: solYutuldu = true; Bildir(MouseButtons.Left); break;
                        case WM_RBUTTONDOWN: sagYutuldu = true; Bildir(MouseButtons.Right); break;
                        case WM_MBUTTONDOWN: ortaYutuldu = true; Bildir(MouseButtons.Middle); break;
                        case WM_MOUSEWHEEL:
                            // Hassas tekerlek / dokunmatik yüzey 120'den küçük adımlar gönderir: biriktirilir
                            tekerlekBirikim += (short)((m.mouseData >> 16) & 0xFFFF);
                            int centik = tekerlekBirikim / 120;
                            tekerlekBirikim -= centik * 120;
                            Action<int> t = TekerlekDondu;
                            if (t != null && centik != 0) t(centik);
                            break;
                    }
                    return (IntPtr)1;   // Windows'a iletme
                }
            }
            return CallNextHookEx(kanca, nCode, wParam, lParam);
        }

        private void Bildir(MouseButtons tus)
        {
            Action<MouseButtons> h = Tiklandi;
            if (h != null) h(tus);
        }
    }
}
