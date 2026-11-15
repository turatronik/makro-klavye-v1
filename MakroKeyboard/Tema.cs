using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MakroKeyboard
{
    // Uygulamanın renkleri. Açık / koyu tema seçimi ayarlar.ini'den okunur ve
    // uygulama açılırken bir kez uygulanır (tema değişikliği yeniden başlatınca geçerli olur).
    public static class Tema
    {
        public static readonly bool Koyu = UygulamaAyarlari.KoyuTema;

        private static Color Sec(Color acik, Color koyu)
        {
            return Koyu ? koyu : acik;
        }

        private static Color R(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }

        // Koyu tema 16 tuşlu uygulamayla (Makro Klavye 16) aynı renklerde

        // Pencere zemini (gradyan)
        public static readonly Color Zemin1 = Sec(R(216, 237, 255), R(14, 17, 23));
        public static readonly Color Zemin2 = Sec(R(186, 220, 250), R(11, 13, 18));

        // Başlık çubuğu
        public static readonly Color Baslik = Sec(R(18, 45, 78), R(9, 11, 15));

        // Yazılar
        public static readonly Color Yazi = Sec(R(18, 45, 78), R(230, 233, 242));
        public static readonly Color SolukYazi = Sec(R(95, 115, 140), R(139, 147, 167));
        public static readonly Color Pasif = Sec(R(160, 175, 195), R(91, 98, 117));

        // Kartlar, kutular, paneller
        public static readonly Color Kart = Sec(Color.White, R(20, 24, 33));
        public static readonly Color KartAcik = Sec(R(244, 250, 255), R(26, 31, 43));
        public static readonly Color Panel = Sec(R(244, 250, 255), R(17, 20, 27));
        public static readonly Color PanelKenar = Sec(R(225, 238, 250), R(29, 34, 48));
        public static readonly Color Kenar = Sec(R(200, 222, 242), R(35, 40, 56));
        public static readonly Color Kenar2 = Sec(R(190, 215, 240), R(47, 53, 70));

        // Etkileşim
        public static readonly Color Hover = Sec(R(235, 244, 255), R(31, 37, 51));
        public static readonly Color Secili = Sec(R(225, 240, 255), R(19, 48, 59));

        // Durum renkleri
        public static readonly Color OkDolgu = Sec(R(222, 246, 235), R(18, 51, 38));
        public static readonly Color OkKenar = Sec(R(160, 225, 190), R(42, 107, 80));
        public static readonly Color OkYazi = Sec(R(20, 120, 70), R(87, 217, 163));
        public static readonly Color HataDolgu = Sec(R(254, 230, 230), R(58, 23, 32));
        public static readonly Color HataKenar = Sec(R(240, 180, 180), R(107, 42, 58));
        public static readonly Color HataYazi = Sec(R(190, 45, 45), R(255, 107, 138));

        // Pencere zemini (gradyan). Ana pencerenin içine konan sayfa / gömülü pencere de
        // gradyanın kendisine düşen parçasını boyar: zemin kesintisiz görünür.
        public static void ZeminBoya(Graphics g, Control c)
        {
            if (c.Width < 1 || c.Height < 1) return;
            Control ust = c.TopLevelControl ?? c;
            Rectangle r = c.ClientRectangle;
            if (ust != c && ust.IsHandleCreated && c.IsHandleCreated)
                r = new Rectangle(c.PointToClient(ust.PointToScreen(Point.Empty)), ust.ClientSize);
            if (r.Width < 1 || r.Height < 1) return;
            using (LinearGradientBrush b = new LinearGradientBrush(r, Zemin1, Zemin2, 50f))
                g.FillRectangle(b, c.ClientRectangle);
        }
    }
}