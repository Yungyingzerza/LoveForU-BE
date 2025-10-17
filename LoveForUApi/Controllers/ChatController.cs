using System.Security.Claims;
using System.Text.Json;
using LoveForU.Models;
using LoveForUApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoveForUApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private const int DefaultMessageLimit = 50;
    private const int MaxMessageLimit = 200;
    private static readonly JsonSerializerOptions SseSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly LoveForUContext _context;
    private readonly IChatNotificationService _notificationService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        LoveForUContext context,
        IChatNotificationService notificationService,
        ILogger<ChatController> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpGet("threads")]
    public async Task<ActionResult<IEnumerable<ChatThreadResponse>>> GetThreads(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var threads = await _context.ChatThreads
            .AsNoTracking()
            .Where(t => t.Friendship.Status == FriendshipStatus.Accepted &&
                        (t.Friendship.RequesterId == userId || t.Friendship.AddresseeId == userId))
            .Select(t => new
            {
                t.Id,
                t.FriendshipId,
                FriendId = t.Friendship.RequesterId == userId ? t.Friendship.AddresseeId : t.Friendship.RequesterId,
                Friend = t.Friendship.RequesterId == userId ? t.Friendship.Addressee : t.Friendship.Requester,
                LastMessage = t.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault(),
                t.Friendship.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var responses = threads
            .Select(t =>
            {
                var friend = t.Friend;
                var lastMessage = t.LastMessage;
                return new ChatThreadResponse(
                    t.Id,
                    t.FriendshipId,
                    t.FriendId,
                    friend?.displayName ?? string.Empty,
                    friend?.pictureUrl ?? string.Empty,
                    lastMessage?.Id,
                    lastMessage?.Content,
                    lastMessage?.CreatedAt,
                    t.CreatedAt);
            })
            .OrderByDescending(t => t.LastMessageAt ?? t.CreatedAt)
            .ToList();

        return Ok(responses);
    }

    [HttpGet("threads/{threadId:guid}/messages")]
    public async Task<ActionResult<IEnumerable<ChatMessageResponse>>> GetMessages(
        Guid threadId,
        [FromQuery] int? limit,
        [FromQuery] DateTimeOffset? after,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var membership = await _context.ChatThreads
            .AsNoTracking()
            .Where(t => t.Id == threadId && t.Friendship.Status == FriendshipStatus.Accepted)
            .Select(t => new
            {
                t.FriendshipId,
                t.Friendship.RequesterId,
                t.Friendship.AddresseeId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return NotFound();
        }

        if (!string.Equals(membership.RequesterId, userId, StringComparison.Ordinal) &&
            !string.Equals(membership.AddresseeId, userId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        var take = Math.Clamp(limit ?? DefaultMessageLimit, 1, MaxMessageLimit);

        var query = _context.ChatMessages
            .AsNoTracking()
            .Where(m => m.ThreadId == threadId);

        if (after.HasValue)
        {
            query = query.Where(m => m.CreatedAt > after.Value);
        }

        query = query.Include(m => m.Sender)
            .Include(m => m.Photo)
            .OrderByDescending(m => m.CreatedAt);

        var messages = await query
            .Take(take)
            .ToListAsync(cancellationToken);

        messages.Reverse();

        return Ok(messages.Select(ToResponse));
    }

    [HttpPost("friendships/{friendshipId:guid}/messages")]
    public async Task<ActionResult<ChatMessageResponse>> SendMessage(
        Guid friendshipId,
        SendChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request payload is required.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Content) && request.PhotoShareId is null && request.PhotoId is null)
        {
            return BadRequest("Message content or a photo share id is required.");
        }

        var friendship = await _context.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Include(f => f.Thread)
            .FirstOrDefaultAsync(f => f.Id == friendshipId &&
                                      f.Status == FriendshipStatus.Accepted &&
                                      (f.RequesterId == userId || f.AddresseeId == userId),
                cancellationToken);

        if (friendship is null)
        {
            return NotFound();
        }

        var senderIsRequester = string.Equals(friendship.RequesterId, userId, StringComparison.Ordinal);
        var recipientId = senderIsRequester ? friendship.AddresseeId : friendship.RequesterId;

        if (string.IsNullOrEmpty(recipientId))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Recipient could not be determined.");
        }

        Photo? resolvedPhoto = null;
        if (request.PhotoShareId.HasValue)
        {
            var existingShare = await _context.PhotoShares
                .AsNoTracking()
                .Include(ps => ps.Photo)
                .FirstOrDefaultAsync(ps => ps.Id == request.PhotoShareId.Value, cancellationToken);

            if (existingShare is null)
            {
                return BadRequest("Photo share not found.");
            }

            var shareAccessible = string.Equals(existingShare.RecipientId, userId, StringComparison.Ordinal) ||
                                  string.Equals(existingShare.RecipientId, recipientId, StringComparison.Ordinal) ||
                                  (existingShare.Photo is not null &&
                                      string.Equals(existingShare.Photo.UploaderId, userId, StringComparison.Ordinal));

            if (!shareAccessible)
            {
                return BadRequest("You cannot attach this photo share to the chat message.");
            }

            resolvedPhoto = existingShare.Photo;
        }
        else if (request.PhotoId.HasValue)
        {
            var photo = await _context.Photos
                .Include(p => p.Shares)
                .FirstOrDefaultAsync(p => p.Id == request.PhotoId.Value, cancellationToken);

            if (photo is null)
            {
                return BadRequest("Photo not found.");
            }

            var senderCanUsePhoto = string.Equals(photo.UploaderId, userId, StringComparison.Ordinal) ||
                                    photo.Shares.Any(s => string.Equals(s.RecipientId, userId, StringComparison.Ordinal)) ||
                                    (!photo.Shares.Any() &&
                                     (string.Equals(photo.UploaderId, recipientId, StringComparison.Ordinal) ||
                                      string.Equals(photo.UploaderId, userId, StringComparison.Ordinal)));

            if (!senderCanUsePhoto)
            {
                return BadRequest("You cannot attach this photo to the chat message.");
            }

            resolvedPhoto = photo;
        }

        var thread = friendship.Thread;
        if (thread is null)
        {
            thread = new ChatThread
            {
                FriendshipId = friendship.Id
            };
            friendship.Thread = thread;
            _context.ChatThreads.Add(thread);
        }

        var trimmedContent = string.IsNullOrWhiteSpace(request.Content)
            ? null
            : request.Content.Trim();

        var message = new ChatMessage
        {
            Thread = thread,
            ThreadId = thread.Id,
            SenderId = userId,
            Sender = senderIsRequester ? friendship.Requester : friendship.Addressee,
            Content = trimmedContent,
            PhotoId = resolvedPhoto?.Id,
            Photo = resolvedPhoto,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.ChatMessages.Add(message);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save chat message for friendship {FriendshipId}", friendship.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to send message at this time.");
        }

        var recipients = new[] { userId, recipientId };
        var notification = new ChatNotification(thread.Id, message.Id, userId);
        await _notificationService.PublishAsync(recipients, notification, cancellationToken);

        if (message.Sender is null)
        {
            await _context.Entry(message).Reference(m => m.Sender).LoadAsync(cancellationToken);
        }

        if (message.PhotoId.HasValue && message.Photo is null)
        {
            await _context.Entry(message).Reference(m => m.Photo).LoadAsync(cancellationToken);
        }

        var response = ToResponse(message);
        return CreatedAtAction(nameof(GetMessages), new { threadId = thread.Id }, response);
    }

    [HttpGet("events")]
    public async Task GetEvents(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = _notificationService.Subscribe(userId);

        await Response.Body.FlushAsync(cancellationToken);

        try
        {
            await foreach (var notification in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                var payload = JsonSerializer.Serialize(notification, SseSerializerOptions);
                await Response.WriteAsync($"event: {notification.Event}\n", cancellationToken);
                await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // connection closed by client
        }
    }

    private static ChatMessageResponse ToResponse(ChatMessage message)
    {
        var sender = message.Sender;
        return new ChatMessageResponse(
            message.Id,
            message.ThreadId,
            message.SenderId,
            sender?.displayName ?? string.Empty,
            sender?.pictureUrl ?? string.Empty,
            message.Content,
            message.PhotoId,
            message.Photo is not null
                ? new PhotoAttachmentResponse(
                    message.Photo.Id,
                    message.Photo.ImageUrl,
                    message.Photo.Caption)
                : null,
            message.CreatedAt);
    }
}

public record ChatThreadResponse(
    Guid ThreadId,
    Guid FriendshipId,
    string FriendUserId,
    string FriendDisplayName,
    string FriendPictureUrl,
    Guid? LastMessageId,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt);

public record ChatMessageResponse(
    Guid Id,
    Guid ThreadId,
    string SenderId,
    string SenderDisplayName,
    string SenderPictureUrl,
    string? Content,
    Guid? PhotoShareId,
    PhotoAttachmentResponse? Photo,
    DateTimeOffset CreatedAt);

public record PhotoAttachmentResponse(Guid PhotoId, string ImageUrl, string? Caption);

public record SendChatMessageRequest(string? Content, Guid? PhotoShareId, Guid? PhotoId);
