using Microsoft.EntityFrameworkCore;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Flashcard> Flashcards => Set<Flashcard>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>(); // czytelne wartości w bazie ("Paid" zamiast 1)
        builder.Properties<decimal>().HavePrecision(10, 2);  // kwoty w złotych z groszami
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        builder.Entity<LessonProgress>().HasKey(p => new { p.UserId, p.LessonId });

        // Kursu, który ktoś kupił, nie wolno usunąć razem z historią zamówień.
        builder.Entity<Order>().HasOne(o => o.Course).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Order>().HasOne<User>().WithMany().HasForeignKey(o => o.UserId);
        builder.Entity<LessonProgress>().HasOne<User>().WithMany().HasForeignKey(p => p.UserId);
        builder.Entity<LessonProgress>().HasOne<Lesson>().WithMany().HasForeignKey(p => p.LessonId);
    }
}
