using System;

namespace MakroKeyboard
{
    // Uygulama güncellemelerinin imzasını doğrulayan AÇIK anahtar (gizli değildir).
    // Ayarlar > Genel > YAYIN HAZIRLA ilk kez kullanılınca bu dosyaya kendiliğinden yazılır.
    // Gizli anahtar yalnızca geliştiricinin bilgisayarında durur: %APPDATA%\MakroKeyboard\yayin-anahtari.xml
    // Boşsa internetten uygulama güncellemesi kapalıdır.
    internal static class GuncellemeAnahtari
    {
        public const string Acik = "<RSAKeyValue><Modulus>y5kHWUf1WKddKn0ZW3v66oWNBz0BCkEckL1j+kI2vubmB36iOAKRCwcmBVXLBViMwkBjM8dOXRywB4dYr1RjA22Z6RTkVALHa/8bFMohojrbFO/Hihihp7goQSWpjRtyNBy5SDiGWQHUn3zg0UYcC4GKMoJmfGXn1vGaPbqA3NMqtJdTiPH5luFEwkpJ5LRPMebhoaspIOWej/ZcJIsPeQaLukW9PVf1t1KgkHDL+FZyj3OOmK+bzM9NtbA82PynXGiCWueDPNyTRH5bySYCjYap3ZKQ6eMWYNVKKVSVcsNFcXroQAi0+6qKCxiX8nJcjmDbsXpxJYbfVnOC2MkXuQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}
