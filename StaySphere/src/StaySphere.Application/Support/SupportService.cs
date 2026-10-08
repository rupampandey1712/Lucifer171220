using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Support;
using StaySphere.Domain.Common;
using StaySphere.Domain.Trust;

namespace StaySphere.Application.Support;

public interface ISupportService
{
    Task<Result<TicketDto>> CreateAsync(CreateTicketRequest request, CancellationToken ct);
    Task<PagedResult<TicketDto>> ListAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<Result<TicketDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<TicketDto>> AddMessageAsync(Guid id, string body, CancellationToken ct);
    Task<Result<TicketDto>> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct);
    Task<Result> FileReportAsync(CreateReportRequest request, CancellationToken ct);
}

public sealed class SupportService(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock) : ISupportService
{
    private static readonly Error NotFound = Error.NotFound("ticket.not_found", "Ticket not found.");

    public async Task<Result<TicketDto>> CreateAsync(CreateTicketRequest r, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (!Enum.TryParse<TicketPriority>(r.Priority, true, out var priority)) priority = TicketPriority.Normal;
        if (r.ReservationId is { } rid && !await db.Reservations.AnyAsync(x => x.Id == rid && (x.GuestId == userId || x.HostId == userId), ct))
            return Error.NotFound("reservation.not_found", "Reservation not found.");
        var ticket = SupportTicket.Open(userId, r.ReservationId, r.Category, priority, r.Subject, r.Description, clock.GetUtcNow());
        db.SupportTickets.Add(ticket);
        audit.Record("support.ticket_opened", nameof(SupportTicket), ticket.Id.ToString());
        await db.SaveChangesAsync(ct);
        return (await GetAsync(ticket.Id, ct)).Value;
    }

    public async Task<PagedResult<TicketDto>> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var userId = currentUser.RequireUserId();
        var q = db.SupportTickets.AsNoTracking();
        if (!currentUser.IsStaff()) q = q.Where(t => t.UserId == userId);
        if (Enum.TryParse<TicketStatus>(status, true, out var s)) q = q.Where(t => t.Status == s);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(t => t.Priority).ThenByDescending(t => t.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => t.Id).ToListAsync(ct);
        var items = new List<TicketDto>();
        foreach (var id in ids) items.Add((await LoadAsync(id, ct))!);
        return new PagedResult<TicketDto>(items, page, pageSize, total);
    }

    public async Task<Result<TicketDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await LoadAsync(id, ct);
        if (dto is null || (dto.UserId != currentUser.UserId && !currentUser.IsStaff())) return NotFound;
        return dto;
    }

    public async Task<Result<TicketDto>> AddMessageAsync(Guid id, string body, CancellationToken ct)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        var userId = currentUser.RequireUserId();
        if (ticket is null || (ticket.UserId != userId && !currentUser.IsStaff())) return NotFound;
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) return Error.Validation("ticket.message", "Message must be 1–4000 characters.");
        ticket.AddMessage(userId, currentUser.IsStaff() && ticket.UserId != userId, body, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return (await LoadAsync(id, ct))!;
    }

    public async Task<Result<TicketDto>> UpdateAsync(Guid id, UpdateTicketRequest r, CancellationToken ct)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound;
        if (!Enum.TryParse<TicketStatus>(r.Status, true, out var status) || !Enum.TryParse<TicketPriority>(r.Priority, true, out var priority))
            return Error.Validation("ticket.status", "Invalid status or priority.");
        ticket.Update(status, priority, r.AssigneeId);
        audit.Record("support.ticket_updated", nameof(SupportTicket), id.ToString(), new { r.Status, r.Priority, r.AssigneeId });
        await db.SaveChangesAsync(ct);
        return (await LoadAsync(id, ct))!;
    }

    public async Task<Result> FileReportAsync(CreateReportRequest r, CancellationToken ct)
    {
        var allowed = new[] { "Property", "Review", "User", "Conversation" };
        if (!allowed.Contains(r.TargetType)) return Error.Validation("report.target", "Unknown report target.");
        db.Reports.Add(Report.File(currentUser.RequireUserId(), r.TargetType, r.TargetId, r.Reason, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<TicketDto?> LoadAsync(Guid id, CancellationToken ct)
    {
        var t = await db.SupportTickets.AsNoTracking().Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;
        var userIds = t.Messages.Select(m => m.AuthorId).Append(t.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new TicketDto(t.Id, t.UserId, names.GetValueOrDefault(t.UserId, "User"), t.ReservationId, t.Category, t.Priority.ToString(),
            t.Status.ToString(), t.Subject, t.Description, t.AssigneeId, t.CreatedAt, t.UpdatedAt,
            t.Messages.OrderBy(m => m.At).Select(m => new TicketMessageDto(m.Id, m.AuthorId, names.GetValueOrDefault(m.AuthorId, "User"),
                m.FromAgent, m.Body, m.At)).ToList());
    }
}
