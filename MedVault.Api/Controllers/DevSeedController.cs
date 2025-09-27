
using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;


namespace MedVault.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DevSeedController(MedVaultDbContext db, AuthService auth) : ControllerBase
    {
        [HttpPost("create-initial-users")]
        public async Task<IActionResult> Seed()
        {
            var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            var doctorRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Doctor");
            var radiology = await db.Departments.FirstOrDefaultAsync(d => d.Name == "Radiology");


            if (!db.AppUsers.Any(u => u.UserName == "admin"))
                db.AppUsers.Add(new Models.AppUsers { Id = Guid.NewGuid(), UserName = "admin", PasswordHash = auth.HashPassword("MedVault!2025"), RoleId = adminRole!.Id, IsActive = true });


            if (!db.AppUsers.Any(u => u.UserName == "doctor"))
                db.AppUsers.Add(new Models.AppUsers { Id = Guid.NewGuid(), UserName = "doctor", PasswordHash = auth.HashPassword("MedVault!2025"), RoleId = doctorRole!.Id, DepartmentId = radiology!.Id, IsActive = true });


            await db.SaveChangesAsync();
            return Ok(new { ok = true });
        }
    }
}