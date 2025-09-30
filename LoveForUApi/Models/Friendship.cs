using System;

namespace LoveForU.Models;

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Blocked = 3
}

public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RequesterId { get; set; } = null!;
    public User? Requester { get; set; }
    public string AddresseeId { get; set; } = null!;
    public User? Addressee { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
    public ChatThread? Thread { get; set; }
}
