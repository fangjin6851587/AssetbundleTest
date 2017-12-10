using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AssetBundles
{
    public class Crypto
    {
        private const string PASS_KEY = "43EC94013158CCDDF4E5CC99044CD2D7";
        private const string SALT_KEY = "2057E908296E519C76FF01DBFAD30B67";

        public static byte[] AesDecryptBytes(byte[] cryptBytes)
        {
            byte[] clearBytes;

            var key = new Rfc2898DeriveBytes(PASS_KEY, Encoding.Default.GetBytes(SALT_KEY));

            using (Aes aes = new AesManaged())
            {
                // set the key size to 256
                aes.KeySize = 256;
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);

                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(cryptBytes, 0, cryptBytes.Length);
                        cs.Close();
                    }
                    clearBytes = ms.ToArray();
                }
            }
            return clearBytes;
        }

        public static byte[] AesEncryptBytes(byte[] clearBytes)
        {
            byte[] encryptedBytes;

            var key = new Rfc2898DeriveBytes(PASS_KEY, Encoding.Default.GetBytes(SALT_KEY));

            // create an AES object
            using (Aes aes = new AesManaged())
            {
                // set the key size to 256
                aes.KeySize = 256;
                aes.Key = key.GetBytes(aes.KeySize / 8);
                aes.IV = key.GetBytes(aes.BlockSize / 8);
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(),
                        CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }
                    encryptedBytes = ms.ToArray();
                }
            }
            return encryptedBytes;
        }
    }
}