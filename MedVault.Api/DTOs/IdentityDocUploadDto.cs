using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MedVault.Api.Dtos
{
    public class IdentityDocUploadDto
    {
        [Required] public string DocName { get; set; } = default!;   // Naziv dokumenta
        [Required] public IFormFile Scan { get; set; } = default!;   // obavezno

        public string? IssueDateIso { get; set; }
        public string? ExpiryDateIso { get; set; }
    }
}