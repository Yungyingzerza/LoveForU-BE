using Microsoft.EntityFrameworkCore;

namespace LoveForU.Models;

public class LoveForUContext : DbContext
{
    public LoveForUContext(DbContextOptions<LoveForUContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
}