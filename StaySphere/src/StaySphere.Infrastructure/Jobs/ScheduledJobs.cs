using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Booking;
using StaySphere.Application.Payments;

namespace StaySphere.Infrastructure.Jobs;

/// <summary>
/// Periodic jobs (hold expiry, payment reconciliation, stay completion, reminders, cleanup). In Azure these run in the
/// Workers container app (or as Container Apps Jobs); every job is idempotent and safe on multiple replicas.
/// </summary>
public sealed class ScheduledJobs(IServiceScopeFactory scopes, ILogger<ScheduledJobs> logger) : BackgroundService
{
    private sealed record Job(string Name, TimeSpan Interval, Func<IServiceProvider, CancellationToken, Task<int>> Run);

    private static readonly Job[] Jobs =
    [
        new("expire-holds", TimeSpan.FromSeconds(30), (sp, ct) => sp.GetRequiredService<IBookingMaintenance>().ExpireHoldsAsync(ct)),
        new("expire-booking-requests", TimeSpan.FromSeconds(60), (sp, ct) => sp.GetRequiredService<IBookingRequestService>().ExpireOverdueAsync(ct)),
        new("host-payouts", TimeSpan.FromHours(1), (sp, ct) => sp.GetRequiredService<IPayoutService>().RunScheduledPayoutsAsync(ct)),
        new("reconcile-payments", TimeSpan.FromSeconds(60), (sp, ct) => sp.GetRequiredService<IPaymentService>().ReconcilePendingAsync(ct)),
        new("complete-stays", TimeSpan.FromMinutes(10), (sp, ct) => sp.GetRequiredService<IBookingMaintenance>().CompleteFinishedStaysAsync(ct)),
        new("checkin-reminders", TimeSpan.FromHours(1), (sp, ct) => sp.GetRequiredService<IBookingMaintenance>().SendCheckInRemindersAsync(ct)),
        new("cleanup", TimeSpan.FromHours(6), (sp, ct) => sp.GetRequiredService<IBookingMaintenance>().CleanupAsync(ct)),
    ];

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Jobs.Select(job => RunLoopAsync(job, stoppingToken)));

    private async Task RunLoopAsync(Job job, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, CancellationToken.None);
        using var timer = new PeriodicTimer(job.Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await job.Run(scope.ServiceProvider, ct);
                if (count > 0) logger.LogInformation("Job {Job} processed {Count} item(s)", job.Name, count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Job {Job} failed", job.Name);
            }
        }
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct).AsTask().ContinueWith(t => !t.IsCanceled && t.Result, CancellationToken.None));
    }
}
