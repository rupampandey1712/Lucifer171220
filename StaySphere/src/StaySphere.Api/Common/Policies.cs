using StaySphere.Domain.Identity;

namespace StaySphere.Api.Common;

public static class Policies
{
    public const string CanCreateProperty = nameof(CanCreateProperty);
    public const string CanManageProperty = nameof(CanManageProperty);
    public const string CanManageReservation = nameof(CanManageReservation);
    public const string CanViewAdminDashboard = nameof(CanViewAdminDashboard);
    public const string CanProcessRefund = nameof(CanProcessRefund);
    public const string CanModerateReview = nameof(CanModerateReview);
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanHandleSupport = nameof(CanHandleSupport);
    public const string CanViewAudit = nameof(CanViewAudit);

    public static void Register(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        // Role-level gates here; per-resource ownership (host owns property, guest owns reservation) is enforced in
        // the Application layer on the loaded resource — the backend is the only enforcement point.
        options.AddPolicy(CanCreateProperty, p => p.RequireRole(Roles.Host, Roles.Admin));
        options.AddPolicy(CanManageProperty, p => p.RequireRole(Roles.Host, Roles.Admin));
        options.AddPolicy(CanManageReservation, p => p.RequireAuthenticatedUser());
        options.AddPolicy(CanViewAdminDashboard, p => p.RequireRole(Roles.Admin));
        options.AddPolicy(CanProcessRefund, p => p.RequireRole(Roles.Admin));
        options.AddPolicy(CanModerateReview, p => p.RequireRole(Roles.Admin));
        options.AddPolicy(CanManageUsers, p => p.RequireRole(Roles.Admin));
        options.AddPolicy(CanHandleSupport, p => p.RequireRole(Roles.Support, Roles.Admin));
        options.AddPolicy(CanViewAudit, p => p.RequireRole(Roles.Admin));
    }
}

public static class RateLimitPolicies
{
    public const string Anonymous = "anon";
    public const string Auth = "auth";
    public const string AuthStrict = "auth-strict";
    public const string Payment = "payment";
    public const string Ai = "ai";
    public const string Messaging = "messaging";
    public const string Upload = "upload";
}
