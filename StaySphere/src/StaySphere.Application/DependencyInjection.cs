using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Admin;
using StaySphere.Application.Ai;
using StaySphere.Application.Booking;
using StaySphere.Application.Catalog;
using StaySphere.Application.Common;
using StaySphere.Application.Engagement;
using StaySphere.Application.Events;
using StaySphere.Application.Identity;
using StaySphere.Application.Payments;
using StaySphere.Application.Reviews;
using StaySphere.Application.Search;
using StaySphere.Application.Support;
using StaySphere.Contracts.Events;

namespace StaySphere.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(includeInternalTypes: true);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ISearchProvider, DatabaseSearchProvider>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IBookingMaintenance, BookingMaintenance>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IMessagingService, MessagingService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISupportService, SupportService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IHostDashboardService, HostDashboardService>();
        services.AddScoped<IFraudService, FraudService>();

        services.AddScoped<AssistantToolbox>();
        services.AddScoped<IAssistantService, AssistantService>();
        services.AddScoped<IAssistantEngine, RuleBasedAssistantEngine>();

        services.AddSingleton<IntegrationEventDispatcher>();
        AddHandlers(services);
        return services;
    }

    private static void AddHandlers(IServiceCollection services)
    {
        var handlerTypes = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
                .Select(i => (Service: i, Implementation: t)));
        foreach (var (service, implementation) in handlerTypes)
            services.AddScoped(service, implementation);
    }

    /// <summary>Marker so other assemblies can reference integration events without a direct Contracts dependency.</summary>
    public static Type IntegrationEventMarker => typeof(IIntegrationEvent);
}
