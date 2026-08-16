namespace StudyMarket.Api.Domain;

// Postęp użytkownika w jednej lekcji. Klucz złożony (UserId, LessonId).
public class LessonProgress
{
    public int UserId { get; set; }
    public int LessonId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? BestQuizScore { get; set; } // w procentach
}
