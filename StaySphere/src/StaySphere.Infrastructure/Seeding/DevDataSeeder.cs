using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Engagement;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.Reviews;
using StaySphere.Domain.Trust;
using StaySphere.Domain.ValueObjects;
using StaySphere.Infrastructure.Persistence;

namespace StaySphere.Infrastructure.Seeding;

/// <summary>
/// Deterministic development data (fixed RNG seed): 120 properties across 24 cities, 25 hosts, 150 guests, past stays
/// with payments, ledger entries and reviews, upcoming bookings, and demo accounts. Contains no real personal data.
/// Development/Test only — never runs in Production.
/// </summary>
public sealed class DevDataSeeder(AppDbContext db, IPasswordHasher hasher, TimeProvider clock, ILogger<DevDataSeeder> logger)
{
    public const string DemoPassword = "Passw0rd!Demo";

    private sealed record City(string Name, string Region, string Country, string Code, string Currency, double Lat, double Lng, int MinPrice, int MaxPrice, string[] Areas);

    private static readonly City[] Cities =
    [
        new("London", "England", "United Kingdom", "GB", "GBP", 51.5074, -0.1278, 90, 380, ["Shoreditch", "Notting Hill", "Camden", "South Bank", "Greenwich"]),
        new("Paris", "Île-de-France", "France", "FR", "EUR", 48.8566, 2.3522, 95, 420, ["Le Marais", "Montmartre", "Saint-Germain", "Canal Saint-Martin"]),
        new("Lisbon", "Lisbon", "Portugal", "PT", "EUR", 38.7223, -9.1393, 60, 240, ["Alfama", "Bairro Alto", "Príncipe Real", "Belém"]),
        new("Barcelona", "Catalonia", "Spain", "ES", "EUR", 41.3874, 2.1686, 70, 300, ["Gràcia", "El Born", "Eixample", "Barceloneta"]),
        new("Rome", "Lazio", "Italy", "IT", "EUR", 41.9028, 12.4964, 75, 320, ["Trastevere", "Monti", "Prati", "Testaccio"]),
        new("Amsterdam", "North Holland", "Netherlands", "NL", "EUR", 52.3676, 4.9041, 110, 380, ["Jordaan", "De Pijp", "Oost", "Centrum"]),
        new("New York", "New York", "United States", "US", "USD", 40.7128, -74.0060, 140, 520, ["Brooklyn", "Harlem", "SoHo", "Upper West Side"]),
        new("Miami", "Florida", "United States", "US", "USD", 25.7617, -80.1918, 120, 480, ["South Beach", "Wynwood", "Coconut Grove"]),
        new("San Francisco", "California", "United States", "US", "USD", 37.7749, -122.4194, 150, 450, ["Mission", "Nob Hill", "Hayes Valley"]),
        new("Goa", "Goa", "India", "IN", "INR", 15.2993, 74.1240, 3500, 18000, ["Anjuna", "Palolem", "Candolim", "Assagao"]),
        new("Mumbai", "Maharashtra", "India", "IN", "INR", 19.0760, 72.8777, 4000, 20000, ["Bandra", "Colaba", "Juhu"]),
        new("Jaipur", "Rajasthan", "India", "IN", "INR", 26.9124, 75.7873, 2500, 14000, ["Old City", "C-Scheme", "Amer"]),
        new("Tokyo", "Tokyo", "Japan", "JP", "JPY", 35.6762, 139.6503, 9000, 45000, ["Shibuya", "Asakusa", "Shinjuku", "Nakameguro"]),
        new("Kyoto", "Kyoto", "Japan", "JP", "JPY", 35.0116, 135.7681, 8000, 40000, ["Gion", "Arashiyama", "Higashiyama"]),
        new("Bali", "Bali", "Indonesia", "ID", "USD", -8.3405, 115.0920, 45, 350, ["Ubud", "Canggu", "Seminyak", "Uluwatu"]),
        new("Bangkok", "Bangkok", "Thailand", "TH", "USD", 13.7563, 100.5018, 35, 200, ["Sukhumvit", "Silom", "Riverside"]),
        new("Sydney", "New South Wales", "Australia", "AU", "AUD", -33.8688, 151.2093, 130, 480, ["Bondi", "Surry Hills", "Manly"]),
        new("Cape Town", "Western Cape", "South Africa", "ZA", "USD", -33.9249, 18.4241, 60, 300, ["Camps Bay", "Gardens", "Sea Point"]),
        new("Reykjavik", "Capital Region", "Iceland", "IS", "EUR", 64.1466, -21.9426, 120, 380, ["Miðborg", "Vesturbær"]),
        new("Banff", "Alberta", "Canada", "CA", "CAD", 51.1784, -115.5708, 160, 520, ["Downtown", "Tunnel Mountain"]),
        new("Queenstown", "Otago", "New Zealand", "NZ", "USD", -45.0312, 168.6626, 120, 450, ["Frankton", "Fernhill", "Arthurs Point"]),
        new("Santorini", "South Aegean", "Greece", "GR", "EUR", 36.3932, 25.4615, 110, 600, ["Oia", "Fira", "Imerovigli"]),
        new("Zermatt", "Valais", "Switzerland", "CH", "EUR", 46.0207, 7.7491, 180, 700, ["Village", "Winkelmatten"]),
        new("Mexico City", "CDMX", "Mexico", "MX", "USD", 19.4326, -99.1332, 45, 220, ["Roma Norte", "Condesa", "Coyoacán"]),
    ];

    private static readonly string[] Adjectives = ["Sunny", "Cozy", "Bright", "Elegant", "Charming", "Modern", "Rustic", "Light-filled", "Serene", "Stylish", "Hidden", "Panoramic"];
    private static readonly string[] FirstNames = ["Alex", "Sam", "Jordan", "Taylor", "Riya", "Kenji", "Lucía", "Noah", "Amara", "Mateo", "Ines", "Arjun", "Chloé", "Kai", "Zara", "Leo", "Maya", "Omar", "Yuki", "Elena", "Theo", "Priya", "Jonas", "Aisha", "Hugo"];
    private static readonly string[] LastInitials = ["A.", "B.", "C.", "D.", "E.", "F.", "G.", "H.", "K.", "L.", "M.", "N.", "P.", "R.", "S.", "T.", "V.", "W."];
    private static readonly string[] ReviewComments =
    [
        "Spotless, beautifully decorated and exactly as pictured. The host's recommendations were spot on.",
        "Great location within walking distance of everything. Bed was super comfortable. Would stay again!",
        "Lovely place with a gorgeous view. Check-in was seamless and communication was quick.",
        "Very good value for the area. A little noise at night but overall a great stay.",
        "Perfect for our family — plenty of space, a well-equipped kitchen and thoughtful touches for the kids.",
        "The photos don't do it justice. Peaceful, bright and immaculately clean.",
        "Host was incredibly helpful and responsive. Neighbourhood felt safe and had great cafés.",
        "Comfortable and well located, though the Wi-Fi was a bit slow for video calls.",
        "One of the best places we've stayed. Every detail was considered.",
        "Good stay overall. The check-in instructions could be clearer, but the host sorted it quickly.",
    ];

    private static readonly (PropertyType Type, RoomType Room, string Noun)[] Kinds =
    [
        (PropertyType.Apartment, RoomType.EntirePlace, "apartment"),
        (PropertyType.Apartment, RoomType.EntirePlace, "loft"),
        (PropertyType.House, RoomType.EntirePlace, "townhouse"),
        (PropertyType.Villa, RoomType.EntirePlace, "villa"),
        (PropertyType.Cabin, RoomType.EntirePlace, "cabin"),
        (PropertyType.Cottage, RoomType.EntirePlace, "cottage"),
        (PropertyType.GuestHouse, RoomType.PrivateRoom, "guest suite"),
        (PropertyType.BedAndBreakfast, RoomType.PrivateRoom, "B&B room"),
        (PropertyType.Hotel, RoomType.HotelRoom, "boutique hotel room"),
        (PropertyType.Hostel, RoomType.SharedRoom, "hostel bunk"),
        (PropertyType.UniqueStay, RoomType.EntirePlace, "treehouse"),
        (PropertyType.FarmStay, RoomType.EntirePlace, "farm stay"),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Seed skipped: database already has data");
            return;
        }

        var rng = new Random(20240601);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var passwordHash = hasher.Hash(DemoPassword);
        logger.LogInformation("Seeding development data…");

        User NewUser(string email, string name, DateTimeOffset at, params string[] roles)
        {
            var user = User.Register(EmailAddress.Create(email).Value, name, passwordHash, at);
            user.ConfirmEmail();
            foreach (var r in roles) user.AddRole(r);
            user.ClearDomainEvents();
            db.Users.Add(user);
            return user;
        }

        var admin = NewUser("admin@example.local", "Avery Admin", now.AddDays(-400), Roles.Admin);
        NewUser("support@example.local", "Sasha Support", now.AddDays(-380), Roles.Support);
        var demoHost = NewUser("host@example.local", "Hana Host", now.AddDays(-365), Roles.Host);
        var demoGuest = NewUser("guest@example.local", "Gabe Guest", now.AddDays(-300));

        var hosts = new List<User> { demoHost };
        for (var i = 1; i < 25; i++)
            hosts.Add(NewUser($"host{i:00}@example.local", $"{FirstNames[i % FirstNames.Length]} {LastInitials[i % LastInitials.Length]}", now.AddDays(-rng.Next(60, 900)), Roles.Host));

        var guests = new List<User> { demoGuest };
        for (var i = 1; i < 150; i++)
            guests.Add(NewUser($"guest{i:000}@example.local", $"{FirstNames[(i * 7) % FirstNames.Length]} {LastInitials[(i * 3) % LastInitials.Length]}", now.AddDays(-rng.Next(1, 700))));

        var amenityCodes = Persistence.Configurations.AmenityCatalog.All.Select(a => a.Code).ToArray();
        var properties = new List<Property>();
        var index = 0;
        foreach (var city in Cities)
        {
            for (var j = 0; j < 5; j++, index++)
            {
                var host = j == 0 && index < 15 ? demoHost : hosts[rng.Next(hosts.Count)];
                var kind = Kinds[rng.Next(Kinds.Length)];
                var area = city.Areas[rng.Next(city.Areas.Length)];
                var p = Property.CreateDraft(host.Id, kind.Type, kind.Room, now.AddDays(-rng.Next(30, 600)));
                var bedrooms = kind.Room == RoomType.EntirePlace ? rng.Next(1, 5) : 1;
                var title = $"{Adjectives[rng.Next(Adjectives.Length)]} {kind.Noun} in {area}";
                p.UpdateBasics(title,
                    $"Welcome to our {kind.Noun} in the heart of {area}, {city.Name}. Thoughtfully designed with comfortable beds, fast Wi-Fi " +
                    $"and plenty of natural light, it's an easy base for exploring {city.Name}. Cafés, markets and public transport are a short walk away. " +
                    "We provide fresh linens, towels and local tips for your stay.", kind.Type, kind.Room);
                p.UpdateRooms(kind.Room == RoomType.SharedRoom ? 1 : Math.Min(16, bedrooms * 2 + rng.Next(0, 2)), bedrooms,
                    bedrooms + rng.Next(0, 2), Math.Max(1, bedrooms - rng.Next(0, 2)));

                var lat = city.Lat + (rng.NextDouble() - 0.5) * 0.08;
                var lng = city.Lng + (rng.NextDouble() - 0.5) * 0.08;
                p.SetLocation(new Address($"{rng.Next(1, 220)} {area} Street", null, city.Name, city.Region, $"{rng.Next(10000, 99999)}", city.Code, city.Country),
                    Coordinates.Create(lat, lng).Value);

                var step = city.MinPrice > 1000 ? 100m : 1m;
                var price = RoundTo((decimal)(city.MinPrice + rng.NextDouble() * (city.MaxPrice - city.MinPrice)) * (1 + bedrooms * 0.15m), step);
                p.UpdatePricing(price, RoundTo(price * 0.25m, step), city.Currency, rng.Next(0, 3) * 10, rng.Next(0, 3) * 5, 15, rng.Next(0, 4) == 0 ? 2 : 1);
                p.UpdateRules(rng.Next(3) == 0, false, false, "No parties or events. Quiet hours 22:00–08:00. Please remove shoes indoors.",
                    new TimeOnly(15, 0), new TimeOnly(11, 0), (CancellationPolicy)rng.Next(3), rng.Next(5) != 0);
                p.SetAmenities(amenityCodes.OrderBy(_ => rng.Next()).Take(rng.Next(7, 16)).Append("wifi").Append("smoke-alarm"));

                for (var k = 0; k < 5; k++)
                {
                    var seed = $"staysphere-{index}-{k}";
                    p.AddImage($"https://picsum.photos/seed/{seed}/1280/853", $"https://picsum.photos/seed/{seed}/480/320", null, 1280, 853);
                }

                if (rng.Next(4) == 0)
                {
                    var seasonStart = new DateOnly(today.Year, 12, 15);
                    p.AddSeasonalPrice(seasonStart, seasonStart.AddDays(21), Math.Round(price * 1.35m, 0));
                }

                if (index % 23 != 0) p.Publish(p.CreatedAt.AddDays(1));
                p.ClearDomainEvents();
                properties.Add(p);
                db.Properties.Add(p);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Users} users and {Properties} properties", hosts.Count + guests.Count + 2, properties.Count);

        // Reservations: past (completed, paid, reviewed) and upcoming (confirmed), non-overlapping per property.
        var reviewsByProperty = new Dictionary<Guid, List<int>>();
        var reservationCount = 0;
        foreach (var p in properties.Where(p => p.Status == PropertyStatus.Published))
        {
            var cursor = today.AddDays(-rng.Next(150, 240));
            var bookings = rng.Next(2, 7);
            for (var b = 0; b < bookings; b++)
            {
                cursor = cursor.AddDays(rng.Next(3, 25));
                var nights = Math.Max(p.MinNights, rng.Next(2, 7));
                var stay = DateRange.Create(cursor, cursor.AddDays(nights)).Value;
                cursor = stay.End;
                if (stay.Start <= today && stay.End > today) continue; // skip in-progress stays to keep states simple

                var guest = b == 0 && reservationCount % 9 == 0 ? demoGuest : guests[rng.Next(guests.Count)];
                if (guest.Id == p.HostId) continue;
                var bookedAt = new DateTimeOffset(stay.Start.AddDays(-rng.Next(10, 60)).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
                var priceResult = PriceCalculator.Calculate(p, stay, null, PricingSettings.Default, 10m, bookedAt);
                if (priceResult.IsFailure) continue;
                var reservation = Reservation.Hold(p, guest.Id, stay, Math.Min(p.MaxGuests, rng.Next(1, 5)), priceResult.Value, null, bookedAt);
                if (reservation.IsFailure) continue;
                var r = reservation.Value;
                r.MarkPaymentPending(bookedAt.AddMinutes(2));
                var payment = Payment.Start(r.Id, guest.Id, r.TotalAmount, r.Currency, "fake", $"seed-{r.Id:N}", "4242", bookedAt.AddMinutes(2));
                if (r.RequiresApproval)
                {
                    payment.MarkAuthorized("pi_seed_" + r.Id.ToString("N"), bookedAt.AddMinutes(3));
                    r.AwaitApproval(bookedAt.AddMinutes(3));
                }

                payment.MarkSucceeded("pi_seed_" + r.Id.ToString("N"), bookedAt.AddMinutes(4));
                r.Confirm(bookedAt.AddMinutes(4));
                db.LedgerEntries.AddRange(StaySphere.Application.Payments.Ledger.ForConfirmation(r, bookedAt.AddMinutes(4)));

                if (stay.End <= today)
                {
                    var completedAt = new DateTimeOffset(stay.End.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
                    r.Complete(completedAt);
                    if (rng.Next(10) < 8)
                    {
                        var overall = rng.Next(10) < 7 ? 5 : rng.Next(3, 5);
                        var review = Review.Submit(r, guest.Id, new ReviewRatings(overall, Clamp(overall + rng.Next(-1, 1)), Clamp(overall),
                            Clamp(overall + rng.Next(-1, 1)), Clamp(overall + rng.Next(-1, 1)), Clamp(overall), Clamp(overall + rng.Next(-1, 1))),
                            ReviewComments[rng.Next(ReviewComments.Length)], completedAt.AddDays(rng.Next(1, 10)));
                        if (review.IsSuccess)
                        {
                            if (rng.Next(3) == 0) review.Value.Respond(p.HostId, p.HostId, "Thank you so much for staying with us — you're welcome back any time!", completedAt.AddDays(12));
                            review.Value.ClearDomainEvents();
                            db.Reviews.Add(review.Value);
                            reviewsByProperty.TryAdd(p.Id, []);
                            reviewsByProperty[p.Id].Add(overall);
                        }
                    }
                }

                r.ClearDomainEvents();
                payment.ClearDomainEvents();
                db.Reservations.Add(r);
                db.Payments.Add(payment);
                reservationCount++;
            }

            if (reviewsByProperty.TryGetValue(p.Id, out var ratings)) p.ApplyReviewStats(ratings.Average(), ratings.Count);
        }

        // Request-to-book demo: the demo host's first listing requires approval and has two pending requests.
        var requestListing = properties.First(p => p.HostId == demoHost.Id && p.Status == PropertyStatus.Published);
        requestListing.UpdateRules(requestListing.PetsAllowed, requestListing.SmokingAllowed, requestListing.EventsAllowed, requestListing.HouseRules,
            requestListing.CheckInTime, requestListing.CheckOutTime, requestListing.CancellationPolicy, instantBook: false);
        foreach (var (offset, guest) in new[] { (200, demoGuest), (215, guests[5]) })
        {
            var stay = DateRange.Create(today.AddDays(offset), today.AddDays(offset + 3)).Value;
            var requestedAt = now.AddHours(-2);
            var price = PriceCalculator.Calculate(requestListing, stay, null, PricingSettings.Default, 10m, requestedAt).Value;
            var r = Reservation.Hold(requestListing, guest.Id, stay, 2, price, null, requestedAt).Value;
            r.MarkPaymentPending(requestedAt.AddMinutes(1));
            var payment = Payment.Start(r.Id, guest.Id, r.TotalAmount, r.Currency, "fake", $"seed-req-{r.Id:N}", "4242", requestedAt.AddMinutes(1));
            payment.MarkAuthorized("pi_seed_req_" + r.Id.ToString("N"), requestedAt.AddMinutes(2));
            r.AwaitApproval(requestedAt.AddMinutes(2));
            r.ClearDomainEvents();
            payment.ClearDomainEvents();
            db.Reservations.Add(r);
            db.Payments.Add(payment);
        }

        requestListing.ClearDomainEvents();

        foreach (var g in guests.Take(40))
            foreach (var p in properties.Where(x => x.Status == PropertyStatus.Published).OrderBy(_ => rng.Next()).Take(rng.Next(0, 5)))
                db.Favorites.Add(new Favorite(g.Id, p.Id, now.AddDays(-rng.Next(1, 90))));

        // Demo host: payout account (other hosts have none, so their earnings accumulate until they add one).
        db.PayoutAccounts.Add(PayoutAccount.Create(demoHost.Id, demoHost.DisplayName, "PT50000201231234567890154", "PT", now.AddDays(-200)).Value);

        db.Coupons.Add(Coupon.Create("WELCOME10", 10, null, null, 0, now.AddDays(-30), now.AddYears(1), 10_000));
        db.Coupons.Add(Coupon.Create("SUMMER25", 25, null, null, 200, now.AddDays(-5), now.AddMonths(4), 500));

        var ticket = SupportTicket.Open(demoGuest.Id, null, "Payments", TicketPriority.Normal, "Question about my receipt",
            "Hi, could you send me an itemised receipt for my last stay? Thanks!", now.AddDays(-2));
        ticket.ClearDomainEvents();
        db.SupportTickets.Add(ticket);
        db.FraudChecks.Add(FraudCheck.Raise("User", guests[17].Id, guests[17].Id, "REPEATED_PAYMENT_FAILURES", 70, RiskDecision.Review,
            "4 failed payments in the last hour (seeded example).", now.AddHours(-3)));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Reservations} reservations. Demo login: guest@example.local / host@example.local / admin@example.local — password {Password}",
            reservationCount, DemoPassword);
        _ = admin;
    }

    private static decimal RoundTo(decimal value, decimal step) => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    private static int Clamp(int v) => Math.Clamp(v, 1, 5);

    public static string Describe() => string.Create(CultureInfo.InvariantCulture, $"{Cities.Length} cities");
}
