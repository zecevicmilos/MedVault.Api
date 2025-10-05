public sealed class UpdatePatientDto
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public Guid? DepartmentId { get; set; }
}