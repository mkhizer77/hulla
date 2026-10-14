using System;
using System.Security.Cryptography;
using System.Text;

namespace Hulla.Infrastructure.Content
{
    /// <summary>
    /// Checks the RSA-SHA256 (PKCS #1 v1.5) signature over the manifest's signed bytes against the
    /// public key in the binary (ADR-0011). The key is PEM, either SubjectPublicKeyInfo
    /// ("BEGIN PUBLIC KEY") or PKCS #1 ("BEGIN RSA PUBLIC KEY").
    /// </summary>
    public static class ManifestSignature
    {
        public static bool Verify(string signedBytes, string? signatureBase64, RSAParameters key)
        {
            if (string.IsNullOrEmpty(signatureBase64))
            {
                return false;
            }

            try
            {
                var signature = Convert.FromBase64String(signatureBase64);
                // The concrete type, not RSA.Create(): IL2CPP strips what CryptoConfig finds by name.
                using var rsa = new RSACryptoServiceProvider();
                rsa.ImportParameters(key);
                return rsa.VerifyData(Encoding.UTF8.GetBytes(signedBytes), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Null when the text holds no RSA public key.</summary>
        public static RSAParameters? ParsePublicKey(string pem)
        {
            try
            {
                var der = Convert.FromBase64String(Body(pem));
                var reader = new Der(der);
                reader.Enter(0x30);
                if (der.Length > reader.Position && der[reader.Position] == 0x30)
                {
                    // SubjectPublicKeyInfo: the algorithm, then the PKCS #1 key in a bit string.
                    reader.Skip();
                    reader.Enter(0x03);
                    if (reader.Byte() != 0)
                    {
                        return null;
                    }

                    reader.Enter(0x30);
                }

                var modulus = reader.Integer();
                var exponent = reader.Integer();
                return modulus.Length >= 128 ? new RSAParameters { Modulus = modulus, Exponent = exponent } : (RSAParameters?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Body(string pem)
        {
            var text = new StringBuilder();
            foreach (var line in pem.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("-----"))
                {
                    text.Append(trimmed);
                }
            }

            return text.ToString();
        }

        /// <summary>Just enough DER to walk a public key.</summary>
        private sealed class Der
        {
            private readonly byte[] _bytes;

            public Der(byte[] bytes)
            {
                _bytes = bytes;
            }

            public int Position { get; private set; }

            public byte Byte() => _bytes[Position++];

            public int Enter(byte tag)
            {
                if (Byte() != tag)
                {
                    throw new FormatException("unexpected tag");
                }

                return Length();
            }

            public void Skip()
            {
                Position++;
                var length = Length();
                Position += length;
            }

            public byte[] Integer()
            {
                var length = Enter(0x02);
                var start = Position;
                Position += length;
                while (length > 1 && _bytes[start] == 0)
                {
                    start++;
                    length--;
                }

                var value = new byte[length];
                Array.Copy(_bytes, start, value, 0, length);
                return value;
            }

            private int Length()
            {
                int first = Byte();
                if (first < 0x80)
                {
                    return first;
                }

                var length = 0;
                for (var i = 0; i < (first & 0x7F); i++)
                {
                    length = (length << 8) | Byte();
                }

                if (length < 0 || Position + length > _bytes.Length)
                {
                    throw new FormatException("bad length");
                }

                return length;
            }
        }
    }
}
