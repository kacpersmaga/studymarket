namespace StudyMarket.Api.Domain;

// Komentarz pod lekcją - pytania kupujących i odpowiedzi autora.
public class Comment
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Opinia o kursie. Jedna na kupującego (klucz złożony CourseId + UserId).
public class Review
{
    public int CourseId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int Rating { get; set; } // 1-5
    public string? Text { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
