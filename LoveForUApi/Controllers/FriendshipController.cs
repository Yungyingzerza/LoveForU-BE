using System.Security.Claims;
using LoveForU.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoveForUApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class FriendshipController : ControllerBase
{
    private readonly LoveForUContext _context;
    private readonly ILogger<FriendshipController> _logger;

    public FriendshipController(LoveForUContext context, ILogger<FriendshipController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<FriendshipResponse>> AddFriend(AddFriendRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FriendUserId))
        {
            return BadRequest("Friend user id is required.");
        }

        var requesterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(requesterId))
        {
            return Unauthorized();
        }

        if (string.Equals(requesterId, request.FriendUserId, StringComparison.Ordinal))
        {
            return BadRequest("You cannot add yourself as a friend.");
        }

        var targetUserExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == request.FriendUserId, cancellationToken);

        if (!targetUserExists)
        {
            return NotFound("Friend user not found.");
        }

        var existingFriendship = await _context.Friendships
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == requesterId && f.AddresseeId == request.FriendUserId) ||
                (f.RequesterId == request.FriendUserId && f.AddresseeId == requesterId),
                cancellationToken);

        if (existingFriendship is not null)
        {
            return existingFriendship.Status switch
            {
                FriendshipStatus.Accepted => Conflict("Users are already friends."),
                FriendshipStatus.Pending => Conflict(ToResponse(existingFriendship)),
                FriendshipStatus.Declined => Conflict("Friend request was declined."),
                FriendshipStatus.Blocked => Forbid(),
                _ => Conflict(ToResponse(existingFriendship))
            };
        }

        var friendship = new Friendship
        {
            RequesterId = requesterId,
            AddresseeId = request.FriendUserId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Friendships.Add(friendship);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create friendship between {RequesterId} and {AddresseeId}", requesterId, request.FriendUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to add friend at this time.");
        }

        return CreatedAtAction(nameof(GetFriendship), new { id = friendship.Id }, ToResponse(friendship));
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<FriendshipResponse>>> GetPendingFriendships([FromQuery] string? direction, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var normalizedDirection = direction?.Trim().ToLowerInvariant();
        var query = _context.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending);

        query = normalizedDirection switch
        {
            "outgoing" => query.Where(f => f.RequesterId == userId),
            "all" => query.Where(f => f.RequesterId == userId || f.AddresseeId == userId),
            _ => query.Where(f => f.AddresseeId == userId)
        };

        var pendingFriendships = await query
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(pendingFriendships.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FriendshipResponse>> GetFriendship(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var friendship = await _context.Friendships
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id &&
                (f.RequesterId == userId || f.AddresseeId == userId),
                cancellationToken);

        if (friendship is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(friendship));
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<FriendshipResponse>> AcceptFriendship(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (friendship is null)
        {
            return NotFound();
        }

        if (!string.Equals(friendship.AddresseeId, userId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return Conflict("Friend request already handled.");
        }

        friendship.Status = FriendshipStatus.Accepted;
        friendship.RespondedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(friendship));
    }

    [HttpPost("{id:guid}/deny")]
    public async Task<ActionResult<FriendshipResponse>> DenyFriendship(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (friendship is null)
        {
            return NotFound();
        }

        if (!string.Equals(friendship.AddresseeId, userId, StringComparison.Ordinal))
        {
            return Forbid();
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return Conflict("Friend request already handled.");
        }

        friendship.Status = FriendshipStatus.Declined;
        friendship.RespondedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(friendship));
    }

    private static FriendshipResponse ToResponse(Friendship friendship) =>
        new(friendship.Id, friendship.RequesterId, friendship.AddresseeId, friendship.Status, friendship.CreatedAt, friendship.RespondedAt);
}

public record AddFriendRequest(string FriendUserId);

public record FriendshipResponse(Guid Id, string RequesterId, string AddresseeId, FriendshipStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? RespondedAt);
