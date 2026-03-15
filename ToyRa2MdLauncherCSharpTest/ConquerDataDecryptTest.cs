using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text;
using ToyRa2MdLauncherCSharp.AlefCrypto;

namespace ToyRa2MdLauncherCSharpTest {
    [TestClass]
    public class ConquerDataDecryptTest {
        [TestMethod]
        [DataRow(
            "3183e-8034262613281225414183-76481-640-8834005-23420",
            // B3 22 F5 1C 83 5B A2 9B AB 4F DD 3B CC 06 E0 89 94 51 AE C1 CF 03 D1 5C 7B 29 BE 03 D9 A0 4F A1 70 B9 02 48 73 24 9C 31 BB AB 20 3D BC 8F 26 5C 65 64
            new byte[] { 0xB3, 0x22, 0xF5, 0x1C, 0x83, 0x5B, 0xA2, 0x9B, 0xAB, 0x4F, 0xDD, 0x3B, 0xCC, 0x06, 0xE0, 0x89, 0x94, 0x51, 0xAE, 0xC1, 0xCF, 0x03, 0xD1, 0x5C, 0x7B, 0x29, 0xBE, 0x03, 0xD9, 0xA0, 0x4F, 0xA1, 0x70, 0xB9, 0x02, 0x48, 0x73, 0x24, 0x9C, 0x31, 0xBB, 0xAB, 0x20, 0x3D, 0xBC, 0x8F, 0x26, 0x5C, 0x65, 0x64 },
            "(c) 2000 Electronic Arts, Inc. All Rights Reserved"
        )]
        [DataRow(
            "3183e-8284831329166779735047-76481-640-8834005-23420",
            new byte[] { 0xB0, 0xDB, 0xEE, 0x68, 0xC1, 0x7C, 0x05, 0x0C, 0x8B, 0xBB, 0xA6, 0x48, 0x72, 0x56, 0xD1, 0x1C, 0x50, 0x53, 0x00 },
            "UIDATA,3DDATA,MAPS\0"
        )]
        public void TestConquerDatDecrypt(string key, byte[] conquerData, string expectedPlaintext) {
            byte[] expectedPlaintextBytes = Encoding.ASCII.GetBytes(expectedPlaintext);

            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            BlowfishContext bf = new BlowfishContext(keyBytes);

            int numBlocks = conquerData.Length / 8;
            for (int i = 0; i < numBlocks; i++) {
                byte[] block = new byte[8];
                Array.Copy(conquerData, i * 8, block, 0, 8);

                bf.Decrypt(block, block.Length);
                Assert.IsTrue(block.AsSpan().SequenceEqual(expectedPlaintextBytes.AsSpan(i * 8, 8)), $"Block {i} does not match expected result.");
            }

            // Remainder (if any) is left unmodified. This is intentional.
            Assert.IsTrue(conquerData.AsSpan(numBlocks * 8).SequenceEqual(expectedPlaintextBytes.AsSpan(numBlocks * 8)), "Remainder does not match expected result.");
        }
    }
}
