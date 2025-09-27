namespace MedVault.Api.Dtos
{
    public record PatientCreateDto(
    string MedicalRecordNumber,
    string FirstName,
    string LastName,
    string JMBG,
    string? Address,
    string? Phone,
    string? Email,
    Guid? DepartmentId
    );


    public record PatientViewDto(
    Guid Id,
    string MedicalRecordNumber,
    string FirstName,
    string LastName,
    DateTime CreatedAt,
    Guid? DepartmentId
    );
}