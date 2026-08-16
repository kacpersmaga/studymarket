namespace StudyMarket.Api.Domain;

public enum Role { Student, Creator, Admin }

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
}
