namespace MakroKeyboard
{
    // Ayarlar > Profiller > HAZIR PROFİLLER menüsündeki tek tıkla yüklenen düzenler.
    // Biçim profil dosyasıyla aynı: KEY=değer (karta gider), #ADn / #RENKn (yalnızca uygulamada görünür).
    // LED ayarları bu metinlerde yok: yüklerken karttaki mevcut LED ayarı korunur.
    // Tuş sırası: BUTTON0-7 = 1-8 numaralı tuşlar, BUTTON8 = LED tuşu (9), BUTTON9 = encoder'a basma.
    public static class HazirProfiller
    {
        public class Sablon
        {
            public string Ad, Aciklama, Metin;
        }

        public static readonly Sablon[] Liste =
        {
            new Sablon
            {
                Ad = "Ofis",
                Aciklama = "Kopyala, yapıştır, kes, geri al (uzun basış: yinele), kaydet, bul, ekran görüntüsü, masaüstü. " +
                           "Encoder: ses.",
                Metin =
                    "BUTTON0=CTRL+C\n" +
                    "BUTTON1=CTRL+V\n" +
                    "BUTTON2=CTRL+X\n" +
                    "BUTTON3=CTRL+Z\n" +
                    "BUTTON3_LONG=CTRL+Y\n" +
                    "BUTTON4=CTRL+S\n" +
                    "BUTTON5=CTRL+F\n" +
                    "BUTTON6=SHIFT+WINDOWS+S\n" +
                    "BUTTON7=WINDOWS+D\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=MUTE\n" +
                    "ENCODER_CW=VOLUME_UP\n" +
                    "ENCODER_CCW=VOLUME_DOWN\n" +
                    "#AD3=Geri al / Yinele\n"
            },
            new Sablon
            {
                Ad = "Müzik ve medya",
                Aciklama = "Önceki, oynat/durdur, sonraki, sessiz, Alt+Tab, masaüstü, kopyala, yapıştır. " +
                           "Encoder: ses, basınca oynat/durdur.",
                Metin =
                    "BUTTON0=MEDIA_PREVIOUS\n" +
                    "BUTTON1=MEDIA_PLAY_PAUSE\n" +
                    "BUTTON2=MEDIA_NEXT\n" +
                    "BUTTON3=MUTE\n" +
                    "BUTTON4=ALT+TAB\n" +
                    "BUTTON5=WINDOWS+D\n" +
                    "BUTTON6=CTRL+C\n" +
                    "BUTTON7=CTRL+V\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=MEDIA_PLAY_PAUSE\n" +
                    "ENCODER_CW=VOLUME_UP\n" +
                    "ENCODER_CCW=VOLUME_DOWN\n" +
                    "#RENK1=16A34A\n"
            },
            new Sablon
            {
                Ad = "Tarayıcı",
                Aciklama = "Yeni sekme, sekmeyi kapat, kapanan sekmeyi aç, yenile, geri, ileri, önceki / sonraki sekme. " +
                           "Encoder: sayfayı kaydır, basınca yakınlığı sıfırla.",
                Metin =
                    "BUTTON0=CTRL+T\n" +
                    "BUTTON1=CTRL+W\n" +
                    "BUTTON2=CTRL+SHIFT+T\n" +
                    "BUTTON3=F5\n" +
                    "BUTTON4=ALT+LEFT\n" +
                    "BUTTON5=ALT+RIGHT\n" +
                    "BUTTON6=CTRL+SHIFT+TAB\n" +
                    "BUTTON7=CTRL+TAB\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=CTRL+0\n" +
                    "ENCODER_CW=SCROLL_DOWN\n" +
                    "ENCODER_CCW=SCROLL_UP\n" +
                    "#AD4=Geri\n" +
                    "#AD5=İleri\n" +
                    "#AD6=Önceki sekme\n" +
                    "#AD7=Sonraki sekme\n" +
                    "#AD9=Yakınlık sıfırla\n"
            },
            new Sablon
            {
                Ad = "Toplantı (Teams)",
                Aciklama = "Microsoft Teams: mikrofon, kamera, el kaldır, ekran paylaş, görüşmeden ayrıl. " +
                           "Encoder: ses.",
                Metin =
                    "BUTTON0=CTRL+SHIFT+M\n" +
                    "BUTTON1=CTRL+SHIFT+O\n" +
                    "BUTTON2=CTRL+SHIFT+K\n" +
                    "BUTTON3=CTRL+SHIFT+E\n" +
                    "BUTTON4=ALT+TAB\n" +
                    "BUTTON5=WINDOWS+D\n" +
                    "BUTTON6=SHIFT+WINDOWS+S\n" +
                    "BUTTON7=CTRL+SHIFT+H\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=MUTE\n" +
                    "ENCODER_CW=VOLUME_UP\n" +
                    "ENCODER_CCW=VOLUME_DOWN\n" +
                    "#AD0=Mikrofon\n" +
                    "#RENK0=16A34A\n" +
                    "#AD1=Kamera\n" +
                    "#RENK1=1068E8\n" +
                    "#AD2=El kaldır\n" +
                    "#RENK2=CA8A04\n" +
                    "#AD3=Ekran paylaş\n" +
                    "#AD7=Ayrıl\n" +
                    "#RENK7=DC2626\n"
            },
            new Sablon
            {
                Ad = "Oyun ve yayın",
                Aciklama = "Xbox Game Bar (aç, kaydı başlat, görüntü al), Discord mikrofon / kulaklık, müzik, Alt+Tab. " +
                           "Encoder: ses.",
                Metin =
                    "BUTTON0=WINDOWS+G\n" +
                    "BUTTON1=ALT+WINDOWS+R\n" +
                    "BUTTON2=ALT+WINDOWS+PRINT_SCREEN\n" +
                    "BUTTON3=CTRL+SHIFT+M\n" +
                    "BUTTON4=CTRL+SHIFT+D\n" +
                    "BUTTON5=MEDIA_PLAY_PAUSE\n" +
                    "BUTTON6=MEDIA_NEXT\n" +
                    "BUTTON7=ALT+TAB\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=MUTE\n" +
                    "ENCODER_CW=VOLUME_UP\n" +
                    "ENCODER_CCW=VOLUME_DOWN\n" +
                    "#AD0=Game Bar\n" +
                    "#AD1=Kayıt\n" +
                    "#RENK1=DC2626\n" +
                    "#AD2=Oyun görüntüsü\n" +
                    "#AD3=Discord mikrofon\n" +
                    "#RENK3=7C3AED\n" +
                    "#AD4=Discord kulaklık\n" +
                    "#RENK4=7C3AED\n"
            },
            new Sablon
            {
                Ad = "Video düzenleme (Premiere)",
                Aciklama = "Adobe Premiere Pro: oynat/durdur, kare geri / ileri, kes, geri al, yinele, kaydet, sil. " +
                           "Encoder: kare kare ilerle.",
                Metin =
                    "BUTTON0=SPACE\n" +
                    "BUTTON1=LEFT\n" +
                    "BUTTON2=RIGHT\n" +
                    "BUTTON3=CTRL+K\n" +
                    "BUTTON4=CTRL+Z\n" +
                    "BUTTON5=CTRL+SHIFT+Z\n" +
                    "BUTTON6=CTRL+S\n" +
                    "BUTTON7=DELETE\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=MUTE\n" +
                    "ENCODER_CW=RIGHT\n" +
                    "ENCODER_CCW=LEFT\n" +
                    "#AD0=Oynat / Durdur\n" +
                    "#RENK0=16A34A\n" +
                    "#AD1=Kare geri\n" +
                    "#AD2=Kare ileri\n" +
                    "#AD3=Kes\n" +
                    "#RENK3=EA580C\n" +
                    "#AD5=Yinele\n" +
                    "#AD7=Sil\n" +
                    "#AD9=Ses kapat\n"
            },
            new Sablon
            {
                Ad = "Sayı tuş takımı",
                Aciklama = "Sayı tuş takımı olmayan dizüstüler için: 1-9 rakamlar (9 = LED tuşu), encoder'a basma 0, " +
                           "uzun basma Enter. Uzun basış: 1 +, 2 -, 3 *, 4 /, 5 virgül, 6 sil. Encoder: imleç sağa / sola.",
                Metin =
                    "BUTTON0=1\n" +
                    "BUTTON0_LONG=KEYPAD_PLUS\n" +
                    "BUTTON1=2\n" +
                    "BUTTON1_LONG=KEYPAD_MINUS\n" +
                    "BUTTON2=3\n" +
                    "BUTTON2_LONG=KEYPAD_ASTERISK\n" +
                    "BUTTON3=4\n" +
                    "BUTTON3_LONG=KEYPAD_FORWARD_SLASH\n" +
                    "BUTTON4=5\n" +
                    "BUTTON4_LONG=KEYPAD_PERIOD\n" +
                    "BUTTON5=6\n" +
                    "BUTTON5_LONG=BACKSPACE\n" +
                    "BUTTON6=7\n" +
                    "BUTTON7=8\n" +
                    "BUTTON8=9\n" +
                    "BUTTON9=0\n" +
                    "BUTTON9_LONG=ENTER\n" +
                    "ENCODER_CW=RIGHT\n" +
                    "ENCODER_CCW=LEFT\n" +
                    "#AD0=1  (uzun: +)\n" +
                    "#AD1=2  (uzun: -)\n" +
                    "#AD2=3  (uzun: *)\n" +
                    "#AD3=4  (uzun: /)\n" +
                    "#AD4=5  (uzun: ,)\n" +
                    "#AD5=6  (uzun: sil)\n" +
                    "#AD6=7\n" +
                    "#AD7=8\n" +
                    "#AD8=9\n" +
                    "#AD9=0  (uzun: Enter)\n"
            },
            new Sablon
            {
                Ad = "OBS Studio (F13-F20)",
                Aciklama = "Tuşlar F13-F20 gönderir; gerçek klavyede olmadıkları için hiçbir şeyle çakışmaz. " +
                           "OBS > Ayarlar > Kısayollar'da bir kutuya tıklayıp makro klavyedeki tuşa bas. Encoder: ses.",
                Metin =
                    "BUTTON0=F13\n" +
                    "BUTTON1=F14\n" +
                    "BUTTON2=F15\n" +
                    "BUTTON3=F16\n" +
                    "BUTTON4=F17\n" +
                    "BUTTON5=F18\n" +
                    "BUTTON6=F19\n" +
                    "BUTTON7=F20\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=MUTE\n" +
                    "ENCODER_CW=VOLUME_UP\n" +
                    "ENCODER_CCW=VOLUME_DOWN\n" +
                    "#AD0=Kayıt\n" +
                    "#RENK0=DC2626\n" +
                    "#AD1=Yayın\n" +
                    "#RENK1=7C3AED\n" +
                    "#AD2=Sahne 1\n" +
                    "#AD3=Sahne 2\n" +
                    "#AD4=Sahne 3\n" +
                    "#AD5=Mikrofon\n" +
                    "#RENK5=16A34A\n" +
                    "#AD6=Masaüstü sesi\n" +
                    "#AD7=Tekrar kaydet\n"
            },
            new Sablon
            {
                Ad = "Visual Studio",
                Aciklama = "Başlat, durdur, derle, kesme noktası, adım atla, içine gir, yorum yap (uzun: yorumu kaldır), " +
                           "belgeyi biçimlendir. Encoder: kaydır, basınca kaydet.",
                Metin =
                    "BUTTON0=F5\n" +
                    "BUTTON1=SHIFT+F5\n" +
                    "BUTTON2=CTRL+SHIFT+B\n" +
                    "BUTTON3=F9\n" +
                    "BUTTON4=F10\n" +
                    "BUTTON5=F11\n" +
                    "BUTTON6=MACRO:CTRL+K|CTRL+C\n" +
                    "BUTTON6_LONG=MACRO:CTRL+K|CTRL+U\n" +
                    "BUTTON7=MACRO:CTRL+K|CTRL+D\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=CTRL+S\n" +
                    "ENCODER_CW=SCROLL_DOWN\n" +
                    "ENCODER_CCW=SCROLL_UP\n" +
                    "#AD0=Başlat\n" +
                    "#RENK0=16A34A\n" +
                    "#AD1=Durdur\n" +
                    "#RENK1=DC2626\n" +
                    "#AD2=Derle\n" +
                    "#AD3=Kesme noktası\n" +
                    "#AD4=Adım atla\n" +
                    "#AD5=İçine gir\n" +
                    "#AD6=Yorum yap\n" +
                    "#AD7=Biçimlendir\n" +
                    "#AD9=Kaydet\n"
            },
            new Sablon
            {
                Ad = "Sunum (PowerPoint)",
                Aciklama = "Sunumu baştan / bu slayttan başlat, önceki / sonraki slayt, siyah ekran, beyaz ekran, " +
                           "lazer işaretçi, bitir. Encoder: slayt ileri / geri.",
                Metin =
                    "BUTTON0=F5\n" +
                    "BUTTON1=SHIFT+F5\n" +
                    "BUTTON2=LEFT\n" +
                    "BUTTON3=RIGHT\n" +
                    "BUTTON4=B\n" +
                    "BUTTON5=W\n" +
                    "BUTTON6=CTRL+L\n" +
                    "BUTTON7=ESCAPE\n" +
                    "BUTTON8=PIXEL_MODE\n" +
                    "BUTTON9=RIGHT\n" +
                    "ENCODER_CW=RIGHT\n" +
                    "ENCODER_CCW=LEFT\n" +
                    "#AD0=Baştan başlat\n" +
                    "#RENK0=16A34A\n" +
                    "#AD1=Bu slayttan\n" +
                    "#AD2=Önceki\n" +
                    "#AD3=Sonraki\n" +
                    "#AD4=Siyah ekran\n" +
                    "#AD5=Beyaz ekran\n" +
                    "#AD6=Lazer\n" +
                    "#AD7=Bitir\n" +
                    "#RENK7=DC2626\n" +
                    "#AD9=Sonraki\n"
            }
        };
    }
}