using System.Security.Cryptography;


namespace MedVault.Api.Services
{
    public class SearchIndexService
    {
        private readonly byte[] _pepper;
        public SearchIndexService(IConfiguration cfg)
        {
            _pepper = Convert.FromHexString(cfg["Crypto:PepperHex"]!);
        }
        public byte[] HmacIndex(string value)
        {
            using var h = new HMACSHA256(_pepper);
            return h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        }
    }
}