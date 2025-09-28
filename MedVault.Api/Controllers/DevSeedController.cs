
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
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                // Ensure roles
                var adminRole = await db.Roles.SingleOrDefaultAsync(r => r.Name == "Admin");
                if (adminRole == null)
                {
                    adminRole = new Roles { Id = Guid.NewGuid(), Name = "Admin", Description = "Admins with full access" };
                    db.Roles.Add(adminRole);
                }

                var doctorRole = await db.Roles.SingleOrDefaultAsync(r => r.Name == "Doctor");
                if (doctorRole == null)
                {
                    doctorRole = new Roles { Id = Guid.NewGuid(), Name = "Doctor", Description = "Doctors scoped by department" };
                    db.Roles.Add(doctorRole);
                }

                // Ensure a department
                var radiology = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Radiology");
                if (radiology == null)
                {
                    radiology = new Departments { Id = Guid.NewGuid(), Name = "Radiology" };
                    db.Departments.Add(radiology);
                }

                await db.SaveChangesAsync();

                // Users
                if (!await db.AppUsers.AnyAsync(u => u.UserName == "admin"))
                {
                    db.AppUsers.Add(new AppUsers
                    {
                        Id = Guid.NewGuid(),
                        UserName = "admin",
                        PasswordHash = auth.HashPassword("MedVault!2025"),
                        RoleId = adminRole.Id,
                        IsActive = true
                    });
                }

                if (!await db.AppUsers.AnyAsync(u => u.UserName == "doctor"))
                {
                    db.AppUsers.Add(new AppUsers
                    {
                        Id = Guid.NewGuid(),
                        UserName = "doctor",
                        PasswordHash = auth.HashPassword("MedVault!2025"),
                        RoleId = doctorRole.Id,
                        DepartmentId = radiology.Id,
                        IsActive = true
                    });
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                // Return full problem details so you can see the exact reason in Postman
                return Problem(ex.ToString());
            }
        }
    }
}