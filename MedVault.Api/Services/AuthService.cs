using System.Buffers.Binary;
using System.Security.Cryptography;


namespace MedVault.Api.Services
{
    public class AuthService
    {
        public byte[] HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var iters = 210_000;
            var dk = Rfc2898DeriveBytes.Pbkdf2(password, salt, iters, HashAlgorithmName.SHA256, 32);
            var result = new byte[4 + 16 + 32];
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), iters);
            salt.CopyTo(result.AsSpan(4, 16));
            dk.CopyTo(result.AsSpan(20, 32));
            return result;
        }
        public bool VerifyPassword(string password, byte[] packed)
        {
            var iters = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(0, 4));
            var salt = packed.AsSpan(4, 16).ToArray();
            var dk = packed.AsSpan(20, 32).ToArray();
            var test = Rfc2898DeriveBytes.Pbkdf2(password, salt, iters, HashAlgorithmName.SHA256, dk.Length);
            return CryptographicOperations.FixedTimeEquals(test, dk);
        }
    }
}