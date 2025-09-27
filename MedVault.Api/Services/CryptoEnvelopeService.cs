using System.Buffers.Binary;
using System.Security.Cryptography;


namespace MedVault.Api.Services
{
    public class CryptoEnvelopeService
    {
        private const byte Version = 1; // header version
        private const byte AlgId = 2; // 2 = AES-GCM + DPAPI-wrapped DEK
        private const int NonceSize = 12;
        private const int TagSize = 16;




        private readonly byte[]? _entropy; // extra entropy for DPAPI (pepper)
        private readonly DataProtectionScope _scope;




        public CryptoEnvelopeService(IConfiguration cfg)
        {
            var pepperHex = cfg["Crypto:PepperHex"]; // e.g., 32B hex
            _entropy = string.IsNullOrWhiteSpace(pepperHex) ? null : Convert.FromHexString(pepperHex);
            _scope = string.Equals(cfg["Crypto:DpapiScope"], "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? DataProtectionScope.LocalMachine
            : DataProtectionScope.CurrentUser;
        }



        public byte[] Encrypt(byte[] plain, out long origLen, out int padLen)
        {
            origLen = plain.LongLength;
            padLen = RandomNumberGenerator.GetInt32(0, 64 * 1024 + 1);


            var padded = new byte[plain.Length + padLen];
            Buffer.BlockCopy(plain, 0, padded, 0, plain.Length);
            if (padLen > 0) RandomNumberGenerator.Fill(padded.AsSpan(plain.Length));


            var dek = RandomNumberGenerator.GetBytes(32); // AES-256 content key
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var wrappedDek = ProtectedData.Protect(dek, _entropy, _scope); // DPAPI wrap


            var header = new byte[28 + wrappedDek.Length];
            header[0] = Version; header[1] = AlgId;
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(2, 8), origLen);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10, 4), padLen);
            nonce.CopyTo(header.AsSpan(14, NonceSize));
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26, 2), (ushort)wrappedDek.Length);
            wrappedDek.CopyTo(header.AsSpan(28));


            var ciphertext = new byte[padded.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(dek))
            {
                aes.Encrypt(nonce, padded, ciphertext, tag, header); // header as AAD
            }
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(padded);


            var blob = new byte[header.Length + ciphertext.Length + TagSize];
            Buffer.BlockCopy(header, 0, blob, 0, header.Length);
            Buffer.BlockCopy(ciphertext, 0, blob, header.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, blob, header.Length + ciphertext.Length, TagSize);
            return blob;
        }
        public byte[] Decrypt(byte[] blob)
        {
            if (blob.Length < 28 + TagSize) throw new CryptographicException("Corrupt blob.");
            if (blob[0] != Version || blob[1] != AlgId) throw new CryptographicException("Unsupported.");


            long origLen = BinaryPrimitives.ReadInt64LittleEndian(blob.AsSpan(2, 8));
            int padLen = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(10, 4));
            var nonce = blob.AsSpan(14, NonceSize).ToArray();
            ushort wrappedLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(26, 2));
            int headerLen = 28 + wrappedLen;
            if (blob.Length < headerLen + TagSize) throw new CryptographicException("Corrupt blob.");


            var header = blob.AsSpan(0, headerLen).ToArray();
            var wrappedDek = blob.AsSpan(28, wrappedLen).ToArray();
            var ctLen = blob.Length - headerLen - TagSize;
            if (ctLen < 0) throw new CryptographicException("Corrupt blob.");


            var ciphertext = blob.AsSpan(headerLen, ctLen);
            var tag = blob.AsSpan(headerLen + ctLen, TagSize);


            var dek = ProtectedData.Unprotect(wrappedDek, _entropy, _scope);


            var paddedPlain = new byte[ctLen];
            using (var aes = new AesGcm(dek))
            {
                aes.Decrypt(nonce, ciphertext, tag, paddedPlain, header);
            }
            CryptographicOperations.ZeroMemory(dek);


            if (origLen < 0 || origLen > paddedPlain.LongLength) throw new CryptographicException("Invalid length.");
            var plain = new byte[origLen];
            Buffer.BlockCopy(paddedPlain, 0, plain, 0, (int)origLen);
            CryptographicOperations.ZeroMemory(paddedPlain);
            return plain;
        }


        public byte[] EncryptString(string s, out long ol, out int pl)
        => Encrypt(System.Text.Encoding.UTF8.GetBytes(s), out ol, out pl);
        public string DecryptToString(byte[] blob)
        => System.Text.Encoding.UTF8.GetString(Decrypt(blob));
    }
}