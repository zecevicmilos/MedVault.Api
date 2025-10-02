
using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;


var b = WebApplication.CreateBuilder(args);



b.Services.AddScoped<CryptoEnvelopeService>();
b.Services.AddScoped<AuthService>();
b.Services.AddScoped<SearchIndexService>();
b.Services.AddHttpContextAccessor();
b.Services.AddDbContext<MedVaultDbContext>(o => o.UseSqlServer(b.Configuration.GetConnectionString("DefaultConnection")));

b.Services.AddControllers();
// logging providers
b.Logging.ClearProviders();
b.Logging.AddConsole();
b.Logging.AddDebug();

var key = b.Configuration["Jwt:Key"]!;
b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(o =>
{
    o.RequireHttpsMetadata = false;       // dev only
    o.SaveToken = true;
    o.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,

        ValidIssuer = b.Configuration["Jwt:Issuer"],
        ValidAudience = b.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.NameIdentifier
    };
  
});

b.Services.AddAuthorization(o => {
    o.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    o.AddPolicy("DoctorOrAdmin", p => p.RequireRole("Doctor", "Admin"));
});


b.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));


var app = b.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();



app.Run();