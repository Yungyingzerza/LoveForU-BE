using System;
using System.Collections.Generic;

namespace LoveForU.Models;

public class Photo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UploaderId { get; set; } = null!;
    public User? Uploader { get; set; }
    public string ImageUrl { get; set; } = null!;
    public string? Caption { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<PhotoShare> Shares { get; set; } = new List<PhotoShare>();
}
