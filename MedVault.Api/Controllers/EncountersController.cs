
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
    public class EncountersController(MedVaultDbContext db, CryptoEnvelopeService crypto) : ControllerBase
    {
        private (string role, Guid? dept) Current()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var dept = User.FindFirst("dept")?.Value;
            return (role, Guid.TryParse(dept, out var d) ? d : null);
        }



        [HttpPost, Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Create([FromForm] Guid patientId, [FromForm] DateTime encounterDate, [FromForm] string? notes)
        {
            var p = await db.Patients.FindAsync(patientId); if (p is null) return NotFound();
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && p.DepartmentId != dept) return Forbid();
            var e = new Models.Encounters
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                EncounterDate = encounterDate,
                NotesEnc = string.IsNullOrWhiteSpace(notes) ? null : crypto.EncryptString(notes, out _, out _)
            };
            db.Encounters.Add(e);
            await db.SaveChangesAsync();
            return Ok(new { e.Id });
        }


        [HttpGet("{patientId:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> List(Guid patientId)
        {
            var p = await db.Patients.FindAsync(patientId); if (p is null) return NotFound();
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && p.DepartmentId != dept) return Forbid();
            var list = await db.Encounters.Where(x => x.PatientId == patientId)
            .OrderByDescending(x => x.EncounterDate)
            .Select(x => new { x.Id, x.EncounterDate })
            .ToListAsync();
            return Ok(list);
        }

        [HttpGet("detail/{id:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Detail(Guid id)
        {
            var e = await db.Encounters.FindAsync(id); if (e is null) return NotFound();
            var p = await db.Patients.FindAsync(e.PatientId);
            var (role, dept) = Current(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && p?.DepartmentId != dept) return Forbid();
            string? notes = e.NotesEnc is null ? null : crypto.DecryptToString(e.NotesEnc);
            return Ok(new { e.Id, e.PatientId, e.EncounterDate, Notes = notes });
        }
    }
}