using System.Security.Cryptography;
using System.Text;

namespace comviaServer.Security;

/// <summary>
/// הצפנה/פענוח של פרטי התחברות לספקים (סיסמאות) לפני שמירה ב-DB.
/// AES-CBC עם IV אקראי לכל ערך. הפלט הוא Base64 של (IV + ciphertext).
/// המפתח נגזר מ-Credentials:EncryptionKey שב-appsettings.
/// </summary>
public class CredentialProtector
{
    private readonly byte[] _key;

    public CredentialProtector(IConfiguration config)
    {
        var passphrase = config["Credentials:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new Exception("Credentials:EncryptionKey is not configured");

        // גוזרים מפתח 256-bit קבוע מתוך ה-passphrase
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
    }

    /// <summary>מצפין טקסט גלוי. מחזיר null עבור קלט ריק.</summary>
    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return null;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var output = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, output, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(output);
    }

    /// <summary>מפענח ערך שהוצפן ע"י Encrypt. מחזיר null עבור קלט ריק/לא תקין.</summary>
    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return null;

        try
        {
            var raw = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = _key;

            int ivLen = aes.BlockSize / 8; // 16 בתים
            if (raw.Length <= ivLen) return null;

            var iv = new byte[ivLen];
            var cipherBytes = new byte[raw.Length - ivLen];
            Buffer.BlockCopy(raw, 0, iv, 0, ivLen);
            Buffer.BlockCopy(raw, ivLen, cipherBytes, 0, cipherBytes.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }
}
