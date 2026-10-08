namespace StudyMarket.Api.Domain;

// Etap edukacji wg polskiego systemu szkolnictwa; klasa (Grade) doprecyzowuje poziom.
public enum SchoolLevel { PrimarySchool, SecondarySchool, University }

// Struktura treści: Kurs -> Moduły -> Lekcje -> (notatka, wideo, załączniki, fiszki, quiz, zadania).
public class Course
{
    public int Id { get; set; }
    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public required string Subject { get; set; }
    public SchoolLevel Level { get; set; }
    public int? Grade { get; set; }
    public decimal Price { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Module> Modules { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
}

public class Module
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public required string Title { get; set; }
    public int Position { get; set; }
    public List<Lesson> Lessons { get; set; } = [];
}

public class Lesson
{
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public Module Module { get; set; } = null!;
    public required string Title { get; set; }
    public string Content { get; set; } = "";  // notatka (Markdown)
    public string? VideoUrl { get; set; }      // link do filmu (np. YouTube/Vimeo) - platforma nie hostuje wideo
    public int Position { get; set; }
    public List<Flashcard> Flashcards { get; set; } = [];
    public List<QuizQuestion> QuizQuestions { get; set; } = [];
    public List<Exercise> Exercises { get; set; } = [];

    // answers: identyfikator pytania -> indeks wybranej odpowiedzi.
    public int CountCorrectAnswers(IReadOnlyDictionary<int, int> answers) =>
        QuizQuestions.Count(q => answers.TryGetValue(q.Id, out var chosen) && chosen == q.CorrectOption);
}

public class Flashcard
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public required string Front { get; set; }
    public required string Back { get; set; }
}

// Zadanie do samodzielnego rozwiązania (np. zadanie maturalne) z rozwiązaniem wzorcowym do samosprawdzenia.
public class Exercise
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public required string Text { get; set; }
    public required string Solution { get; set; }
}

// Plik dołączony do lekcji (PDF, obraz). Zawartość trzymana w bazie - patrz README.
public class Attachment
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
}

public class QuizQuestion
{
    public int Id { get; set; }
    public int LessonId { get; set; }
    public required string Text { get; set; }
    public List<string> Options { get; set; } = [];
    public int CorrectOption { get; set; }
}
