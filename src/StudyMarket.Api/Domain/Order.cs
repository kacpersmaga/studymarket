namespace StudyMarket.Api.Domain;

public enum OrderStatus { Pending, Paid }

// Jedno zamówienie = jeden kurs. Opłacone zamówienie daje dostęp do treści kursu.
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public decimal Price { get; set; }       // cena z chwili zakupu - późniejsza zmiana ceny kursu jej nie zmienia
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}
