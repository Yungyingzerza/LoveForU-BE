using System;
using System.Collections.Generic;

namespace LoveForU.Models;

public class ChatThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FriendshipId { get; set; }
    public Friendship Friendship { get; set; } = null!;
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
