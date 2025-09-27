using MedVault.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;


namespace MedVault.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuditController(MedVaultDbContext db) : ControllerBase
    {
        [HttpGet, Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Recent()
        {
            var last50 = await db.AuditLog.OrderByDescending(a => a.At).Take(50).ToListAsync();
            return Ok(last50);
        }
    }
}