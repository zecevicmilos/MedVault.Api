using MedVault.Api.Models;
using MedVault.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

[ApiController]
[Route("api/[controller]")]
public class DevSeedController : ControllerBase
{
    private readonly MedVaultDbContext db;
    private readonly AuthService auth;
    private readonly CryptoEnvelopeService crypto;
    private readonly SearchIndexService idx;

    public DevSeedController(MedVaultDbContext db, AuthService auth, CryptoEnvelopeService crypto, SearchIndexService idx)
    {
        this.db = db;
        this.auth = auth;
        this.crypto = crypto;
        this.idx = idx;
    }

    [HttpPost("create-initial-users")]
    public async Task<IActionResult> Seed()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // 1. Osiguraj da postoje osnovne uloge
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

            // 2. Dodaj odeljenja (Radiologija, Kardiologija, Pedijatrija) ako ne postoje
            var radiologyDept = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Radiologija");
            if (radiologyDept == null)
            {
                radiologyDept = new Departments { Id = Guid.NewGuid(), Name = "Radiologija" };
                db.Departments.Add(radiologyDept);
            }
            var cardioDept = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Kardiologija");
            if (cardioDept == null)
            {
                cardioDept = new Departments { Id = Guid.NewGuid(), Name = "Kardiologija" };
                db.Departments.Add(cardioDept);
            }
            var pediaDept = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Pedijatrija");
            if (pediaDept == null)
            {
                pediaDept = new Departments { Id = Guid.NewGuid(), Name = "Pedijatrija" };
                db.Departments.Add(pediaDept);
            }
            var ortoDept = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Ortopedija");
            if (ortoDept == null)
            {
                ortoDept = new Departments { Id = Guid.NewGuid(), Name = "Ortopedija" };
                db.Departments.Add(ortoDept);
            }
            var gastroDept = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Gastroenterologija");
            if (gastroDept == null)
            {
                gastroDept = new Departments { Id = Guid.NewGuid(), Name = "Gastroenterologija" };
                db.Departments.Add(gastroDept);
            }
           var neuroDept = await db.Departments.SingleOrDefaultAsync(d => d.Name == "Neurologija");
            if (neuroDept == null)
            {
                neuroDept = new Departments { Id = Guid.NewGuid(), Name = "Neurologija" };
                db.Departments.Add(neuroDept);
            }
            await db.SaveChangesAsync(); // Sačuvaj kreirane uloge i odeljenja

            //// 3. Kreiraj inicijalnog admin korisnika ako ne postoji
            //if (!await db.AppUsers.AnyAsync(u => u.UserName == "admin"))
            //{
            //    db.AppUsers.Add(new AppUsers
            //    {
            //        Id = Guid.NewGuid(),
            //        UserName = "admin",
            //        PasswordHash = auth.HashPassword("MedVault!2025"),
            //        RoleId = adminRole!.Id,
            //        IsActive = true
            //    });
            //}

            // 4. Kreiraj 3 doktora (naloge) povezane sa odeljenjima
            if (!await db.AppUsers.AnyAsync(u => u.UserName == "ppetrovic"))
            {
                db.AppUsers.Add(new AppUsers
                {
                    Id = Guid.NewGuid(),
                    UserName = "ppetrovic",
                    PasswordHash = auth.HashPassword("MedVault!2025"),
                    RoleId = doctorRole!.Id,
                    DepartmentId = ortoDept.Id,
                    FullNameEnc = crypto.EncryptString("Dr Petar Petrović", out _, out _),
                    IsActive = true
                });
            }
            if (!await db.AppUsers.AnyAsync(u => u.UserName == "jjovanovic"))
            {
                db.AppUsers.Add(new AppUsers
                {
                    Id = Guid.NewGuid(),
                    UserName = "jjovanovic",
                    PasswordHash = auth.HashPassword("MedVault!2025"),
                    RoleId = doctorRole.Id,
                    DepartmentId = cardioDept.Id,
                    FullNameEnc = crypto.EncryptString("Dr Jelena Jovanović", out _, out _),
                    IsActive = true
                });
            }
            if (!await db.AppUsers.AnyAsync(u => u.UserName == "mmarkovic"))
            {
                db.AppUsers.Add(new AppUsers
                {
                    Id = Guid.NewGuid(),
                    UserName = "mmarkovic",
                    PasswordHash = auth.HashPassword("MedVault!2025"),
                    RoleId = doctorRole.Id,
                    DepartmentId = neuroDept.Id,
                    FullNameEnc = crypto.EncryptString("Dr Marko Marković", out _, out _),
                    IsActive = true
                });
            }

            await db.SaveChangesAsync(); // Sačuvaj kreirane naloge

            // Pomoćne funkcije za enkripciju i izdvajanje cifara
            byte[] Enc(string s) => crypto.EncryptString(s, out _, out _);
            string OnlyDigits(string s) => new string(s.Where(char.IsDigit).ToArray());

            // 5. Kreiraj pacijente sa validnim podacima (ime, prezime, JMBG, adresa, telefon)
            var patients = new List<Patients>();

            // Radiologija – 3 pacijenta
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00001",
                FirstNameEnc = Enc("Ana"),
                LastNameEnc = Enc("Nikolić"),
                JMBGEnc = Enc("1503990715012"),
                AddressEnc = Enc("Bulevar Kralja Aleksandra 73, Beograd"),
                PhoneEnc = Enc("0601234567"),
                LastNameHmac = idx.HmacIndex("Nikolić".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("1503990715012")),
                DepartmentId = radiologyDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00002",
                FirstNameEnc = Enc("Marko"),
                LastNameEnc = Enc("Ilić"),
                JMBGEnc = Enc("0712985850020"),
                AddressEnc = Enc("Glavna 5, Zrenjanin"),
                PhoneEnc = Enc("0611234567"),
                LastNameHmac = idx.HmacIndex("Ilić".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("0712985850020")),
                DepartmentId = ortoDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00003",
                FirstNameEnc = Enc("Ivana"),
                LastNameEnc = Enc("Đorđević"),
                JMBGEnc = Enc("3006995745100"),
                AddressEnc = Enc("Narodnih Heroja 12, Leskovac"),
                PhoneEnc = Enc("0621234567"),
                LastNameHmac = idx.HmacIndex("Đorđević".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("3006995745100")),
                DepartmentId = neuroDept.Id
            });

            // Kardiologija – 4 pacijenta
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00004",
                FirstNameEnc = Enc("Milan"),
                LastNameEnc = Enc("Petrović"),
                JMBGEnc = Enc("0101980710034"),
                AddressEnc = Enc("Nemanjina 4, Beograd"),
                PhoneEnc = Enc("0631234567"),
                LastNameHmac = idx.HmacIndex("Petrović".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("0101980710034")),
                DepartmentId = cardioDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00005",
                FirstNameEnc = Enc("Marija"),
                LastNameEnc = Enc("Lukić"),
                JMBGEnc = Enc("2008992805038"),
                AddressEnc = Enc("Jevrejska 13, Novi Sad"),
                PhoneEnc = Enc("0641234567"),
                LastNameHmac = idx.HmacIndex("Lukić".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("2008992805038")),
                DepartmentId = cardioDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00006",
                FirstNameEnc = Enc("Dragana"),
                LastNameEnc = Enc("Marković"),
                JMBGEnc = Enc("0909000775671"),
                AddressEnc = Enc("Vuka Karadžića 8, Valjevo"),
                PhoneEnc = Enc("0651234567"),
                LastNameHmac = idx.HmacIndex("Marković".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("0909000775671")),
                DepartmentId = cardioDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00007",
                FirstNameEnc = Enc("Nikola"),
                LastNameEnc = Enc("Stojanović"),
                JMBGEnc = Enc("2112988860107"),
                AddressEnc = Enc("Vojvode Živojina Mišića 15, Pančevo"),
                PhoneEnc = Enc("0661234567"),
                LastNameHmac = idx.HmacIndex("Stojanović".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("2112988860107")),
                DepartmentId = ortoDept.Id
            });

            // Pedijatrija – 3 pacijenta
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00008",
                FirstNameEnc = Enc("Jovana"),
                LastNameEnc = Enc("Petrović"),
                JMBGEnc = Enc("0505014797003"),
                AddressEnc = Enc("Trg Partizana 1, Užice"),
                PhoneEnc = Enc("0671234567"),
                LastNameHmac = idx.HmacIndex("Petrović".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("0505014797003")),
                DepartmentId = pediaDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00009",
                FirstNameEnc = Enc("Stefan"),
                LastNameEnc = Enc("Nikolić"),
                JMBGEnc = Enc("1010008720214"),
                AddressEnc = Enc("Kralja Petra I 45, Kragujevac"),
                PhoneEnc = Enc("0681234567"),
                LastNameHmac = idx.HmacIndex("Nikolić".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("1010008720214")),
                DepartmentId = pediaDept.Id
            });
            patients.Add(new Patients
            {
                Id = Guid.NewGuid(),
                MedicalRecordNumber = "00010",
                FirstNameEnc = Enc("Petra"),
                LastNameEnc = Enc("Kovačević"),
                JMBGEnc = Enc("0101016896005"),
                AddressEnc = Enc("Svetog Save 10, Sremska Mitrovica"),
                PhoneEnc = Enc("0691234567"),
                LastNameHmac = idx.HmacIndex("Kovačević".ToLowerInvariant()),
                JmbgHmac = idx.HmacIndex(OnlyDigits("0101016896005")),
                DepartmentId = pediaDept.Id
            });

            db.Patients.AddRange(patients);
            await db.SaveChangesAsync(); // Sačuvaj sve dodate pacijente

            // 6. Kreiraj po 4 pregleda za svakog pacijenta (2 zakazana u budućnosti, 2 već obavljena sa beleškama)
            var encounters = new List<Encounters>();
            DateTime now = DateTime.Now;
            foreach (var p in patients)
            {
                // Dva datuma u prošlosti (npr. pre ~1 godine i pre ~3 meseca)
                var pastDate1 = now.AddYears(-1);
                var pastDate2 = now.AddMonths(-3);
                // Dva datuma u budućnosti (npr. za ~3 meseca i za ~1 godinu od sada)
                var futureDate1 = now.AddMonths(3);
                var futureDate2 = now.AddYears(1);

                // Beleške za obavljene preglede (šifrovane)
                byte[] notes1 = crypto.EncryptString("Pacijent pregledan, stanje stabilno.", out _, out _);
                byte[] notes2 = crypto.EncryptString("Obavljen kontrolni pregled, preporučena terapija.", out _, out _);

                encounters.Add(new Encounters
                {
                    Id = Guid.NewGuid(),
                    PatientId = p.Id,
                    EncounterDate = pastDate1,
                    NotesEnc = notes1,   // obavljeni pregled sa beleškom
                    DepartmentId = p.DepartmentId,
                    ClinicianId = null   // (opciono: može se dodeliti ID doktora koji je vodio pregled)
                });
                encounters.Add(new Encounters
                {
                    Id = Guid.NewGuid(),
                    PatientId = p.Id,
                    EncounterDate = pastDate2,
                    NotesEnc = notes2,   // obavljeni pregled sa beleškom
                    DepartmentId = p.DepartmentId,
                    ClinicianId = null
                });
              
                encounters.Add(new Encounters
                {
                    Id = Guid.NewGuid(),
                    PatientId = p.Id,
                    EncounterDate = futureDate2,
                    NotesEnc = null,     // zakazan pregled
                    DepartmentId = p.DepartmentId,
                    ClinicianId = null
                });
            }

            db.Encounters.AddRange(encounters);
            await db.SaveChangesAsync(); // Sačuvaj sve dodate preglede

            await tx.CommitAsync();
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            // Vraćamo detalje greške radi dijagnostike (samo za razvojnu fazu)
            return Problem(ex.ToString());
        }
    }
}
