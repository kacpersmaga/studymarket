namespace StudyMarket.Api.Domain;

public enum Role { Student, Creator, Admin }

public class User
{
    public const int MinimumAge = 13;     // młodsze dzieci nie mogą samodzielnie zawierać umów ani zakładać kont
    public const int MinimumCreatorAge = 18; // sprzedaż = zarobek i rozliczenia, wymaga pełnej zdolności do czynności prawnych

    public int Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
    public DateOnly BirthDate { get; set; }
    public DateTime TermsAcceptedAt { get; set; }
    public bool IsDeleted { get; set; } // konto usunięte na żądanie (RODO) - dane osobowe zanonimizowane

    public int AgeOn(DateOnly today)
    {
        var age = today.Year - BirthDate.Year;
        return BirthDate > today.AddYears(-age) ? age - 1 : age;
    }
}
