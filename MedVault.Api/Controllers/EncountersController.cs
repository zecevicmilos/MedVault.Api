using System.Security.Claims;
using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedVault.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EncountersController : ControllerBase
    {
        private readonly MedVaultDbContext db;
        private readonly CryptoEnvelopeService crypto;
        public EncountersController(MedVaultDbContext db, CryptoEnvelopeService crypto)
        { this.db = db; this.crypto = crypto; }

        // Helpers
        private Guid? CurrentUserId()
        {
            var s = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub")
                    ?? User.FindFirstValue("uid");
            return Guid.TryParse(s, out var g) ? g : null;
        }
        private Guid? CurrentDeptId()
        {
            var d = User.FindFirstValue("dept");
            return Guid.TryParse(d, out var g) ? g : null;
        }
        private string Role() =>
            User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? "";

        private string DecryptOrEmpty(byte[] blob)
            => (blob == null || blob.Length == 0) ? "" : crypto.DecryptToString(blob);

        // DTO-i
        public record ScheduleDto(Guid PatientId, DateTime EncounterDate, string? Reason);
        public record CompleteDto(string? Notes);

        [HttpGet("detail/{id:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Detail(Guid id)
        {
            var enc = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
            if (enc == null) return NotFound();

            var p = await db.Patients.AsNoTracking()
                .Where(x => x.Id == enc.PatientId)
                .Select(x => new
                {
                    x.Id,
                    x.MedicalRecordNumber,
                    x.DepartmentId,
                    x.FirstNameEnc,
                    x.LastNameEnc
                })
                .SingleOrDefaultAsync();
            if (p == null) return NotFound("Pacijent nije pronađen.");

            // 🚩 Dozvoli lekaru da vidi sve iz svog odeljenja (nema više uslova za ClinicianId)
            if (Role() == "Doctor")
            {
                var dept = CurrentDeptId();
                if (dept.HasValue && p.DepartmentId.HasValue && dept != p.DepartmentId)
                    return Forbid();
            }

            var firstName = DecryptOrEmpty(p.FirstNameEnc);
            var lastName = DecryptOrEmpty(p.LastNameEnc);

            return Ok(new
            {
                enc.Id,
                enc.PatientId,
                PatientName = string.IsNullOrWhiteSpace(firstName + lastName) ? "Nepoznato" : $"{firstName} {lastName}",
                MedicalRecordNumber = p.MedicalRecordNumber,
                enc.EncounterDate,
                enc.Status,
                enc.Reason,
                enc.ClinicianId
            });
        }

        // LIST za doktora (više pacijenata) sa opcijama filtra
        [HttpGet("for-doctor"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> ListForDoctor(
            [FromQuery] string status = "Scheduled",      // Scheduled|Completed|Canceled|"" (svi)
            [FromQuery] int daysBack = 1,                 // od kada (relativno)
            [FromQuery] int daysAhead = 1400,               // do kada (relativno)
            [FromQuery] Guid? doctorId = null             // Admin može birati doktora; doktor ignoriše
        )
        {
            var now = DateTime.UtcNow;
            var from = now.AddDays(-Math.Abs(daysBack));
            var to = now.AddDays(Math.Abs(daysAhead));

            // bazni upit u vremenskom prozoru
            var q = db.Encounters.AsNoTracking()
                .Where(e => e.EncounterDate >= from && e.EncounterDate <= to);

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(e => e.Status == status);

            if (Role() == "Doctor")
            {
                var uid = CurrentUserId();
                if (uid is null) return Forbid();
                q = q.Where(e => e.ClinicianId == uid);
            }
            else if (doctorId.HasValue)
            {
                q = q.Where(e => e.ClinicianId == doctorId.Value);
            }

            // JOIN sa pacijentima (čitamo šifrovana polja)
            var rows = await q
                .Join(db.Patients.AsNoTracking(),
                      e => e.PatientId, p => p.Id,
                      (e, p) => new {
                          e.Id,
                          e.PatientId,
                          e.ClinicianId,
                          e.EncounterDate,
                          e.Status,
                          e.Reason,
                          p.FirstNameEnc,
                          p.LastNameEnc
                      })
                .OrderBy(x => x.EncounterDate)
                .ToListAsync();
             
            string Dec(byte[] b) => (b == null || b.Length == 0) ? "" : crypto.DecryptToString(b);

            var shaped = rows.Select(r => new {
                r.Id,
                r.PatientId,
                r.EncounterDate,
                r.Status,
                r.Reason,
                r.ClinicianId,
                PatientName = string.Join(" ", new[] { Dec(r.FirstNameEnc), Dec(r.LastNameEnc) }).Trim()
                                  .ToString() is string s && s.Length > 0 ? s : "Nepoznato"
            });

            return Ok(shaped);
        }


        // LIST za pacijenta (sve posete: Scheduled/Completed/Canceled)
        [HttpGet("{patientId:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> ListForPatient(Guid patientId)
        {
            // Guard: doktor sme samo pacijente iz svog odeljenja
            if (Role() == "Doctor")
            {
                var doctorDept = CurrentDeptId();
                var pDept = await db.Patients.Where(p => p.Id == patientId)
                    .Select(p => p.DepartmentId).SingleOrDefaultAsync();
                if (doctorDept.HasValue && pDept.HasValue && doctorDept != pDept)
                    return Forbid();
            }

            var list = await db.Encounters.AsNoTracking()
                .Where(e => e.PatientId == patientId)
                .OrderByDescending(e => e.EncounterDate)
                .Select(e => new { e.Id, e.EncounterDate, e.Status, e.Reason, HasNotes = e.NotesEnc != null, e.DepartmentId })
                .ToListAsync();

            return Ok(list);
        }


         
        [HttpPost("schedule"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Schedule([FromBody] ScheduleDto dto)
        {
            if (dto.EncounterDate == default) return BadRequest("EncounterDate required.");

            Guid clinicianId;
            if (Role() == "Doctor")
            {
                clinicianId = CurrentUserId() ?? throw new InvalidOperationException("Missing user id in token.");
            }
            else
            {
                clinicianId = CurrentUserId() ?? Guid.Empty; // admin može i prazno (kasnije dodela)
            }

            var p = await db.Patients.Select(x => new { x.Id, x.DepartmentId })
                                     .SingleAsync(x => x.Id == dto.PatientId);
            var docDept = await db.AppUsers.Where(u => u.Id == clinicianId)
                                           .Select(u => u.DepartmentId)
                                           .FirstOrDefaultAsync();

            if (Role() == "Doctor")
            {
                if (!docDept.HasValue || p.DepartmentId != docDept)
                    return Problem("Pacijent i doktor nisu iz istog odeljenja.", statusCode: 403);
            }

            var e = new Encounters
            {
                Id = Guid.NewGuid(),
                PatientId = dto.PatientId,
                EncounterDate = dto.EncounterDate,
                Status = "Scheduled",
                Reason = dto.Reason,
                DepartmentId = p.DepartmentId,
                CreatedAt = DateTime.UtcNow
            };

            db.Encounters.Add(e);
            await db.SaveChangesAsync();
            return Ok(new { e.Id });
        }

        [HttpPatch("{id:guid}/complete"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteDto dto)
        {
            var e = await db.Encounters.FindAsync(id);
            if (e == null) return NotFound();

            if (Role() == "Doctor")
            {
                var dept = CurrentDeptId();
                var pDept = await db.Patients.Where(p => p.Id == e.PatientId)
                                             .Select(p => p.DepartmentId)
                                             .SingleOrDefaultAsync();
                if (dept.HasValue && pDept.HasValue && dept != pDept) return Forbid();

                // 🚩 uklonjeno: if (e.ClinicianId.HasValue && e.ClinicianId != CurrentUserId()) return Forbid();
            }

            e.Status = "Completed";
            if (!string.IsNullOrWhiteSpace(dto.Notes))
                e.NotesEnc = crypto.EncryptString(dto.Notes, out _, out _);

            await db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        [HttpPatch("{id:guid}/cancel"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            var e = await db.Encounters.FindAsync(id);
            if (e == null) return NotFound();

            if (Role() == "Doctor")
            {
                var dept = CurrentDeptId();
                var pDept = await db.Patients.Where(p => p.Id == e.PatientId)
                                             .Select(p => p.DepartmentId)
                                             .SingleOrDefaultAsync();
                if (dept.HasValue && pDept.HasValue && dept != pDept) return Forbid();

 
            }

            e.Status = "Canceled";
            await db.SaveChangesAsync();
            return Ok(new { ok = true });
        }



        [HttpGet("my-upcoming"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> MyUpcoming()
        {
            var baseQuery = db.Encounters.AsNoTracking()
                .Where(e => e.Status == "Scheduled" && e.EncounterDate >= DateTime.UtcNow.AddDays(-1))
                .OrderBy(e => e.EncounterDate)
                .Take(50);

            if (Role() == "Admin")
            {
                var rows = await baseQuery
                    .Select(e => new {
                        e.Id,
                        e.PatientId,
                        e.EncounterDate,
                        e.Reason
                    })
                    .ToListAsync();

                // enrich imenima (dešifrovanje)
                var patients = await db.Patients.AsNoTracking()
                    .Where(p => rows.Select(r => r.PatientId).Contains(p.Id))
                    .Select(p => new { p.Id, p.FirstNameEnc, p.LastNameEnc })
                    .ToListAsync();

                var map = patients.ToDictionary(x => x.Id, x => $"{DecryptOrEmpty(x.FirstNameEnc)} {DecryptOrEmpty(x.LastNameEnc)}".Trim());
                var shaped = rows.Select(r => new {
                    r.Id,
                    r.PatientId,
                    r.EncounterDate,
                    r.Reason,
                    PatientName = map.TryGetValue(r.PatientId, out var n) ? (string.IsNullOrWhiteSpace(n) ? "Nepoznato" : n) : "Nepoznato"
                });

                return Ok(shaped);
            }

            var uid = CurrentUserId();
            if (uid is null) return Forbid();
            var deptId = CurrentDeptId();

            var myRows = await baseQuery
                .Where(e => e.DepartmentId == deptId)
                .Select(e => new { e.Id, e.PatientId, e.DepartmentId,e.EncounterDate, e.Reason })
                .ToListAsync();

            var myPatients = await db.Patients.AsNoTracking()
                .Where(p => myRows.Select(r => r.PatientId).Contains(p.Id))
                .Select(p => new { p.Id, p.DepartmentId, p.FirstNameEnc, p.LastNameEnc })
                .ToListAsync();
             
             
            if (deptId.HasValue)
            {
                var allowed = myPatients.Where(p => p.DepartmentId == deptId).Select(p => p.Id).ToHashSet();
                myRows = myRows.Where(r => allowed.Contains(r.PatientId)).ToList();
            }

            var map2 = myPatients.ToDictionary(x => x.Id, x => $"{DecryptOrEmpty(x.FirstNameEnc)} {DecryptOrEmpty(x.LastNameEnc)}".Trim());
            var shaped2 = myRows.Select(r => new {
                r.Id,
                r.PatientId,
                r.EncounterDate,
                r.Reason,
                PatientName = map2.TryGetValue(r.PatientId, out var n) ? (string.IsNullOrWhiteSpace(n) ? "Nepoznato" : n) : "Nepoznato"
            });

            return Ok(shaped2);
        }



    }
}