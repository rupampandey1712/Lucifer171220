using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Engagement;
using StaySphere.Contracts.Properties;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Engagement;

namespace StaySphere.Application.Engagement;

public interface IFavoriteService
{
    Task<IReadOnlyList<PropertyCardDto>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListIdsAsync(CancellationToken ct);
    Task<Result> AddAsync(Guid propertyId, CancellationToken ct);
    Task RemoveAsync(Guid propertyId, CancellationToken ct);
}

public sealed class FavoriteService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IFavoriteService
{
    public async Task<IReadOnlyList<PropertyCardDto>> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var rows = await db.Favorites.AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt)
            .Join(db.Properties, f => f.PropertyId, p => p.Id, (f, p) => p)
            .Where(p => p.DeletedAt == null)
            .Select(p => new
            {
                p.Id, p.Title, p.PropertyType, p.RoomType, City = p.Address!.City, Country = p.Address.Country, p.Latitude, p.Longitude,
                p.BasePrice, p.Currency, p.RatingAverage, p.ReviewCount, p.MaxGuests, p.Bedrooms, p.Beds, p.InstantBook,
                Images = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).Take(5).ToList(),
            }).ToListAsync(ct);
        return rows.Select(p => new PropertyCardDto(p.Id, p.Title, p.PropertyType.ToString(), p.RoomType.ToString(), p.City, p.Country,
            p.Latitude, p.Longitude, p.BasePrice, p.Currency, null, p.RatingAverage, p.ReviewCount, p.MaxGuests, p.Bedrooms, p.Beds,
            p.InstantBook, p.Images, null)).ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListIdsAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.Favorites.AsNoTracking().Where(f => f.UserId == userId).Select(f => f.PropertyId).ToListAsync(ct);
    }

    public async Task<Result> AddAsync(Guid propertyId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId && p.Status == PropertyStatus.Published, ct))
            return Error.NotFound("property.not_found", "Listing not found.");
        if (await db.Favorites.AnyAsync(f => f.UserId == userId && f.PropertyId == propertyId, ct)) return Result.Success();
        db.Favorites.Add(new Favorite(userId, propertyId, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            db.ResetTracking(); // Already saved by a concurrent request — PUT semantics are idempotent.
        }

        return Result.Success();
    }

    public async Task RemoveAsync(Guid propertyId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.Favorites.Where(f => f.UserId == userId && f.PropertyId == propertyId).ExecuteDeleteAsync(ct);
    }
}

public interface IMessagingService
{
    Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken ct);
    Task<Result<ConversationDto>> StartAsync(StartConversationRequest request, CancellationToken ct);
    Task<Result<PagedResult<MessageDto>>> GetMessagesAsync(Guid conversationId, DateTimeOffset? before, int pageSize, CancellationToken ct);
    Task<Result<MessageDto>> SendAsync(Guid conversationId, string body, CancellationToken ct);
    /// <summary>Sends an image (re-encoded) or PDF (checked) as a message. Stored privately.</summary>
    Task<Result<MessageDto>> SendAttachmentAsync(Guid conversationId, Stream content, string fileName, string? caption, CancellationToken ct);
    /// <summary>Participants only; anyone else gets 404.</summary>
    Task<Result<AttachmentDownload>> DownloadAttachmentAsync(Guid conversationId, Guid messageId, CancellationToken ct);
    Task<Result> MarkReadAsync(Guid conversationId, CancellationToken ct);
    Task<Result> ReportAsync(Guid conversationId, string reason, CancellationToken ct);
    Task<Result<Guid>> GetOtherParticipantAsync(Guid conversationId, CancellationToken ct);
    Task<int> UnreadCountAsync(CancellationToken ct);
}

public sealed class MessagingService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRealtimeNotifier realtime,
    IFileStorageService storage,
    IImageProcessor images,
    IMalwareScanner malwareScanner,
    TimeProvider clock,
    ILogger<MessagingService> logger) : IMessagingService
{
    private static readonly Error NotFound = Error.NotFound("conversation.not_found", "Conversation not found.");

    public async Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var rows = await db.Conversations.AsNoTracking().Where(c => c.GuestId == me || c.HostId == me)
            .OrderByDescending(c => c.LastMessageAt).Take(100)
            .Select(c => new
            {
                c.Id, c.PropertyId, c.ReservationId, c.LastMessageAt, OtherId = c.GuestId == me ? c.HostId : c.GuestId,
                LastRead = c.GuestId == me ? c.GuestLastReadMessageId : c.HostLastReadMessageId,
                Property = db.Properties.Where(p => p.Id == c.PropertyId)
                    .Select(p => new { p.Title, Image = p.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl).FirstOrDefault() }).First(),
                Last = db.Messages.Where(m => m.ConversationId == c.Id).OrderByDescending(m => m.SentAt)
                    .Select(m => new { m.Id, m.Body, m.SentAt }).FirstOrDefault(),
            }).ToListAsync(ct);

        var otherIds = rows.Select(r => r.OtherId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => otherIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.AvatarUrl }).ToDictionaryAsync(u => u.Id, ct);

        var result = new List<ConversationDto>(rows.Count);
        foreach (var r in rows)
        {
            var lastReadAt = r.LastRead is null ? DateTimeOffset.MinValue
                : await db.Messages.Where(m => m.Id == r.LastRead).Select(m => m.SentAt).FirstOrDefaultAsync(ct);
            var unread = await db.Messages.CountAsync(m => m.ConversationId == r.Id && m.SenderId != me && m.SentAt > lastReadAt, ct);
            var other = users.GetValueOrDefault(r.OtherId);
            result.Add(new ConversationDto(r.Id, r.PropertyId, r.Property.Title, r.Property.Image, r.ReservationId, r.OtherId,
                other?.DisplayName ?? "StaySphere user", other?.AvatarUrl, r.Last?.Body, r.Last?.SentAt ?? r.LastMessageAt, unread));
        }

        return result;
    }

    public async Task<Result<ConversationDto>> StartAsync(StartConversationRequest request, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var property = await db.Properties.AsNoTracking().Where(p => p.Id == request.PropertyId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.HostId }).FirstOrDefaultAsync(ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");

        // Hosts start conversations from a reservation (with that guest); guests from a listing (with its host).
        Guid guestId = me;
        if (me == property.HostId)
        {
            if (request.ReservationId is null) return Error.Validation("conversation.reservation", "Hosts can message guests from a reservation.");
            var guest = await db.Reservations.Where(r => r.Id == request.ReservationId && r.HostId == me).Select(r => (Guid?)r.GuestId).FirstOrDefaultAsync(ct);
            if (guest is null) return Error.NotFound("reservation.not_found", "Reservation not found.");
            guestId = guest.Value;
        }

        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.PropertyId == property.Id && c.GuestId == guestId, ct);
        if (conversation is null)
        {
            var started = Conversation.Start(property.Id, request.ReservationId, guestId, property.HostId, clock.GetUtcNow());
            if (started.IsFailure) return started.Error!;
            conversation = started.Value;
            db.Conversations.Add(conversation);
        }

        var sent = conversation.Send(me, request.Message, clock.GetUtcNow());
        if (sent.IsFailure) return sent.Error!;
        await db.SaveChangesAsync(ct);
        await PushAsync(conversation, sent.Value, ct);
        return (await ListConversationsAsync(ct)).First(c => c.Id == conversation.Id);
    }

    public async Task<Result<PagedResult<MessageDto>>> GetMessagesAsync(Guid conversationId, DateTimeOffset? before, int pageSize, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var otherLastRead = me == c.GuestId ? c.HostLastReadMessageId : c.GuestLastReadMessageId;
        var otherReadAt = otherLastRead is null ? DateTimeOffset.MinValue
            : await db.Messages.Where(m => m.Id == otherLastRead).Select(m => m.SentAt).FirstOrDefaultAsync(ct);

        var q = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (before is not null) q = q.Where(m => m.SentAt < before);
        var page = await q.OrderByDescending(m => m.SentAt).Take(pageSize).ToListAsync(ct);
        var items = page.OrderBy(m => m.SentAt)
            .Select(m => ToDto(m, m.SenderId == me && m.SentAt <= otherReadAt)).ToList();
        var next = page.Count == pageSize ? page[^1].SentAt.ToString("O") : null;
        return new PagedResult<MessageDto>(items, 1, pageSize, items.Count, next);
    }

    public async Task<Result<MessageDto>> SendAsync(Guid conversationId, string body, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;
        var sent = c.Send(me, body, clock.GetUtcNow());
        if (sent.IsFailure) return sent.Error!;
        c.MarkRead(me, sent.Value.Id);
        await db.SaveChangesAsync(ct);
        await PushAsync(c, sent.Value, ct);
        return ToDto(sent.Value, false);
    }

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    // Active PDF content we refuse outright (scripts, auto-actions, launching programs, embedded files).
    private static readonly string[] PdfActiveContent = ["/JavaScript", "/JS", "/OpenAction", "/AA", "/Launch", "/EmbeddedFile", "/RichMedia"];

    public async Task<Result<MessageDto>> SendAttachmentAsync(Guid conversationId, Stream content, string fileName, string? caption, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;

        // The endpoint caps the request body (RequestSizeLimit), so buffering here is bounded.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0 || buffer.Length > MessageAttachment.MaxBytes)
            return Error.Validation("attachment.size", "Attachments must be between 1 byte and 5 MB.");
        var bytes = buffer.ToArray();

        byte[] stored;
        string contentType, extension;
        if (bytes.AsSpan().StartsWith(PdfMagic))
        {
            var text = System.Text.Encoding.Latin1.GetString(bytes);
            if (PdfActiveContent.Any(token => text.Contains(token, StringComparison.Ordinal)))
                return Error.Validation("attachment.unsafe", "PDFs with scripts, actions or embedded files aren't allowed.");
            (stored, contentType, extension) = (bytes, "application/pdf", ".pdf");
        }
        else
        {
            // Anything else must be a real image: decoded and re-encoded, which strips metadata and neutralises polyglots.
            var processed = await images.ProcessAsync(new MemoryStream(bytes), ct);
            if (processed.IsFailure) return processed.Error!; // e.g. not an image, too small, too large
            (stored, contentType, extension) = (processed.Value.Full, processed.Value.ContentType, processed.Value.Extension);
        }

        if (!await malwareScanner.IsCleanAsync(stored, ct))
            return Error.Validation("attachment.rejected", "This file was rejected by the security scan.");

        var key = $"{conversationId:N}/{Guid.NewGuid():N}{extension}";
        await using (var upload = new MemoryStream(stored))
            await storage.UploadAsync(StorageContainers.MessageAttachments, key, upload, contentType, ct);

        var safeName = SafeFileName(fileName, extension);
        var sent = c.Send(me, caption ?? string.Empty, new MessageAttachment(key, safeName, contentType, stored.LongLength), clock.GetUtcNow());
        if (sent.IsFailure)
        {
            await storage.DeleteAsync(StorageContainers.MessageAttachments, key, ct);
            return sent.Error!;
        }

        c.MarkRead(me, sent.Value.Id);
        await db.SaveChangesAsync(ct);
        await PushAsync(c, sent.Value, ct);
        return ToDto(sent.Value, false);
    }

    public async Task<Result<AttachmentDownload>> DownloadAttachmentAsync(Guid conversationId, Guid messageId, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;
        var attachment = await db.Messages.AsNoTracking().Where(m => m.Id == messageId && m.ConversationId == conversationId)
            .Select(m => m.Attachment).FirstOrDefaultAsync(ct);
        if (attachment is null) return Error.NotFound("attachment.not_found", "Attachment not found.");
        var stream = await storage.DownloadAsync(StorageContainers.MessageAttachments, attachment.StorageKey, ct);
        return stream is null
            ? Error.NotFound("attachment.not_found", "Attachment not found.")
            : new AttachmentDownload(stream, attachment.FileName, attachment.ContentType);
    }

    private static string SafeFileName(string fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var cleaned = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ').ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "attachment";
        return (cleaned.Length > 80 ? cleaned[..80] : cleaned) + extension;
    }

    private static MessageDto ToDto(Message m, bool isRead) => new(m.Id, m.ConversationId, m.SenderId, m.Body, m.SentAt, isRead,
        m.Attachment is null ? null : new MessageAttachmentDto(m.Attachment.FileName, m.Attachment.ContentType, m.Attachment.SizeBytes,
            $"/api/v1/conversations/{m.ConversationId}/messages/{m.Id}/attachment"));

    public async Task<Result> MarkReadAsync(Guid conversationId, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;
        var last = await db.Messages.Where(m => m.ConversationId == conversationId).OrderByDescending(m => m.SentAt).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        if (last is null) return Result.Success();
        c.MarkRead(me, last.Value);
        await db.SaveChangesAsync(ct);
        await SafePush(c.OtherParticipant(me), "ReadReceipt", new { conversationId, readerId = me, messageId = last }, ct);
        return Result.Success();
    }

    public async Task<Result> ReportAsync(Guid conversationId, string reason, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;
        c.Report();
        db.Reports.Add(Domain.Trust.Report.File(me, "Conversation", conversationId, reason, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> GetOtherParticipantAsync(Guid conversationId, CancellationToken ct)
    {
        var me = currentUser.RequireUserId();
        var c = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == conversationId, ct);
        if (c is null || !c.IsParticipant(me)) return NotFound;
        return c.OtherParticipant(me);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var conversations = await ListConversationsAsync(ct);
        return conversations.Sum(c => c.UnreadCount);
    }

    private Task PushAsync(Conversation c, Message m, CancellationToken ct)
    {
        var dto = ToDto(m, false);
        return Task.WhenAll(SafePush(c.OtherParticipant(m.SenderId), "ReceiveMessage", dto, ct), SafePush(m.SenderId, "ReceiveMessage", dto, ct));
    }

    private async Task SafePush(Guid userId, string method, object payload, CancellationToken ct)
    {
        try
        {
            await realtime.SendToUserAsync(userId, method, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Realtime push {Method} failed", method); // Persisted anyway; clients re-sync on reconnect.
        }
    }
}

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<int> UnreadCountAsync(CancellationToken ct);
    Task MarkReadAsync(Guid id, CancellationToken ct);
    Task MarkAllReadAsync(CancellationToken ct);
    Task NotifyAsync(Guid userId, string type, string title, string body, string? link, CancellationToken ct, bool email = false);
}

public sealed class NotificationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRealtimeNotifier realtime,
    IEmailSender emailSender,
    IPresenceTracker presence,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<Identity.AppOptions> options,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<PagedResult<NotificationDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.IsRead, n.CreatedAt)).ToListAsync(ct);
        return new PagedResult<NotificationDto>(items, page, pageSize, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.Notifications.Where(n => n.Id == id && n.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task NotifyAsync(Guid userId, string type, string title, string body, string? link, CancellationToken ct, bool email = false)
    {
        var notification = Notification.Create(userId, type, title, body, link, clock.GetUtcNow());
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        try
        {
            await realtime.SendToUserAsync(userId, "Notification",
                new NotificationDto(notification.Id, type, title, body, link, false, notification.CreatedAt), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Realtime notification push failed");
        }

        // Email when explicitly requested (booking lifecycle) or when the user is offline and would miss the push.
        if (!email && presence.IsOnline(userId)) return;
        if (!email && type != "message") return;
        var to = await db.Users.Where(u => u.Id == userId && u.Status == Domain.Identity.UserStatus.Active).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (to is null) return;
        var url = link is null ? null : options.Value.PublicWebUrl + link;
        await emailSender.SendAsync(new Abstractions.EmailMessage(to, title,
            Identity.EmailTemplates.Layout(title, $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>", url, url is null ? null : "View on StaySphere")), ct);
    }
}
