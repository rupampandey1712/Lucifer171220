using StaySphere.Domain.Common;

namespace StaySphere.Domain.Engagement;

public sealed class Favorite
{
    private Favorite() { }

    public Favorite(Guid userId, Guid propertyId, DateTimeOffset at)
    {
        UserId = userId;
        PropertyId = propertyId;
        CreatedAt = at;
    }

    public Guid UserId { get; private set; }
    public Guid PropertyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>A guest ↔ host conversation, optionally in the context of a property/reservation.</summary>
public sealed class Conversation : AggregateRoot
{
    private readonly List<Message> _messages = [];

    private Conversation() { }

    public Guid PropertyId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid GuestId { get; private set; }
    public Guid HostId { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }
    public Guid? GuestLastReadMessageId { get; private set; }
    public Guid? HostLastReadMessageId { get; private set; }
    public bool IsReported { get; private set; }
    public IReadOnlyCollection<Message> Messages => _messages;

    public static Result<Conversation> Start(Guid propertyId, Guid? reservationId, Guid guestId, Guid hostId, DateTimeOffset now)
    {
        if (guestId == hostId) return Error.Validation("conversation.self", "You cannot message yourself.");
        return new Conversation
        {
            PropertyId = propertyId,
            ReservationId = reservationId,
            GuestId = guestId,
            HostId = hostId,
            LastMessageAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool IsParticipant(Guid userId) => userId == GuestId || userId == HostId;

    public Guid OtherParticipant(Guid userId) => userId == GuestId ? HostId : GuestId;

    public Result<Message> Send(Guid senderId, string body, DateTimeOffset now)
    {
        if (!IsParticipant(senderId)) return Error.Forbidden("conversation.not_participant", "You are not part of this conversation.");
        var text = body.Trim();
        if (text.Length is 0 or > 4000) return Error.Validation("message.length", "Messages must be between 1 and 4000 characters.");
        var message = new Message(Id, senderId, text, now);
        _messages.Add(message);
        LastMessageAt = now;
        Raise(new MessageSentDomainEvent(Id, message.Id, senderId, OtherParticipant(senderId)));
        return message;
    }

    public void MarkRead(Guid userId, Guid messageId)
    {
        if (userId == GuestId) GuestLastReadMessageId = messageId;
        else if (userId == HostId) HostLastReadMessageId = messageId;
    }

    public void Report() => IsReported = true;
}

public sealed class Message : Entity
{
    private Message() { }

    internal Message(Guid conversationId, Guid senderId, string body, DateTimeOffset sentAt)
    {
        ConversationId = conversationId;
        SenderId = senderId;
        Body = body;
        SentAt = sentAt;
    }

    public Guid ConversationId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Body { get; private set; } = default!;
    public DateTimeOffset SentAt { get; private set; }
}

public sealed class Notification : Entity
{
    private Notification() { }

    public Guid UserId { get; private set; }
    public string Type { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string? Link { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(Guid userId, string type, string title, string body, string? link, DateTimeOffset now) =>
        new() { UserId = userId, Type = type, Title = title, Body = body, Link = link, CreatedAt = now };

    public void MarkRead() => IsRead = true;
}

public sealed record MessageSentDomainEvent(Guid ConversationId, Guid MessageId, Guid SenderId, Guid RecipientId) : IDomainEvent;
