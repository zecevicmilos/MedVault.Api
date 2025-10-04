using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MedVault.Api.Dtos
{
    public class IdentityDocUploadDto
    {
        [Required] public string DocType { get; set; } = default!;
        [Required] public string DocNumber { get; set; } = default!;
        public string? IssueDateIso { get; set; }
        public string? ExpiryDateIso { get; set; }
        public IFormFile? Scan { get; set; }           
    }
}