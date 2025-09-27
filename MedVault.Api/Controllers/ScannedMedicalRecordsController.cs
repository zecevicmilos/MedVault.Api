using MedVault.Api. Models;
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
    public class ScannedMedicalRecordsController(MedVaultDbContext db, CryptoEnvelopeService crypto) : ControllerBase
    {
        private (string role, Guid? dept) Current()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var dept = User.FindFirst("dept")?.Value;
            return (role, Guid.TryParse(dept, out var d) ? d : null);
        }

        [HttpPost("{patientId:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> Upload(Guid patientId, [FromForm] IFormFile file, [FromForm] string recordType)
        {
            var patient = await db.Patients.FindAsync(patientId); if (patient is null) return NotFound();
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && patient.DepartmentId != dept) return Forbid();


            using var ms = new MemoryStream(); await file.CopyToAsync(ms);
            var blob = crypto.Encrypt(ms.ToArray(), out long ol, out int pl);


            Guid? uploader = null; var sid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; if (Guid.TryParse(sid, out var uid)) uploader = uid;


            var rec = new Models.ScannedMedicalRecords
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                RecordType = recordType,
                OriginalFileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                OriginalLength = ol,
                PaddingLength = pl,
                Blob = blob,
                UploadedBy = uploader
            };
            db.ScannedMedicalRecords.Add(rec);
            await db.SaveChangesAsync();
            db.AuditLog.Add(new Models.AuditLog { Action = "CREATE", Entity = "ScannedMedicalRecords", EntityId = rec.Id, Success = true, UserId = uploader });
            await db.SaveChangesAsync();
            return Ok(new { rec.Id });
        }


        [HttpGet("{patientId:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> List(Guid patientId)
        {
            var patient = await db.Patients.FindAsync(patientId); if (patient is null) return NotFound();
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && patient.DepartmentId != dept) return Forbid();
            var list = await db.ScannedMedicalRecords.Where(x => x.PatientId == patientId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.RecordType, x.OriginalFileName, x.ContentType, x.CreatedAt, x.OriginalLength, x.PaddingLength })
            .ToListAsync();
            return Ok(list);
        }


        [HttpGet("download/{id:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Download(Guid id)
        {
            var rec = await db.ScannedMedicalRecords.FindAsync(id); if (rec is null) return NotFound();
            var patient = await db.Patients.FindAsync(rec.PatientId);
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && patient?.DepartmentId != dept) return Forbid();
            byte[] plain; try { plain = crypto.Decrypt(rec.Blob); } catch { return BadRequest("Decryption failed."); }
            return File(plain, rec.ContentType, rec.OriginalFileName);
        }


        [HttpDelete("{id:guid}"), Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var rec = await db.ScannedMedicalRecords.FindAsync(id); if (rec is null) return NotFound();
            db.ScannedMedicalRecords.Remove(rec);
            await db.SaveChangesAsync();
            return NoContent();
        }
    }
}