using MedVault.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedVault.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DepartmentsController(MedVaultDbContext db) : ControllerBase
    {
        [HttpGet, Authorize(Policy = "DoctorOrAdmin")]
        public async Task<IActionResult> List()
            => Ok(await db.Departments
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync());
    }
}
