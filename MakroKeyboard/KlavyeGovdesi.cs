using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // =====================================================================
    //  Ana penceredeki klavye çizimi (16 tuşlu uygulamadaki gibi): metal gövde, vidalar,
    //  tuşların altındaki ışıklı yuvalar (LED efektinin rengini gösterir).
    //  Tuşların kendisi bu kontrolün üstüne konan tuş kutucuklarıdır (TusAlani ile yerleşir).
    //  Yerleşim gerçek klavyeyle aynı: 1-4 üstte, 5-8 altta, sağda üstte encoder, altında 9.
    // =====================================================================
    public class KlavyeGovdesi : Control
    {
        public const int TusG = 105, TusY = 100, Ara = 10, Kenar = 28, SagAra = 16;
        // Gövde renkleri: koyu temada koyu metal, açık temada gümüş alüminyum (gerçek klavye gibi)
        internal static readonly Color GovdeUst = Tema.Koyu ? Color.FromArgb(46, 51, 64) : Color.FromArgb(238, 241, 246);
        internal static readonly Color GovdeAlt = Tema.Koyu ? Color.FromArgb(24, 27, 34) : Color.FromArgb(196, 203, 214);
        internal static readonly Color GovdeKenar = Tema.Koyu ? Color.FromArgb(70, 77, 94) : Color.FromArgb(158, 167, 182);
        internal static readonly Color GovdeIcKenar = Tema.Koyu ? Color.FromArgb(18, 20, 26) : Color.FromArgb(222, 227, 235);
        internal static readonly Color YuvaSonuk = Tema.Koyu ? Color.FromArgb(14, 16, 21) : Color.FromArgb(206, 212, 222);
        internal static readonly Color YuvaKenar = Tema.Koyu ? Color.FromArgb(10, 11, 15) : Color.FromArgb(150, 159, 174);

        public static readonly Size Boyut = new Size(Kenar * 2 + 4 * TusG + 3 * Ara + SagAra + TusG,
                                                     Kenar * 2 + 2 * TusY + Ara);

        // Yuvaların ışığı: boşsa (kapalı / bağlı değil) karanlık
        private Color[] isik = new Color[0];
        private int parlaklik = 100;

        public KlavyeGovdesi()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = Boyut;
        }

        // i: 0-8 tuşlar, 9 encoder
        public static Rectangle TusAlani(int i)
        {
            int sutun, satir;
            if (i == 9) { sutun = 4; satir = 0; }
            else if (i == 8) { sutun = 4; satir = 1; }
            else { sutun = i % 4; satir = i / 4; }

            int x = Kenar + sutun * (TusG + Ara) + (sutun == 4 ? SagAra - Ara : 0);
            int y = Kenar + satir * (TusY + Ara);
            if (i == 9)   // encoder: yuvarlak düğme
            {
                int cap = Math.Min(TusG, TusY);
                return new Rectangle(x + (TusG - cap) / 2, y + (TusY - cap) / 2, cap, cap);
            }
            return new Rectangle(x, y, TusG, TusY);
        }

        public void IsikAyarla(Color[] renkler, int parlaklikYuzde)
        {
            isik = renkler ?? new Color[0];
            parlaklik = Math.Max(0, Math.Min(100, parlaklikYuzde));
            Invalidate();
        }

        // Kart LED modunun yuvalarda görünecek renkleri (16 tuşlu uygulamadaki ışık önizlemesinin sade hâli)
        public static Color[] ModRenkleri(string mod, string renkHex, string akis)
        {
            Func<string, Color> hex = h =>
            {
                try { return ColorTranslator.FromHtml("#" + (h ?? "").Trim().TrimStart('#')); }
                catch (Exception) { return Color.FromArgb(0, 102, 255); }
            };
            Color[] gokkusagi =
            {
                Color.FromArgb(255, 40, 40), Color.FromArgb(255, 200, 0), Color.FromArgb(40, 220, 90),
                Color.FromArgb(0, 170, 255), Color.FromArgb(160, 60, 255)
            };

            switch (mod ?? "")
            {
                case "off": return new Color[0];
                case "white": return new[] { Color.White };
                case "red": return new[] { Color.FromArgb(255, 30, 30) };
                case "green": return new[] { Color.FromArgb(30, 220, 80) };
                case "blue": return new[] { Color.FromArgb(30, 100, 255) };
                case "purple": return new[] { Color.FromArgb(140, 0, 255) };
                case "fire": return new[] { Color.FromArgb(255, 60, 0), Color.FromArgb(255, 160, 20), Color.FromArgb(255, 60, 0) };
                case "police": return new[] { Color.FromArgb(255, 30, 30), Color.FromArgb(30, 80, 255) };
                case "red_purple_white":
                    List<Color> l = new List<Color>();
                    foreach (string p in (akis ?? "").Split(',')) if (p.Trim().Length == 6) l.Add(hex(p));
                    return l.Count >= 2 ? l.ToArray() : new[] { Color.Red, Color.FromArgb(140, 0, 255), Color.White };
                case "rainbow":
                case "colorcycle":
                case "wave":
                case "confetti":
                case "random":
                    return gokkusagi;
                default:
                    return new[] { hex(renkHex) };
            }
        }

        internal static GraphicsPath Yol(RectangleF r, float yaricap)
        {
            return YuvarlakPanel.Yol(r, yaricap);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GovdeCiz(g, Width, Height);

            // --- Işıklı yuvalar: tuşların altı (sol 4x2) ve sağ sütun ---
            Rectangle sol, sag;
            Yuvalar(out sol, out sag);
            Yuva(g, sol);
            Yuva(g, sag);
        }

        // Tuşların altındaki iki ışıklı yuva (sol 4x2 ve sağ sütun)
        public static void Yuvalar(out Rectangle sol, out Rectangle sag)
        {
            sol = Rectangle.Union(TusAlani(0), TusAlani(7));
            sag = Rectangle.Union(TusAlani(8), new Rectangle(TusAlani(8).X, Kenar, TusG, TusY));
            sol.Inflate(9, 9);
            sag.Inflate(9, 9);
        }

        // Koyu metal gövde ve dört vida
        public static void GovdeCiz(Graphics g, int Width, int Height)
        {
            // --- Gövde (koyu metal) ---
            RectangleF govde = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath p = Yol(govde, 22))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(govde, GovdeUst, GovdeAlt, 90f))
                    g.FillPath(b, p);
                using (Pen k = new Pen(GovdeKenar, 1.5f)) g.DrawPath(k, p);
            }
            RectangleF ic = RectangleF.Inflate(govde, -6, -6);
            using (GraphicsPath p = Yol(ic, 17))
            using (Pen k = new Pen(GovdeIcKenar, 1.5f)) g.DrawPath(k, p);

            // --- Vidalar ---
            foreach (PointF v in new[] { new PointF(14, 14), new PointF(Width - 15, 14), new PointF(14, Height - 15), new PointF(Width - 15, Height - 15) })
            {
                RectangleF r = new RectangleF(v.X - 6, v.Y - 6, 12, 12);
                using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(150, 156, 170), Color.FromArgb(70, 75, 88), 45f))
                    g.FillEllipse(b, r);
                using (Pen k = new Pen(Color.FromArgb(40, 44, 54), 1.4f))
                {
                    g.DrawLine(k, v.X - 3.5f, v.Y, v.X + 3.5f, v.Y);
                    g.DrawLine(k, v.X, v.Y - 3.5f, v.X, v.Y + 3.5f);
                }
            }
        }

        private void Yuva(Graphics g, RectangleF r)
        {
            using (GraphicsPath p = Yol(r, 12))
            {
                if (isik.Length == 0)
                {
                    using (SolidBrush b = new SolidBrush(YuvaSonuk)) g.FillPath(b, p);
                }
                else
                {
                    int a = 70 + (int)(150 * parlaklik / 100.0);
                    if (isik.Length == 1)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(a, isik[0]))) g.FillPath(b, p);
                    }
                    else
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(r, isik[0], isik[isik.Length - 1], 0f))
                        {
                            ColorBlend cb = new ColorBlend(isik.Length);
                            for (int i = 0; i < isik.Length; i++)
                            {
                                cb.Colors[i] = Color.FromArgb(a, isik[i]);
                                cb.Positions[i] = i / (float)(isik.Length - 1);
                            }
                            b.InterpolationColors = cb;
                            g.FillPath(b, p);
                        }
                    }
                    // içten koyulaşan kenar: ışık tuşların arasından sızıyormuş gibi
                    using (PathGradientBrush pb = new PathGradientBrush(p))
                    {
                        pb.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        pb.SurroundColors = new[] { Color.FromArgb(90, 0, 0, 0) };
                        pb.FocusScales = new PointF(0.85f, 0.75f);
                        g.FillPath(pb, p);
                    }
                }
                using (Pen k = new Pen(YuvaKenar, 2f)) g.DrawPath(k, p);
            }
        }
    }

    // =====================================================================
    //  Işık sekmesindeki canlı klavye demosu: aynı klavye çizimi, ışık kartın animasyonuyla.
    //  Gerçek klavyede 13 LED tuşların çevresinde bir halka; kuyruklu yıldız saat yönünün tersine döner:
    //  alt sıra soldan sağa (5-8), sağ sütun aşağıdan yukarı (9, encoder), üst sıra sağdan sola (4-1).
    //  Değişmeyen katmanlar (gövde, tuşlar, yazılar) bir kez bitmap'e çizilir; her karede yalnızca ışık
    //  çizilir. Yavaş çizim kareleri atlatır ve dönüş ters yöne gidiyormuş gibi görünür (araba tekerleği etkisi).
    // =====================================================================
    internal class KlavyeDemosu : LedOnizleme
    {
        // Tuşların üstünde yazan adlar (0-8 tuşlar, 9 encoder)
        public string[] Adlar = new string[10];

        private static readonly Color Karanlik = KlavyeGovdesi.YuvaSonuk;

        // Tuşun yanındaki LED (kesirli numara). Halkadaki yeri u (5 = 0, 6, 7, 8, 9, encoder, 4, 3, 2, 1 = 9):
        // LED = (u * 1.3 + 11.3) mod 13 (videodan ölçüldü: numara saat yönünün tersine artar)
        private static readonly float[] LedKonumu = new float[10];

        static KlavyeDemosu()
        {
            int[] halka = { 9, 8, 7, 6, 0, 1, 2, 3, 4, 5 };   // atama sırası (0-3 üst sıra, 4-7 alt sıra, 8 = 9, 9 = encoder)
            for (int i = 0; i < halka.Length; i++) LedKonumu[i] = (halka[i] * 1.3f + 11.3f) % 13f;
        }

        private Bitmap arka, on;          // gövde (yuvalar karanlık) / tuşlar + yazılar + yuva gölgesi
        private float olcek;
        private PointF kayma;

        public KlavyeDemosu()
        {
            BackColor = Tema.Panel;
        }

        public void AdlariYenile()
        {
            KatmanlariBirak();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            KatmanlariBirak();
            base.OnResize(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) KatmanlariBirak();
            base.Dispose(disposing);
        }

        private void KatmanlariBirak()
        {
            if (arka != null) { arka.Dispose(); arka = null; }
            if (on != null) { on.Dispose(); on = null; }
        }

        // Halkadaki f konumunun ışığı (LED numarası, kesirli): en yakın iki LED'in karışımı, 12'den sonra 0 gelir
        private Color Isik(float f)
        {
            f = ((f % LedSayisi) + LedSayisi) % LedSayisi;
            int a = Math.Min(LedSayisi - 1, (int)Math.Floor(f)), b = (a + 1) % LedSayisi;
            float t = f - a;
            Color ca = Olcek(led[a], parlaklik), cb = Olcek(led[b], parlaklik);
            return Color.FromArgb((int)(ca.R + (cb.R - ca.R) * t), (int)(ca.G + (cb.G - ca.G) * t), (int)(ca.B + (cb.B - ca.B) * t));
        }


        // Rengi en parlak hâline çeker (parlaklık ayrıca saydamlıkla verilir)
        private static Color Tam(Color c)
        {
            int m = Math.Max(1, (int)Math.Max(c.R, Math.Max(c.G, c.B)));
            return Color.FromArgb(Math.Min(255, c.R * 255 / m), Math.Min(255, c.G * 255 / m), Math.Min(255, c.B * 255 / m));
        }

        private void Donustur(Graphics g)
        {
            g.TranslateTransform(kayma.X, kayma.Y);
            g.ScaleTransform(olcek, olcek);
        }

        private bool KatmanlariHazirla()
        {
            if (arka != null && on != null) return true;
            if (Width < 20 || Height < 20) return false;

            Size b = KlavyeGovdesi.Boyut;
            olcek = Math.Min((Width - 8) / (float)b.Width, (Height - 8) / (float)b.Height);
            if (olcek <= 0.05f) return false;
            kayma = new PointF((Width - b.Width * olcek) / 2f, (Height - b.Height * olcek) / 2f);

            Rectangle sol, sag;
            KlavyeGovdesi.Yuvalar(out sol, out sag);

            arka = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(arka))
            {
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Donustur(g);
                KlavyeGovdesi.GovdeCiz(g, b.Width, b.Height);
                using (SolidBrush z = new SolidBrush(Karanlik))
                foreach (Rectangle r in new[] { sol, sag })
                    using (GraphicsPath p = KlavyeGovdesi.Yol(r, 12)) g.FillPath(z, p);
            }

            on = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(on))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Donustur(g);

                // yuvanın içten koyulaşan kenarı ve çerçevesi
                foreach (Rectangle r in new[] { sol, sag })
                {
                    using (GraphicsPath p = KlavyeGovdesi.Yol(r, 12))
                    {
                        using (PathGradientBrush pb = new PathGradientBrush(p))
                        {
                            pb.CenterColor = Color.FromArgb(0, 0, 0, 0);
                            pb.SurroundColors = new[] { Color.FromArgb(90, 0, 0, 0) };
                            pb.FocusScales = new PointF(0.85f, 0.75f);
                            g.FillPath(pb, p);
                        }
                        using (Pen kp = new Pen(KlavyeGovdesi.YuvaKenar, 2f)) g.DrawPath(kp, p);
                    }
                }

                // tuşlar ve yazıları
                using (Font numara = new Font("Segoe UI", 13f, FontStyle.Bold))
                using (Font sembol = new Font("Segoe UI Symbol", 15f))
                using (Font ad = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (StringFormat orta = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                using (SolidBrush beyaz = new SolidBrush(Tema.Yazi))
                using (SolidBrush soluk = new SolidBrush(Tema.SolukYazi))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Rectangle r = TusKutusu(i);
                        bool enc = i == 9;
                        using (GraphicsPath p = TusYolu(i))
                        // tuşlar ana ekrandaki tuş kutucuklarıyla aynı renkte (açık temada beyaz)
                        using (LinearGradientBrush f = new LinearGradientBrush(r, Tema.Kart, Tema.KartAcik, 90f))
                            g.FillPath(f, p);

                        string n = enc ? "⟳" : (i + 1).ToString();
                        string yazi = i < Adlar.Length ? (Adlar[i] ?? "") : "";
                        RectangleF ust = new RectangleF(r.X, r.Y + r.Height * 0.18f, r.Width, r.Height * 0.36f);
                        RectangleF alt = new RectangleF(r.X + 8, r.Y + r.Height * 0.54f, r.Width - 16, r.Height * 0.26f);
                        g.DrawString(n, enc ? sembol : numara, beyaz, ust, orta);
                        g.DrawString(yazi, ad, soluk, alt, orta);
                    }
                }
            }
            return true;
        }

        private static Rectangle TusKutusu(int i)
        {
            Rectangle r = KlavyeGovdesi.TusAlani(i);
            r.Inflate(-3, -3);
            return r;
        }

        private static GraphicsPath TusYolu(int i)
        {
            Rectangle r = TusKutusu(i);
            if (i != 9) return KlavyeGovdesi.Yol(r, 11);
            GraphicsPath p = new GraphicsPath();
            p.AddEllipse(r);
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Akım sınırı aşılıyorsa kart parlaklığı kısar: demo da aynısını gösterir
            float asil = parlaklik;
            parlaklik = EtkinParlaklik;
            try { Ciz(e); }
            finally { parlaklik = asil; }
        }

        private void Ciz(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (!KatmanlariHazirla())
            {
                g.Clear(BackColor);
                return;
            }

            g.DrawImageUnscaled(arka, 0, 0);

            // --- Işık: hafif ortam ışığı + her tuşun çevresinde yanındaki LED'in rengi (yuvaların içinde) ---
            Rectangle sol, sag;
            KlavyeGovdesi.Yuvalar(out sol, out sag);
            int tr = 0, tg = 0, tb = 0;
            for (int i = 0; i < LedSayisi; i++)
            {
                Color c = Olcek(led[i], parlaklik);
                tr += c.R; tg += c.G; tb += c.B;
            }
            // ortam: LED'lerin ortalama rengi, en parlak kanalı 255'e çekilip parlaklığı saydamlıkla verilir
            int enCok = Math.Max(1, Math.Max(tr, Math.Max(tg, tb)));
            int ort = enCok / LedSayisi;
            Color ortam = ort < 6 ? Color.Transparent
                : Color.FromArgb(Math.Min(170, ort * 2 / 3),
                                 Math.Min(255, tr * 255 / enCok), Math.Min(255, tg * 255 / enCok), Math.Min(255, tb * 255 / enCok));

            GraphicsState durum = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Donustur(g);
            Color[] tusIsigi = new Color[10];
            using (GraphicsPath solYol = KlavyeGovdesi.Yol(sol, 12))
            using (GraphicsPath sagYol = KlavyeGovdesi.Yol(sag, 12))
            using (Region yuvalar = new Region(solYol))
            {
                yuvalar.Union(sagYol);
                g.SetClip(yuvalar, CombineMode.Replace);
                using (SolidBrush z = new SolidBrush(ortam))
                {
                    g.FillPath(z, solYol);
                    g.FillPath(z, sagYol);
                }

                for (int i = 0; i < 10; i++)
                {
                    Color c = tusIsigi[i] = Isik(LedKonumu[i]);
                    int guc = Math.Max(c.R, Math.Max(c.G, c.B));
                    if (guc < 10) continue;
                    // sönük ışık koyu renk değil, saydam renk: açık zeminde kararmasın
                    Color tam = Tam(c);
                    Rectangle r = KlavyeGovdesi.TusAlani(i);
                    foreach (int[] kat in new[] { new[] { 13, 70 }, new[] { 8, 140 }, new[] { 4, 220 } })
                    {
                        Rectangle h = Rectangle.Inflate(r, kat[0], kat[0]);
                        using (SolidBrush fb = new SolidBrush(Color.FromArgb(kat[1] * guc / 255, tam)))
                        {
                            if (i == 9) g.FillEllipse(fb, h);
                            else using (GraphicsPath hp = KlavyeGovdesi.Yol(h, 14)) g.FillPath(fb, hp);
                        }
                    }
                }
            }
            g.Restore(durum);

            g.DrawImageUnscaled(on, 0, 0);

            // tuşun kenarına vuran ışık
            durum = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Donustur(g);
            for (int i = 0; i < 10; i++)
            {
                Color c = tusIsigi[i];
                int guc = Math.Max(c.R, Math.Max(c.G, c.B));
                using (GraphicsPath p = TusYolu(i))
                using (Pen k = new Pen(guc < 10 ? Tema.Kenar : Color.FromArgb(Math.Min(230, 40 + guc * 3 / 4), Tam(c)), 2f))
                    g.DrawPath(k, p);
            }
            g.Restore(durum);
        }
    }
}
