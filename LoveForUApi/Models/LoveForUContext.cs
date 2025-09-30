using Microsoft.EntityFrameworkCore;

namespace LoveForU.Models;

public class LoveForUContext : DbContext
{
    public LoveForUContext(DbContextOptions<LoveForUContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Photo> Photos { get; set; } = null!;
    public DbSet<PhotoShare> PhotoShares { get; set; } = null!;
    public DbSet<Friendship> Friendships { get; set; } = null!;
    public DbSet<ChatThread> ChatThreads { get; set; } = null!;
    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
}
