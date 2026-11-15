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
    // =====================================================================
    //  Yuvarlak köşeli, şeffaf zeminli panel (kart, buton, kutucuk)
    // =====================================================================
    public class YuvarlakPanel : Panel
    {
        public int Yaricap { get; set; } = 14;
        public Color DolguRengi { get; set; } = Tema.Kart;
        public Color KenarRengi { get; set; } = Color.Transparent;
        public int KenarKalinligi { get; set; } = 1;
        public Color HoverRengi { get; set; } = Color.Empty;

        private bool _hover;
        public bool Hover
        {
            get { return _hover; }
            set { if (_hover != value) { _hover = value; Invalidate(); } }
        }

        public YuvarlakPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        public static GraphicsPath Yol(RectangleF r, float yaricap)
        {
            float d = yaricap * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float k = KenarKalinligi / 2f;
            var r = new RectangleF(k, k, Width - KenarKalinligi - 1, Height - KenarKalinligi - 1);

            Color dolgu = (Hover && !HoverRengi.IsEmpty) ? HoverRengi : DolguRengi;

            using (var yol = Yol(r, Yaricap))
            using (var firca = new SolidBrush(dolgu))
            {
                e.Graphics.FillPath(firca, yol);
                if (KenarRengi != Color.Transparent && KenarKalinligi > 0)
                    using (var kalem = new Pen(KenarRengi, KenarKalinligi))
                        e.Graphics.DrawPath(kalem, yol);
            }
        }
    }
}
