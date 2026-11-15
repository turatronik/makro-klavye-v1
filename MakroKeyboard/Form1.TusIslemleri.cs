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
    // Form1'in parçası: Tuş kartlarıyla işlemler: canlı izleme, sürükle-bırak, çakışma, kaydedilmemiş değişiklik, geri al, sağ tık menüsü
    public partial class Form1
    {
        // =================================================================
        //  CANLI TUŞ İZLEME
        // =================================================================
        // Arka plan iş parçacığından gelen işi arayüz iş parçacığında çalıştırır
        private void UiyeAt(Action eylem)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(eylem);
            }
            catch (Exception) { }
        }

        private void TusVurgula(int n, bool tusBasildi, string tur)
        {
            if (n < 0 || n >= kutucuklar.Count) return;

            vurguIndex = n;
            KutucukStilleri();

            vurguZamanlayici.Stop();
            vurguZamanlayici.Start();

            if (tusBasildi && !islemde)
            {
                string ad = (n < atamalar.Count - 1) ? "BUTON " + (n + 1) : "ENCODER";
                string komut = atamalar[n].Kisayol;
                string nasil = " basıldı";

                if (tur == "LONG") { komut = Al("BUTTON" + n + "_LONG", ""); nasil = " uzun basıldı"; }
                else if (tur == "DOUBLE") { komut = Al("BUTTON" + n + "_DOUBLE", ""); nasil = " çift basıldı"; }

                Bildir(ad + nasil + ": " + GosterimKisayol(komut));
            }
        }

        // =================================================================
        //  SÜRÜKLE-BIRAK: iki tuşun atamalarını yer değiştir
        // =================================================================
        private void SurukleBagla(Control c, int indeks)
        {
            c.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                tiklamaYut = false;
                surukleBasildi = true;
                surukleniyor = false;
                surukleKaynak = indeks;
                surukleBaslangic = Cursor.Position;
            };
            c.MouseMove += (s, e) => SurukleHareket();
            c.MouseUp += (s, e) => SurukleBitir();

            foreach (Control alt in c.Controls) SurukleBagla(alt, indeks);
        }

        private void SurukleHareket()
        {
            if (!surukleBasildi) return;

            Point p = Cursor.Position;
            if (!surukleniyor)
            {
                // Küçük titremeler tıklama sayılsın
                if (Math.Abs(p.X - surukleBaslangic.X) < 6 && Math.Abs(p.Y - surukleBaslangic.Y) < 6) return;
                if (islemde) return;
                surukleniyor = true;
            }

            Cursor.Current = Cursors.SizeAll;

            int hedef = KutucukBul(p);
            if (hedef == surukleKaynak) hedef = -1;

            if (hedef != surukleHedef)
            {
                surukleHedef = hedef;
                KutucukStilleri();
                Bildir(hedef < 0
                    ? TusEtiketi(surukleKaynak) + " taşınıyor… bir tuşun üstüne bırak."
                    : "Bırakınca " + TusEtiketi(surukleKaynak) + " ile " + TusEtiketi(hedef) + " yer değiştirir.");
            }
        }

        private void SurukleBitir()
        {
            if (!surukleBasildi) return;
            surukleBasildi = false;

            if (!surukleniyor) return;   // normal tıklama

            int kaynak = surukleKaynak, hedef = surukleHedef;
            surukleniyor = false;
            surukleKaynak = surukleHedef = -1;
            tiklamaYut = true;           // bırakıldığı yerde tıklama olarak sayılmasın
            KutucukStilleri();

            if (hedef >= 0 && hedef != kaynak) TuslariDegistir(kaynak, hedef);
            else Bildir(VarsayilanBilgi);
        }

        private int KutucukBul(Point ekranNoktasi)
        {
            for (int k = 0; k < kutucuklar.Count; k++)
            {
                Control p = kutucuklar[k].Panel;
                if (p.RectangleToScreen(p.ClientRectangle).Contains(ekranNoktasi)) return k;
            }
            return -1;
        }

        private string TusEtiketi(int i)
        {
            return i < atamalar.Count - 1 ? "BUTON " + (i + 1) : "ENCODER";
        }

        // İki tuşun kısa / uzun / çift basış atamalarını karta yazarak yer değiştirir.
        // Uygulama kısayolları her tuşun kendi Ctrl+Alt+harfine taşınır.
        private async void TuslariDegistir(int i, int j)
        {
            if (!klavyeBagli)
            {
                Bildir("Yer değiştirmek için önce klavyeye bağlan.");
                return;
            }
            if (!await IslemSirasiniBekle())
            {
                Bildir("Başka bir işlem sürüyor, biraz sonra tekrar dene.");
                return;
            }

            GeriAlNoktasi(TusEtiketi(i) + " ↔ " + TusEtiketi(j));
            Dictionary<string, string> eski = new Dictionary<string, string>(kartAyarlari);

            string hi = AyrilmisKisayol(i), hj = AyrilmisKisayol(j);
            string lnkI, lnkJ;
            uygulamalar.TryGetValue(hi, out lnkI);
            uygulamalar.TryGetValue(hj, out lnkJ);
            bool uygI = lnkI != null && HotkeyNorm(atamalar[i].Kisayol) == hi;
            bool uygJ = lnkJ != null && HotkeyNorm(atamalar[j].Kisayol) == hj;

            foreach (string ek in new[] { "", "_LONG", "_DOUBLE", "_CW", "_CCW" })
            {
                string ai = "BUTTON" + i + ek, aj = "BUTTON" + j + ek;
                string vi, vj;
                bool varI = eski.TryGetValue(ai, out vi);
                bool varJ = eski.TryGetValue(aj, out vj);

                if (varJ) kartAyarlari[ai] = vj; else kartAyarlari.Remove(ai);
                if (varI) kartAyarlari[aj] = vi; else kartAyarlari.Remove(aj);
            }

            // Uygulama taşındıysa yeni yerinin kısayolunu kullan
            if (uygJ) kartAyarlari["BUTTON" + i] = hi;
            if (uygI) kartAyarlari["BUTTON" + j] = hj;

            string eskiI = atamalar[i].Kisayol, eskiJ = atamalar[j].Kisayol;
            atamalar[i].Kisayol = Al("BUTTON" + i, "");
            atamalar[j].Kisayol = Al("BUTTON" + j, "");
            KutuculariYenile();

            Bildir(TusEtiketi(i) + " ile " + TusEtiketi(j) + " yer değiştiriyor...");
            string hata = await KartaYaz();

            if (hata != null)
            {
                kartAyarlari = eski;
                atamalar[i].Kisayol = eskiI;
                atamalar[j].Kisayol = eskiJ;
                KutuculariYenile();
                GeriAlIptal();
                HataGoster(hata);
                return;
            }

            string mesaj = TusEtiketi(i) + " ile " + TusEtiketi(j) + " yer değiştirdi.";
            try
            {
                // Kısayol dosyalarının tuş kombinasyonlarını da değiştir
                WshShell shell = new WshShell();
                if (uygI)
                {
                    IWshShortcut k = (IWshShortcut)shell.CreateShortcut(lnkI);
                    k.Hotkey = hj;
                    k.Save();
                }
                if (uygJ)
                {
                    IWshShortcut k = (IWshShortcut)shell.CreateShortcut(lnkJ);
                    k.Hotkey = hi;
                    k.Save();
                }
            }
            catch (Exception ex)
            {
                mesaj = "Yer değiştirdi ama uygulama kısayolu güncellenemedi: " + ex.Message;
            }

            // Tuşların adı ve rengi de yer değiştirir
            TusAdRenk gi = OzelGorunum(i), gj = OzelGorunum(j);
            OzelGorunumAyarla(i, gj != null ? gj.Kopya() : null);
            OzelGorunumAyarla(j, gi != null ? gi.Kopya() : null);

            UygulamalariTara();
            KutuculariYenile();
            SecimYap(secili);
            await TasinanKopyayiGuncelle();
            OtomatikYedek(TusEtiketi(i) + " ↔ " + TusEtiketi(j));
            Bildir(mesaj);
        }

        // =================================================================
        //  ÇAKIŞMA: aynı komut başka bir tuşta / basışta var mı?
        // =================================================================
        private static string KomutAnahtari(string komut)
        {
            if (string.IsNullOrEmpty(komut)) return "";
            if (komut.StartsWith("TEXT:") || komut.StartsWith("MACRO:")) return komut;
            return HotkeyNorm(komut);
        }

        // Çakışan yerin adı ("BUTON 3 (uzun basış)"), yoksa null
        private string CakismaBul(string komut)
        {
            string aranan = KomutAnahtari(komut);
            if (aranan.Length == 0 || aranan == "NONE") return null;

            string aktif = Anahtar();

            foreach (var kv in kartAyarlari)
            {
                if (kv.Key == aktif || KomutAnahtari(kv.Value) != aranan) continue;

                string ad = YerAdi(kv.Key);
                if (ad != null) return ad;
            }
            return null;
        }

        private string YerAdi(string anahtar)
        {
            if (anahtar == "ENCODER_CW") return "ENCODER (sağa çevirme)";
            if (anahtar == "ENCODER_CCW") return "ENCODER (sola çevirme)";

            Match m = Regex.Match(anahtar, @"^BUTTON(\d)(_LONG|_DOUBLE|_CW|_CCW)?$");
            if (!m.Success) return null;

            int n = int.Parse(m.Groups[1].Value);
            bool enc = n == atamalar.Count - 1;
            string tur = m.Groups[2].Value == "_LONG" ? (enc ? "uzun basma" : "uzun basış")
                       : m.Groups[2].Value == "_DOUBLE" ? (enc ? "çift basma" : "çift basış")
                       : m.Groups[2].Value == "_CW" ? (enc ? "basılı + sağa" : "tut + sağa çevir")
                       : m.Groups[2].Value == "_CCW" ? (enc ? "basılı + sola" : "tut + sola çevir")
                       : (enc ? "basma" : "kısa basış");
            return TusEtiketi(n) + " (" + tur + ")";
        }

        // =================================================================
        //  KAYDEDİLMEMİŞ DEĞİŞİKLİK
        // =================================================================
        // Düzenleyicide (kutu / liste) duran ama karta yazılmamış komut
        private string EditordekiKomut()
        {
            if (metinModu)
                return txtKisayol.Text.Length > 0 ? "TEXT:" + MetinKodla(txtKisayol.Text) : "";
            return komutHam ?? "";
        }

        private bool DegisiklikVar()
        {
            if (!klavyeBagli || txtKisayol == null) return false;
            if (uygulamaModu && bekleyenExe != null) return true;

            string duzenlenen = EditordekiKomut();
            if (duzenlenen.Length == 0) return false;   // boş kutu: değişiklik sayılmaz

            return KomutAnahtari(duzenlenen) != KomutAnahtari(AktifKomut());
        }

        // Devam edilebilirse true (değişiklik yok, kaydedildi ya da vazgeçildi)
        private async Task<bool> DegisiklikleriSor()
        {
            if (!DegisiklikVar()) return true;

            DialogResult c = MessageBox.Show(this,
                TusEtiketi(secili) + " (" + BasisAdi() + ") için yaptığın değişiklik kaydedilmedi.\n\nKaydedilsin mi?",
                "Kaydedilmemiş değişiklik", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (c == DialogResult.Cancel) return false;
            if (c == DialogResult.No)
            {
                UygulamaAyarlari.GunlukEkle(UygulamaAyarlari.GunlukUyari,
                                            TusEtiketi(secili) + " · " + BasisAdi() + ": kaydedilmemiş değişiklik atıldı");
                return true;
            }
            return await AyariKaydetAsync();
        }

        private async void KullaniciTusSec(int i)
        {
            if (i == secili) return;
            if (!await DegisiklikleriSor()) return;
            SecimYap(i);
        }

        private async void KullaniciBasisSec(int b)
        {
            if (b == basisTuru) return;
            if (!await DegisiklikleriSor()) return;
            BasisSec(b);
        }

        // =================================================================
        //  GERİ AL
        // =================================================================
        private void GeriAlNoktasi(string aciklama)
        {
            sonIslem = aciklama;   // yazılamazsa günlükte ne denendiği görünsün
            if (geriAliniyor) return;

            string metin = ProfilMetni();
            if (metin == null) return;

            geriAlYigini.Add(new KeyValuePair<string, string>(aciklama, metin));
            if (geriAlYigini.Count > GeriAlSiniri) geriAlYigini.RemoveAt(0);
            GeriAlGorunum();
        }

        // =================================================================
        //  SAĞ TIK MENÜSÜ: kopyala / yapıştır / boşalt / varsayılan
        // =================================================================
        private void TusMenusuBagla(Control c, int indeks)
        {
            c.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) tiklamaYut = true;   // sağ tık seçim yapmasın
            };
            c.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) TusMenusuAc(indeks);
            };
            foreach (Control alt in c.Controls) TusMenusuBagla(alt, indeks);
        }

        private void TusMenusuAc(int i)
        {
            menuTusu = i;
            ContextMenuStrip m = TemaMenusu(false);

            m.Items.Add(MenuOgesi("", OzelGorunum(i) != null ? "Ad, renk, simge değiştir…" : "Ad, renk, simge ver…",
                                  () => TusAdiDuzenle(menuTusu)));

            if (i == atamalar.Count - 1)
            {
                ToolStripMenuItem cevirme = new ToolStripMenuItem("Çevirme modu: " + EncoderModAdi())
                {
                    ForeColor = Lacivert,
                    Padding = new Padding(4, 3, 4, 3)
                };
                cevirme.DropDown.Renderer = m.Renderer;
                foreach (ToolStripItem o in EncoderModOgeleri()) cevirme.DropDownItems.Add(o);
                m.Items.Add(cevirme);
            }

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuOgesi("", "Kopyala", () => TusKopyala(menuTusu)));

            ToolStripMenuItem yapistir = MenuOgesi("",
                pano != null ? "Yapıştır (" + pano.Kaynak + ")" : "Yapıştır",
                () => TusYapistir(menuTusu));
            yapistir.Enabled = pano != null;
            m.Items.Add(yapistir);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuOgesi("", "Boşalt (bir şey yapmasın)", () => TusBosalt(menuTusu)));
            m.Items.Add(MenuOgesi("", "Varsayılana döndür", () => TusVarsayilan(menuTusu)));

            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(Cursor.Position);
        }

        private void TusKopyala(int i)
        {
            string hk = AyrilmisKisayol(i), lnk;
            bool uyg = uygulamalar.TryGetValue(hk, out lnk) && HotkeyNorm(atamalar[i].Kisayol) == hk;
            string hedef = uyg ? LnkHedefi(lnk) : null;

            if (uyg && hedef == null)
            {
                Bildir("Bu tuşun uygulama kısayolu okunamadı, kopyalanamadı.");
                return;
            }

            pano = new TusPanosu
            {
                Kisa = atamalar[i].Kisayol,
                Uzun = Al("BUTTON" + i + "_LONG", ""),
                Cift = Al("BUTTON" + i + "_DOUBLE", ""),
                UygHedef = hedef,
                UygAd = uyg ? Path.GetFileNameWithoutExtension(lnk) : null,
                Kaynak = TusEtiketi(i),
                Gorunum = OzelGorunum(i) != null ? OzelGorunum(i).Kopya() : null
            };
            Bildir(TusEtiketi(i) + " kopyalandı. Başka bir tuşa sağ tıklayıp Yapıştır'ı seç.");
        }

        private async void TusYapistir(int j)
        {
            if (pano == null) return;

            await TusuYaz(j, pano.Kisa, pano.Uzun, pano.Cift,
                          pano.UygHedef, pano.UygAd,
                          pano.Kaynak + " → " + TusEtiketi(j) + " yapıştırma", false,
                          pano.Gorunum != null ? pano.Gorunum.Kopya() : null);
        }

        private async void TusBosalt(int j)
        {
            await TusuYaz(j, "NONE", "", "", null, null, TusEtiketi(j) + " boşaltma", false, null, true);
        }

        private async void TusVarsayilan(int j)
        {
            await TusuYaz(j, VarsayilanKomutlar[j], "", "", null, null,
                          TusEtiketi(j) + " varsayılana dönüş", j == atamalar.Count - 1, null, true);
        }

        // Bir tuşun kısa / uzun / çift atamalarını yazar; uygulama varsa yeni tuşun kısayoluyla kurar.
        // gorunum: tuşun yeni adı ve rengi (null = kaldır)
        private async Task TusuYaz(int j, string kisa, string uzun, string cift,
                                   string uygHedef, string uygAd, string aciklama, bool encoderSifirla,
                                   TusAdRenk gorunum, bool cevirmeyiSil = false)
        {
            if (!klavyeBagli)
            {
                Bildir("Önce klavyeye bağlan.");
                return;
            }
            if (!await IslemSirasiniBekle())
            {
                Bildir("Başka bir işlem sürüyor, biraz sonra tekrar dene.");
                return;
            }

            string hj = AyrilmisKisayol(j);
            string eskiLnk;
            uygulamalar.TryGetValue(hj, out eskiLnk);

            bool yeniUyg = uygHedef != null;
            if (yeniUyg) kisa = hj;   // uygulama bu tuşun kendi Ctrl+Alt+harfine taşınır

            GeriAlNoktasi(aciklama);
            Dictionary<string, string> eski = new Dictionary<string, string>(kartAyarlari);
            string eskiKisa = atamalar[j].Kisayol;

            kartAyarlari["BUTTON" + j] = string.IsNullOrEmpty(kisa) ? "NONE" : kisa;

            if (string.IsNullOrEmpty(uzun) || uzun == "NONE") kartAyarlari.Remove("BUTTON" + j + "_LONG");
            else kartAyarlari["BUTTON" + j + "_LONG"] = uzun;

            if (string.IsNullOrEmpty(cift) || cift == "NONE") kartAyarlari.Remove("BUTTON" + j + "_DOUBLE");
            else kartAyarlari["BUTTON" + j + "_DOUBLE"] = cift;

            if (cevirmeyiSil)
            {
                kartAyarlari.Remove("BUTTON" + j + "_CW");
                kartAyarlari.Remove("BUTTON" + j + "_CCW");
            }

            if (encoderSifirla)
            {
                kartAyarlari["ENCODER_CW"] = "VOLUME_UP";
                kartAyarlari["ENCODER_CCW"] = "VOLUME_DOWN";
            }

            atamalar[j].Kisayol = kartAyarlari["BUTTON" + j];
            KutuculariYenile();

            Bildir(aciklama + "...");
            string hata = await KartaYaz();

            if (hata != null)
            {
                kartAyarlari = eski;
                atamalar[j].Kisayol = eskiKisa;
                KutuculariYenile();
                GeriAlIptal();
                HataGoster(hata);
                return;
            }

            string mesaj = aciklama + " tamamlandı.";
            try
            {
                if (eskiLnk != null) System.IO.File.Delete(eskiLnk);
                if (yeniUyg) KisayolDosyasiOlustur(uygHedef, uygAd, hj);
            }
            catch (Exception ex)
            {
                mesaj = aciklama + " yapıldı ama uygulama kısayolu güncellenemedi: " + ex.Message;
            }

            OzelGorunumAyarla(j, gorunum);
            UygulamalariTara();
            KutuculariYenile();
            SecimYap(secili);
            await TasinanKopyayiGuncelle();
            OtomatikYedek(aciklama);
            Bildir(mesaj);
        }

        // =================================================================
        //  TEMALI MENÜLER
        // =================================================================
        private ContextMenuStrip TemaMenusu(bool isaretli)
        {
            return new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new TemaRenkTablosu()) { RoundedEdges = false },
                Font = new Font("Segoe UI", 9.5f),
                ShowImageMargin = false,
                ShowCheckMargin = isaretli,
                ForeColor = Lacivert
            };
        }

        private ToolStripMenuItem MenuOgesi(string ikon, string yazi, Action tik)
        {
            // ikon şimdilik kullanılmıyor (menü yazı tipi simge karakterlerini çizmez)
            ToolStripMenuItem o = new ToolStripMenuItem(yazi)
            {
                ForeColor = Lacivert,
                Padding = new Padding(4, 3, 4, 3)
            };
            o.Click += (s, e) => tik();
            return o;
        }

        private class TemaRenkTablosu : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Tema.Kart; } }
            public override Color ImageMarginGradientBegin { get { return Tema.Kart; } }
            public override Color ImageMarginGradientMiddle { get { return Tema.Kart; } }
            public override Color ImageMarginGradientEnd { get { return Tema.Kart; } }
            public override Color MenuBorder { get { return Tema.Kenar; } }
            public override Color MenuItemBorder { get { return Tema.Hover; } }
            public override Color MenuItemSelected { get { return Tema.Hover; } }
            public override Color SeparatorDark { get { return Tema.Kenar; } }
            public override Color SeparatorLight { get { return Tema.Kart; } }
            public override Color CheckBackground { get { return Tema.Secili; } }
            public override Color CheckSelectedBackground { get { return Tema.Secili; } }
            public override Color CheckPressedBackground { get { return Tema.Secili; } }
        }
    }
}
