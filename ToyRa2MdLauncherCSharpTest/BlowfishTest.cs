using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToyRa2MdLauncherCSharp.AlefCrypto;

namespace ToyRa2MdLauncherCSharpTest {
    [TestClass]
    public class BlowfishTest {
        [TestMethod]
        [DataRow(
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, // 0000000000000000
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, // 0000000000000000
            new byte[] { 0x4E, 0xF9, 0x97, 0x45, 0x61, 0x98, 0xDD, 0x78 } // 4EF997456198DD78
            )]
        [DataRow(
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, // FFFFFFFFFFFFFFFF
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, // FFFFFFFFFFFFFFFF
            new byte[] { 0x51, 0x86, 0x6F, 0xD5, 0xB8, 0x5E, 0xCB, 0x8A } // 51866FD5B85ECB8A
            )]
        public void TestBlowfishEncryptAndDecrypt(byte[] key, byte[] plaintext, byte[] expectedCiphertext) {
            BlowfishContext bf = new BlowfishContext(key);

            byte[] ciphertext = plaintext.Clone() as byte[];
            bf.Encrypt(ciphertext, ciphertext.Length);

            CollectionAssert.AreEqual(expectedCiphertext, ciphertext);

            byte[] decrypted = ciphertext.Clone() as byte[];
            bf.Decrypt(decrypted, decrypted.Length);

            CollectionAssert.AreEqual(plaintext, decrypted);
        }
    }
}

