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
    // Form1'in parçası: İlk açılış rehberi (tanıtım)
    public partial class Form1
    {
        // =================================================================
        //  İLK AÇILIŞ REHBERİ
        // =================================================================
        private class RehberAdimi
        {
            public Control Hedef;
            public string Baslik, Metin;
        }

        private static readonly Color VurguRengi = Color.FromArgb(250, 190, 20);   // açık ve koyu temada görünür

        private List<RehberAdimi> rehberAdimlari;
        private int rehberSira = -1;
        private YuvarlakPanel rehberBalon;
        private Label lblRehberSira, lblRehberBaslik, lblRehberMetin, lblRehberIleri;
        private Control vurgulanan;
        private Color vurguEskiRenk;
        private int vurguEskiKalinlik;

        private bool RehberAcik
        {
            get { return rehberSira >= 0; }
        }

        private void RehberBaslat()
        {
            if (!Visible) return;
            if (sayfa != Sayfa.Tuslar)
            {
                SayfaAc(Sayfa.Tuslar);
                if (sayfa != Sayfa.Tuslar) return;
            }

            rehberAdimlari = new List<RehberAdimi>
            {
                new RehberAdimi { Hedef = btnBaglan, Baslik = "Klavyeye bağlan",
                    Metin = "Klavyeyi USB'ye tak; uygulama onu kendiliğinden bulur. Bağlanınca kartın sağlığına ve " +
                            "kart yazılımının yeni sürümü olup olmadığına da bakar." },
                new RehberAdimi { Hedef = atamaKarti, Baslik = "Bir tuş seç",
                    Metin = "Tuşa tıkla (ya da klavyeden 1-9, encoder için 0). Sürükleyip iki tuşun yerini değiştir; " +
                            "sağ tıklayıp ad, renk, simge ver ya da kopyala-yapıştır." },
                new RehberAdimi { Hedef = ozetSatirlari[1], Baslik = "Bir tuşa birçok iş",
                    Metin = "Klavyenin altındaki listede seçili tuşun bütün basışları: kısa, uzun, çift ve tuşu tutup " +
                            "encoder'ı çevirme. Satıra tıkla, o basışı düzenle. Encoder'da sağa-sola çevirme de var." },
                new RehberAdimi { Hedef = komutKutusu, Baslik = "Ne yapacağını seç",
                    Metin = "Kutuya tıkla: üstte ara, altında Kısayol / Metin / Uygulama / Makro / Boş, " +
                            "solda kategoriler. Makro adımına çift tıklayıp düzenle." },
                new RehberAdimi { Hedef = btnKaydet, Baslik = "Kaydet",
                    Metin = "AYARI KAYDET klavyeye anında yazar (Ctrl+S). Kaydedilmemiş değişiklik olunca üstünde " +
                            "kırmızı yazı çıkar. Yanlış olursa Geri al (Ctrl+Z)." },
                new RehberAdimi { Hedef = btnLed, Baslik = "Işık",
                    Metin = "Efekt, renk, parlaklık, hız ve akış renkleri. Klavyenin canlı demosunda ve klavyenin " +
                            "kendisinde kaydetmeden görürsün." },
                new RehberAdimi { Hedef = profilPaneli, Baslik = "Profiller",
                    Metin = "Farklı işler için ayrı tuş düzenleri: + ile şu anki ayarları kaydet, listeden tek tıkla geç. " +
                            "Sağ tık: güncelle / sil. Tepsi menüsünden de geçilir." },
                new RehberAdimi { Hedef = lblAyarlarDugme, Baslik = "Ayarlar",
                    Metin = "Klavye ayarları (Türkçe Q / F, süreler, uyku engelleyici), tanılama, tam yedek, geçmiş, günlük. " +
                            "Aradığını üstteki arama kutusuyla bul (Ctrl+F)." }
            };

            if (rehberBalon == null) RehberBalonuOlustur();

            rehberSira = 0;
            RehberAdimGoster();
        }

        private void RehberBalonuOlustur()
        {
            rehberBalon = new YuvarlakPanel
            {
                Size = new Size(340, 214),
                Yaricap = 14,
                DolguRengi = Tema.Kart,
                KenarRengi = VurguRengi,
                KenarKalinligi = 2,
                Visible = false
            };

            lblRehberSira = Etiket(rehberBalon, "", new Font("Segoe UI", 8.5f, FontStyle.Bold), SolukYazi,
                                   18, 14, 200, 18);
            lblRehberBaslik = Etiket(rehberBalon, "", new Font("Segoe UI", 12f, FontStyle.Bold), Lacivert,
                                     18, 34, 306, 26);
            lblRehberMetin = Etiket(rehberBalon, "", new Font("Segoe UI", 9.5f), Lacivert,
                                    18, 62, 306, 104);

            Label atla = Etiket(rehberBalon, "Tanıtımı geç", new Font("Segoe UI", 9f, FontStyle.Underline), SolukYazi,
                                18, 178, 120, 22, ContentAlignment.MiddleLeft);
            atla.Cursor = Cursors.Hand;
            atla.Click += (s, e) => RehberBitir();

            YuvarlakPanel ileri = new YuvarlakPanel
            {
                Location = new Point(232, 170),
                Size = new Size(92, 34),
                Yaricap = 10,
                DolguRengi = Mavi,
                HoverRengi = MaviKoyu
            };
            lblRehberIleri = Etiket(ileri, "İleri", new Font("Segoe UI", 10f, FontStyle.Bold), Color.White,
                                    0, 0, 92, 34, ContentAlignment.MiddleCenter);
            HoverBagla(ileri);
            TiklaBagla(ileri, (s, e) => RehberIleri());
            rehberBalon.Controls.Add(ileri);

            Controls.Add(rehberBalon);
        }

        private void RehberAdimGoster()
        {
            VurguyuKaldir();

            RehberAdimi a = rehberAdimlari[rehberSira];
            bool son = rehberSira == rehberAdimlari.Count - 1;

            lblRehberSira.Text = "TANITIM  ·  " + (rehberSira + 1) + " / " + rehberAdimlari.Count;
            lblRehberBaslik.Text = a.Baslik;
            lblRehberMetin.Text = a.Metin;
            lblRehberIleri.Text = son ? "Bitir" : "İleri";

            Vurgula(a.Hedef);

            // Balonu hedefin altına (yer yoksa üstüne) yerleştir
            Rectangle h = RectangleToClient(a.Hedef.RectangleToScreen(a.Hedef.ClientRectangle));
            int x = h.Left + h.Width / 2 - rehberBalon.Width / 2;
            x = Math.Max(12, Math.Min(ClientSize.Width - rehberBalon.Width - 12, x));

            int y = h.Bottom + 10;
            if (y + rehberBalon.Height > ClientSize.Height - 12) y = h.Top - rehberBalon.Height - 10;
            if (y < 48)
            {
                // Uzun hedef (ör. soldaki profil paneli): altına da üstüne de sığmıyor, yanına koy
                y = h.Top + 24;
                x = h.Right + 12 + rehberBalon.Width <= ClientSize.Width - 12 ? h.Right + 12 : h.Left - rehberBalon.Width - 12;
                x = Math.Max(12, x);
            }

            rehberBalon.Location = new Point(x, y);
            rehberBalon.Visible = true;
            rehberBalon.BringToFront();
        }

        private void RehberIleri()
        {
            if (!RehberAcik) return;

            if (rehberSira < rehberAdimlari.Count - 1)
            {
                rehberSira++;
                RehberAdimGoster();
            }
            else
            {
                RehberBitir();
            }
        }

        private void RehberBitir()
        {
            VurguyuKaldir();
            if (rehberBalon != null) rehberBalon.Visible = false;
            rehberSira = -1;

            try { UygulamaAyarlari.RehberGoruldu = true; }
            catch (Exception) { }
        }

        // Hedefin çerçevesini sarı yap (yazı düğmesinde yazıyı)
        private void Vurgula(Control c)
        {
            vurgulanan = c;
            YuvarlakPanel p = c as YuvarlakPanel;

            if (p != null)
            {
                vurguEskiRenk = p.KenarRengi;
                vurguEskiKalinlik = p.KenarKalinligi;
                p.KenarRengi = VurguRengi;
                p.KenarKalinligi = 3;
                p.Invalidate();
            }
            else
            {
                vurguEskiRenk = c.ForeColor;
                c.ForeColor = VurguRengi;
            }
        }

        private void VurguyuKaldir()
        {
            if (vurgulanan == null) return;

            YuvarlakPanel p = vurgulanan as YuvarlakPanel;
            if (p != null)
            {
                p.KenarRengi = vurguEskiRenk;
                p.KenarKalinligi = vurguEskiKalinlik;
                p.Invalidate();
            }
            else
            {
                vurgulanan.ForeColor = vurguEskiRenk;
            }

            vurgulanan = null;

            // Seçili tuş / basış sekmesi stilleri kendi renklerine dönsün
            KutucukStilleri();
            BasisGorunum();
        }
    }
}
