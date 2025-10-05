
using MedVault.Api.Dtos;
using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        [Consumes("multipart/form-data")]
        [RequestFormLimits(ValueCountLimit = 10_000, MultipartBodyLengthLimit = 100_000_000)]
        public async Task<IActionResult> Upload(Guid patientId, [FromForm] IdentityDocUploadDto form)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            var p = await db.Patients.FindAsync(patientId);
            if (p is null) return NotFound("Pacijent ne postoji.");
            if (form.Scan is null || form.Scan.Length == 0) return BadRequest("Sken je obavezan.");

            var entity = new PatientIdentityDocuments
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,

                // upis naziva (šifrovan)
                DocNameEnc = crypto.EncryptString(form.DocName.Trim(), out _, out _),

                IssueDateEnc = string.IsNullOrWhiteSpace(form.IssueDateIso) ? null : crypto.EncryptString(form.IssueDateIso, out _, out _),
                ExpiryDateEnc = string.IsNullOrWhiteSpace(form.ExpiryDateIso) ? null : crypto.EncryptString(form.ExpiryDateIso, out _, out _),

                ContentType = string.IsNullOrWhiteSpace(form.Scan.ContentType) ? "application/octet-stream" : form.Scan.ContentType,
                OriginalFileNameEnc = crypto.EncryptString(form.Scan.FileName, out _, out _)
            };

            using var ms = new MemoryStream();
            await form.Scan.CopyToAsync(ms);
            entity.ScanBlob = crypto.Encrypt(ms.ToArray(), out _, out _);

            db.PatientIdentityDocuments.Add(entity);
            await db.SaveChangesAsync();
            return Ok(new { entity.Id });
        }


        [HttpGet("{patientId:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> List(Guid patientId)
        {
            var pat = await db.Patients.FindAsync(patientId); if (pat is null) return NotFound();
            var (role, dept) = Current();
            if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && pat.DepartmentId != dept) return Forbid();

            var rows = await db.PatientIdentityDocuments
                .Where(x => x.PatientId == patientId)
                .OrderByDescending(x => x.CreatedAt) // najnoviji prvi
                .Select(x => new { x.Id, x.DocNameEnc, x.ScanBlob, x.CreatedAt })
                .AsNoTracking()
                .ToListAsync();

            var list = rows.Select(r => new
            {
                r.Id,
                DocName = r.DocNameEnc != null ? crypto.DecryptToString(r.DocNameEnc) : "(Bez naziva)",
                HasScan = r.ScanBlob != null,
                r.CreatedAt
            });

            return Ok(list);
        }
        [HttpGet("download/{id:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Download(Guid id)
        {
            var doc = await db.PatientIdentityDocuments.FindAsync(id);
            if (doc is null || doc.ScanBlob is null) return NotFound();

            var pat = await db.Patients.FindAsync(doc.PatientId);
            var (role, dept) = Current();
            if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && pat?.DepartmentId != dept) return Forbid();

            byte[] plain;
            try { plain = crypto.Decrypt(doc.ScanBlob); }
            catch { return BadRequest("Decryption failed."); }

            var ct = string.IsNullOrWhiteSpace(doc.ContentType) ? null : doc.ContentType!;
            var origName = doc.OriginalFileNameEnc != null ? crypto.DecryptToString(doc.OriginalFileNameEnc) : null;

            if (ct == null || origName == null)
            {
                var (detCt, detExt) = DetectMime(plain);
                ct ??= detCt;
                
                var fallbackName = doc.DocNameEnc != null ? crypto.DecryptToString(doc.DocNameEnc) : "Dokument";
                origName ??= $"{fallbackName}{detExt}";
            }

            return File(plain, ct!, origName!);
        }

        private static (string ct, string ext) DetectMime(byte[] data)
        {
            if (data.Length >= 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return ("application/pdf", ".pdf");
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return ("image/png", ".png");
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ("image/jpeg", ".jpg");
            return ("application/octet-stream", ".bin");
        }
    }
}