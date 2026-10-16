using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Booking;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.Contracts.Reviews;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ReviewReminderTests(ApiFactory api)
{
    private static int _offset = 1500;

    /// <summary>Books and pays a stay, then moves it into the past (checked out <paramref name="daysAgo"/> days ago) and completes it.</summary>
    private async Task<(HttpClient Guest, Guid GuestId, Guid ReservationId)> CompletedStayAsync(int daysAgo)
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guest, guestAuth) = await api.RegisterAsync();
        var ci = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _offset, 10));
        var quote = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, ci.AddDays(3), 2, null))).ReadAsync<QuoteDto>();
        var held = await (await guest.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, ci, ci.AddDays(3), 2, quote.QuoteToken, null),
            Guid.NewGuid().ToString())).ReadAsync<ReservationDto>();
        (await guest.PostJsonAsync($"/api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var checkout = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo);
        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE booking.Reservations SET CheckIn = {checkout.AddDays(-3)}, CheckOut = {checkout} WHERE Id = {held.Id}"));
        await api.WithScopeAsync(sp => sp.GetRequiredService<IBookingMaintenance>().CompleteFinishedStaysAsync(CancellationToken.None));
        return (guest, guestAuth.User.Id, held.Id);
    }

    private Task<List<string>> ReminderTypesAsync(Guid guestId, Guid reservationId) => api.WithScopeAsync(sp =>
        sp.GetRequiredService<AppDbContext>().Notifications
            .Where(n => n.UserId == guestId && n.Link == $"/trips/{reservationId}" && n.Type.StartsWith("review.reminder"))
            .Select(n => n.Type).ToListAsync());

    private Task<int> RunAsync() => api.WithScopeAsync(sp => sp.GetRequiredService<IBookingMaintenance>().SendReviewRemindersAsync(CancellationToken.None));

    [Fact]
    public async Task Guests_get_one_reminder_the_day_after_checkout()
    {
        var (_, guestId, reservationId) = await CompletedStayAsync(daysAgo: 1);
        // The completion event sends the first reminder; the job must not add another.
        await HttpExtensions.EventuallyAsync(() => ReminderTypesAsync(guestId, reservationId), t => t.Count > 0);

        await RunAsync();
        await RunAsync();

        (await ReminderTypesAsync(guestId, reservationId)).ShouldBe(["review.reminder"], "sent once, never duplicated");
    }

    [Fact]
    public async Task A_final_reminder_comes_a_week_before_the_review_window_closes()
    {
        var (_, guestId, reservationId) = await CompletedStayAsync(daysAgo: 23);

        await RunAsync();

        (await ReminderTypesAsync(guestId, reservationId)).ShouldContain("review.reminder.final");
    }

    [Fact]
    public async Task Stays_that_were_already_reviewed_get_no_final_reminder()
    {
        var (guest, guestId, reservationId) = await CompletedStayAsync(daysAgo: 23);
        (await guest.PostJsonAsync("/api/v1/reviews", new CreateReviewRequest(reservationId, 5, 5, 5, 5, 5, 5, 5, "Lovely flat, great host.")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await RunAsync();

        (await ReminderTypesAsync(guestId, reservationId)).ShouldNotContain("review.reminder.final");
    }
}
