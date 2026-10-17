using System.Net.Http.Json;
using StudyMarket.Api.Domain;
using StudyMarket.Api.Features.Courses;
using StudyMarket.Api.Features.Orders;

namespace StudyMarket.Tests.Integration;

// Wspólne dane i kroki używane w wielu testach.
public static class TestData
{
    public static readonly CourseRequest SampleCourse =
        new("Matura z chemii: stechiometria", "Zadania maturalne krok po kroku", "Chemia", SchoolLevel.SecondarySchool, 4, 49.99m);

    public static readonly LessonRequest SampleLesson = new("Mol i masa molowa", "Notatka do lekcji", "https://youtu.be/abc", 1,
        [new FlashcardDto("1 mol", "6,02 · 10^23 drobin")],
        [new QuizQuestionRequest("Masa molowa H2O?", ["16 g/mol", "18 g/mol"], 1), new QuizQuestionRequest("Ile to 2*3?", ["6", "5"], 0)],
        [new ExerciseDto("Oblicz liczbę moli w 36 g wody.", "36 g / 18 g/mol = 2 mol")]);

    // Tworzy kurs z jednym modułem i jedną lekcją. Zwraca (idKursu, idLekcji).
    public static async Task<(int CourseId, int LessonId)> CreateCourseAsync(HttpClient creator, bool publish = true)
    {
        var course = await (await creator.PostAsJsonAsync("/api/courses", SampleCourse, ApiFactory.Json))
            .Content.ReadFromJsonAsync<CourseSummary>(ApiFactory.Json);
        var module = await (await creator.PostAsJsonAsync($"/api/courses/{course!.Id}/modules", new ModuleRequest("Obliczenia", 1)))
            .Content.ReadFromJsonAsync<IdResponse>();
        var lesson = await (await creator.PostAsJsonAsync($"/api/modules/{module!.Id}/lessons", SampleLesson))
            .Content.ReadFromJsonAsync<IdResponse>();
        if (publish) (await creator.PostAsync($"/api/courses/{course.Id}/publish", null)).EnsureSuccessStatusCode();
        return (course.Id, lesson!.Id);
    }

    public static async Task<OrderDto> OrderAsync(HttpClient buyer, int courseId)
    {
        var response = await buyer.PostAsJsonAsync("/api/orders", new CreateOrderRequest(courseId, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDto>(ApiFactory.Json))!;
    }

    // Zamówienie + potwierdzenie płatności webhookiem Stripe.
    public static async Task<int> PurchaseAsync(ApiFactory api, HttpClient buyer, int courseId)
    {
        var order = await OrderAsync(buyer, courseId);
        (await api.SendStripeEventAsync(order.Id)).EnsureSuccessStatusCode();
        return order.Id;
    }
}
