using MedVault.Api.Dtos;
using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace MedVault.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController(MedVaultDbContext db, AuthService auth, IConfiguration cfg) : ControllerBase
    {
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var u = await db.AppUsers.Include(x => x.Role).FirstOrDefaultAsync(x => x.UserName == dto.UserName && x.IsActive);
            if (u is null) return Unauthorized();
            if (!auth.VerifyPassword(dto.Password, u.PasswordHash)) return Unauthorized();


            var claims = new List<Claim>
                         {
                         new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
                         new(ClaimTypes.NameIdentifier, u.Id.ToString()),
                         new(ClaimTypes.Name, u.UserName),
                         new(ClaimTypes.Role, u.Role?.Name ?? "")
                         };
            if (u.DepartmentId.HasValue)
                claims.Add(new Claim("dept", u.DepartmentId.Value.ToString()));


            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(cfg["Jwt:Issuer"], cfg["Jwt:Audience"], claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(cfg["Jwt:ExpireMinutes"]!)), signingCredentials: creds);
            return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
        }
    }
}
