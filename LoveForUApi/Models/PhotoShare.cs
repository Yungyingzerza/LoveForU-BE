using System;
using System.Collections.Generic;

namespace LoveForU.Models;

public class PhotoShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PhotoId { get; set; }
    public Photo Photo { get; set; } = null!;
    public string RecipientId { get; set; } = null!;
    public User? Recipient { get; set; }
    public DateTimeOffset SharedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ViewedAt { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
