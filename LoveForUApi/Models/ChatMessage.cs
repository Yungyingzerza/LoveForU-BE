using System;

namespace LoveForU.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public ChatThread Thread { get; set; } = null!;
    public string SenderId { get; set; } = null!;
    public User? Sender { get; set; }
    public string? Content { get; set; }
    public Guid? PhotoShareId { get; set; }
    public PhotoShare? PhotoShare { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
