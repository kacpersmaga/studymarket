using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Data;

// Przykładowe konta i kursy do pracy nad frontendem i do prezentacji na obronie.
// Ładowane tylko w środowisku Development, gdy SeedDemoData = true, i tylko do pustej bazy.
// Hasło wszystkich kont: Demo123!
public static class DemoData
{
    public const string Password = "Demo123!";

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher<User> hasher)
    {
        if (await db.Users.AnyAsync()) return;

        User NewUser(string email, string name, Role role, int birthYear)
        {
            var user = new User { Email = email, DisplayName = name, Role = role, BirthDate = new DateOnly(birthYear, 5, 1), TermsAcceptedAt = DateTime.UtcNow };
            user.PasswordHash = hasher.HashPassword(user, Password);
            return user;
        }

        var admin = NewUser("admin@studymarket.dev", "Administrator", Role.Admin, 1995);
        var creator = NewUser("tworca@studymarket.dev", "Anna Chemiczka", Role.Creator, 2002);
        var student = NewUser("uczen@studymarket.dev", "Kuba Maturzysta", Role.Student, 2008);

        var chemistry = new Course
        {
            Author = creator, Title = "Stechiometria na maturę rozszerzoną", Subject = "Chemia",
            Description = "Mol, masa molowa i obliczenia stechiometryczne krok po kroku, na zadaniach z arkuszy CKE.",
            Level = SchoolLevel.SecondarySchool, Grade = 3, Price = 19.99m, IsPublished = true,
            Modules =
            [
                new Module
                {
                    Title = "Podstawy", Position = 1,
                    Lessons =
                    [
                        new Lesson
                        {
                            Title = "Mol i masa molowa", Position = 1, VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                            Content = "## Mol\nMol to liczba drobin równa liczbie Avogadra: **6,02 · 10²³**.\n\nMasę molową odczytujemy z układu okresowego.",
                            Flashcards = [new Flashcard { Front = "Liczba Avogadra", Back = "6,02 · 10²³ mol⁻¹" }, new Flashcard { Front = "M(H₂O)", Back = "18 g/mol" }],
                            QuizQuestions =
                            [
                                new QuizQuestion { Text = "Ile wynosi masa molowa CO₂?", Options = ["28 g/mol", "44 g/mol", "32 g/mol"], CorrectOption = 1 },
                                new QuizQuestion { Text = "Ile moli to 36 g wody?", Options = ["1", "2", "3"], CorrectOption = 1 },
                            ],
                            Exercises = [new Exercise { Text = "Oblicz liczbę cząsteczek w 9 g wody.", Solution = "9 g / 18 g/mol = 0,5 mol; 0,5 · 6,02 · 10²³ = 3,01 · 10²³" }],
                        },
                    ],
                },
                new Module
                {
                    Title = "Obliczenia z równań reakcji", Position = 2,
                    Lessons =
                    [
                        new Lesson
                        {
                            Title = "Proporcje w równaniach", Position = 1,
                            Content = "Współczynniki w równaniu reakcji to **stosunki molowe** reagentów.",
                            Exercises = [new Exercise { Text = "Ile gramów tlenu potrzeba do spalenia 4 g wodoru?", Solution = "2H₂ + O₂ → 2H₂O; 2 mol H₂ : 1 mol O₂; 4 g H₂ = 2 mol → 1 mol O₂ = 32 g" }],
                        },
                    ],
                },
            ],
        };

        var fractions = new Course
        {
            Author = creator, Title = "Ułamki zwykłe – klasa 5", Subject = "Matematyka",
            Description = "Dodawanie i odejmowanie ułamków o różnych mianownikach.",
            Level = SchoolLevel.PrimarySchool, Grade = 5, Price = 4.99m, IsPublished = true,
            Modules = [new Module { Title = "Ułamki", Position = 1, Lessons = [new Lesson { Title = "Wspólny mianownik", Position = 1, Content = "1/2 + 1/3 = 3/6 + 2/6 = 5/6" }] }],
        };

        var draft = new Course
        {
            Author = creator, Title = "Równowaga chemiczna (szkic)", Subject = "Chemia",
            Level = SchoolLevel.SecondarySchool, Grade = 3, Price = 24.99m,
        };

        db.AddRange(admin, student, chemistry, fractions, draft);
        await db.SaveChangesAsync(); // identyfikatory potrzebne do zamówienia i opinii

        // Uczeń kupił kurs chemii i go ocenił.
        db.Orders.Add(new Order
        {
            UserId = student.Id, CourseId = chemistry.Id, Price = chemistry.Price, PlatformFee = Order.CalculateFee(chemistry.Price, 15),
            Status = OrderStatus.Paid, PaidAt = DateTime.UtcNow, ImmediateAccessConsentAt = DateTime.UtcNow,
        });
        db.Reviews.Add(new Review { CourseId = chemistry.Id, UserId = student.Id, Rating = 5, Text = "Wreszcie zrozumiałem mole!" });
        await db.SaveChangesAsync();
    }
}
