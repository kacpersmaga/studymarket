using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using StudyMarket.Api.Data;
using StudyMarket.Api.Domain;

namespace StudyMarket.Api.Features.Orders;

public record CheckoutResponse(string PaymentUrl);

// Płatności przez Stripe Checkout: strona płatności jest hostowana przez operatora, więc platforma
// nigdy nie dotyka danych karty ani kodu BLIK (mniejsze wymagania bezpieczeństwa, PCI DSS po stronie operatora).
//
// Przepływ:
// 1. Frontend wywołuje /checkout i przekierowuje użytkownika na otrzymany adres.
// 2. Użytkownik płaci (BLIK, Przelewy24, karta) na stronie Stripe.
// 3. Stripe wywołuje webhook; dopiero potwierdzenie z webhooka (z poprawnym podpisem) oznacza zamówienie jako opłacone.
//    Powrót przeglądarki na SuccessUrl niczego nie potwierdza - użytkownik mógłby go wywołać ręcznie.
public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/orders/{id:int}/checkout", Checkout).WithTags("Orders").RequireAuthorization();
        app.MapPost("/api/payments/stripe-webhook", StripeWebhook).WithTags("Orders").ExcludeFromDescription();
    }

    static async Task<Results<Ok<CheckoutResponse>, NotFound, ProblemHttpResult>> Checkout(
        int id, AppDbContext db, ClaimsPrincipal user, IConfiguration config)
    {
        var order = await db.Orders.Include(o => o.Course).SingleOrDefaultAsync(o => o.Id == id && o.UserId == user.GetUserId());
        if (order is null) return TypedResults.NotFound();
        if (order.Status == OrderStatus.Paid)
            return TypedResults.Problem("Zamówienie jest już opłacone.", statusCode: 409);

        var session = await new SessionService(new StripeClient(config["Stripe:SecretKey"])).CreateAsync(new SessionCreateOptions
        {
            // Metody płatności (BLIK, Przelewy24, karta) włącza się w panelu Stripe - Checkout pokazuje te dostępne dla PLN.
            Mode = "payment",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "pln",
                        UnitAmount = (long)(order.Price * 100), // Stripe przyjmuje kwoty w groszach
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = order.Course.Title },
                    },
                },
            ],
            Metadata = new Dictionary<string, string> { ["orderId"] = order.Id.ToString() },
            SuccessUrl = config["Stripe:SuccessUrl"],
            CancelUrl = config["Stripe:CancelUrl"],
        });

        return TypedResults.Ok(new CheckoutResponse(session.Url));
    }

    static async Task<Results<Ok, BadRequest>> StripeWebhook(HttpRequest request, AppDbContext db, IConfiguration config)
    {
        var json = await new StreamReader(request.Body).ReadToEndAsync();
        Event stripeEvent;
        try
        {
            // Weryfikacja podpisu HMAC-SHA256 - odrzuca żądania, które nie pochodzą od Stripe.
            stripeEvent = EventUtility.ConstructEvent(json, request.Headers["Stripe-Signature"], config["Stripe:WebhookSecret"],
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            return TypedResults.BadRequest();
        }

        // BLIK i Przelewy24 mogą potwierdzić płatność z opóźnieniem - wtedy przychodzi drugie zdarzenie.
        var isPaymentConfirmation = stripeEvent.Type is EventTypes.CheckoutSessionCompleted or EventTypes.CheckoutSessionAsyncPaymentSucceeded;
        if (isPaymentConfirmation && stripeEvent.Data.Object is Session { PaymentStatus: "paid" } session)
        {
            var order = await db.Orders.FindAsync(int.Parse(session.Metadata["orderId"]));
            // Stripe może wysłać to samo zdarzenie kilka razy - ponowne przetworzenie niczego nie zmienia (idempotencja).
            if (order is { Status: OrderStatus.Pending })
            {
                order.Status = OrderStatus.Paid;
                order.PaidAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        return TypedResults.Ok(); // potwierdzamy odbiór każdego poprawnie podpisanego zdarzenia, inaczej Stripe ponawia
    }
}
