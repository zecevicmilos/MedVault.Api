
using MedVault.Api.Dtos;
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
    public class PatientsController(MedVaultDbContext db, CryptoEnvelopeService crypto, SearchIndexService idx) : ControllerBase
    {
        private (string role, Guid? deptId) CurrentRole()
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var dept = User.FindFirst("dept")?.Value;
            return (role, Guid.TryParse(dept, out var d) ? d : null);
        }
        
        [HttpPost("create"), Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Create([FromBody] PatientCreateDto dto)
        {
            static string OnlyDigits(string s) => new string(s.Where(char.IsDigit).ToArray());
            byte[] Enc(string s) => crypto.Encrypt(System.Text.Encoding.UTF8.GetBytes(s), out _, out _);

            var p = new Models.Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = dto.MedicalRecordNumber,
                LastNameEnc = Enc(dto.LastName),
                FirstNameEnc = Enc(dto.FirstName),
                JMBGEnc = Enc(dto.JMBG), // čuva originalan unos (OK)
                AddressEnc = string.IsNullOrWhiteSpace(dto.Address) ? null : Enc(dto.Address),
                PhoneEnc = string.IsNullOrWhiteSpace(dto.Phone) ? null : Enc(dto.Phone),
                EmailEnc = string.IsNullOrWhiteSpace(dto.Email) ? null : Enc(dto.Email),

                LastNameHmac = idx.HmacIndex(dto.LastName.Trim()),
                JmbgHmac = idx.HmacIndex(OnlyDigits(dto.JMBG)),

                DepartmentId = dto.DepartmentId
            };

            db.Patients.Add(p);
            await db.SaveChangesAsync();
            return Ok(new { p.Id });
        }


        
        [HttpGet("search"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Search([FromQuery] string? lastName, [FromQuery] string? jmbg)
        {
            static string OnlyDigits(string s) => new string(s.Where(char.IsDigit).ToArray());

            var (role, deptId) = CurrentRole();
            IQueryable<Models.Patients> q = db.Patients;

            if (!string.IsNullOrWhiteSpace(jmbg))
            {
                var norm = OnlyDigits(jmbg.Trim());
                var jmbgHash = idx.HmacIndex(norm);
                byte[]? legacyHash = norm == jmbg.Trim() ? null : idx.HmacIndex(jmbg.Trim());

                q = q.Where(p => p.JmbgHmac != null &&
                                 (p.JmbgHmac!.SequenceEqual(jmbgHash) ||
                                  (legacyHash != null && p.JmbgHmac!.SequenceEqual(legacyHash))));
            }

            if (!string.IsNullOrWhiteSpace(lastName))
            {
                var lnHash = idx.HmacIndex(lastName.Trim());
                q = q.Where(p => p.LastNameHmac != null && p.LastNameHmac!.SequenceEqual(lnHash));
            }

            if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase))
                q = q.Where(p => p.DepartmentId == deptId);

            // 1) pokupi potrebna polja iz baze
            var raw = await q.OrderByDescending(p => p.CreatedAt)
                             .Take(50)
                             .Select(p => new { p.Id, p.MedicalRecordNumber, p.CreatedAt, p.FirstNameEnc, p.LastNameEnc })
                             .ToListAsync();

            // 2) dešifruj ime i prezime u memoriji i vrati čista polja
            string Dec(byte[] b) => crypto.DecryptToString(b);

            var res = raw.Select(p => new {
                p.Id,
                p.MedicalRecordNumber,
                p.CreatedAt,
                FirstName = Dec(p.FirstNameEnc),
                LastName = Dec(p.LastNameEnc)
            });

            return Ok(res);
        }


        [HttpGet("{id:guid}"), Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> Get(Guid id)
        {
            var p = await db.Patients.FindAsync(id); if (p is null) return NotFound();
            var (role, deptId) = CurrentRole(); if (string.Equals(role, "Doctor", StringComparison.OrdinalIgnoreCase) && p.DepartmentId != deptId) return Forbid();
            string Dec(byte[] b) => crypto.DecryptToString(b);
            return Ok(new PatientViewDto(p.Id, p.MedicalRecordNumber, Dec(p.FirstNameEnc), Dec(p.LastNameEnc), p.CreatedAt, p.DepartmentId));
        }

        //[HttpPut("{id:guid}")]
        //[Authorize(Roles = "Admin")]
        //public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePatientDto dto)
        //{
        //    var p = await _db.Patients.FirstOrDefaultAsync(x => x.Id == id);
        //    if (p is null) return NotFound();

        //    p.FirstName = dto.FirstName?.Trim() ?? p.FirstName;
        //    p.LastName = dto.LastName?.Trim() ?? p.LastName;
        //    p.DepartmentId = dto.DepartmentId;

        //    await _db.SaveChangesAsync();
        //    return NoContent();
        //}

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var p = await db.Patients.FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return NotFound();

            db.Patients.Remove(p);
            await db.SaveChangesAsync();
            return NoContent();
        }
    }
}