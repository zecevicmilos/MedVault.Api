
using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;


namespace MedVault.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IdentityDocumentsController(MedVaultDbContext db, CryptoEnvelopeService crypto) : ControllerBase
    {
        private (string role, Guid? dept) Current()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var dept = User.FindFirst("dept")?.Value;
            return (role, Guid.TryParse(dept, out var d) ? d : null);
        }



        [HttpPost("{patientId:guid}"), Authorize(Policy = "AdminOnly")]
        [RequestSizeLimit(25_000_000)]
        public async Task<IActionResult> Upload(Guid patientId, [FromForm] IFormFile? scan, [FromForm] string docType, [FromForm] string docNumber,
        [FromForm] string? issueDateIso, [FromForm] string? expiryDateIso)
        {
            var doc = new Models.PatientIdentityDocuments
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                DocType = docType,
                DocNumberEnc = crypto.EncryptString(docNumber, out _, out _),
                IssueDateEnc = string.IsNullOrWhiteSpace(issueDateIso) ? null : crypto.EncryptString(issueDateIso, out _, out _),
                ExpiryDateEnc = string.IsNullOrWhiteSpace(expiryDateIso) ? null : crypto.EncryptString(expiryDateIso, out _, out _)
            };
            if (scan != null) { using var ms = new MemoryStream(); await scan.CopyToAsync(ms); doc.ScanBlob = crypto.Encrypt(ms.ToArray(), out _, out _); }
            db.PatientIdentityDocuments.Add(doc);
            await db.SaveChangesAsync();
            return Ok(new { doc.Id });
        }

        [HttpGet("{patientId:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> List(Guid patientId)
        {
            var p = await db.Patients.FindAsync(patientId); if (p is null) return NotFound();
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && p.DepartmentId != dept) return Forbid();
            var list = await db.PatientIdentityDocuments.Where(x => x.PatientId == patientId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.DocType, HasScan = x.ScanBlob != null, x.CreatedAt })
            .ToListAsync();
            return Ok(list);
        }


        [HttpGet("download/{id:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Download(Guid id)
        {
            var doc = await db.PatientIdentityDocuments.FindAsync(id); if (doc is null || doc.ScanBlob is null) return NotFound();
            var p = await db.Patients.FindAsync(doc.PatientId);
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && p?.DepartmentId != dept) return Forbid();
            byte[] plain; try { plain = crypto.Decrypt(doc.ScanBlob); } catch { return BadRequest("Decryption failed."); }
            return File(plain, "application/octet-stream", $"{doc.DocType}-scan.bin");
        }
    }
}