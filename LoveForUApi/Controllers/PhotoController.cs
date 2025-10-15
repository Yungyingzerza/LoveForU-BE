using System.Security.Claims;
using LoveForU.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoveForUApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class PhotoController : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",
        ".bmp"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/bmp"
    };

    private readonly LoveForUContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PhotoController> _logger;

    public PhotoController(LoveForUContext context, IWebHostEnvironment environment, ILogger<PhotoController> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PhotoResponse>>> GetMyPhotos(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var friendIds = await _context.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                        (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync(cancellationToken);

        friendIds.Add(userId);
        var relevantUserIds = friendIds.Distinct().ToList();

        var photos = await _context.Photos.AsNoTracking()
            .Where(p => relevantUserIds.Contains(p.UploaderId))
            .Include(p => p.Uploader)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(photos.Select(p => ToResponse(p, p.Uploader?.displayName ?? string.Empty)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PhotoResponse>> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var photo = await _context.Photos.AsNoTracking()
            .Include(p => p.Uploader)
            .FirstOrDefaultAsync(p => p.Id == id && p.UploaderId == userId, cancellationToken);

        if (photo is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(photo, photo.Uploader?.displayName ?? string.Empty));
    }

    [HttpPost]
    public async Task<ActionResult<PhotoResponse>> Upload([FromForm] PhotoUploadRequest request, CancellationToken cancellationToken)
    {
        if (request.Image is null || request.Image.Length == 0)
        {
            return BadRequest("Image file is required.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Forbid();
        }

        var extension = Path.GetExtension(request.Image.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return BadRequest("Unsupported image file type.");
        }

        if (!AllowedContentTypes.Contains(request.Image.ContentType))
        {
            return BadRequest("Unsupported image content type.");
        }

        var uploadsRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        uploadsRoot = Path.Combine(uploadsRoot, "uploads");

        Directory.CreateDirectory(uploadsRoot); // ensures upload folder exists

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(uploadsRoot, fileName);
        var relativePath = $"/uploads/{fileName}";

        try
        {
            await using (var stream = System.IO.File.Create(filePath))
            {
                await request.Image.CopyToAsync(stream, cancellationToken);
            }

            var photo = new Photo
            {
                UploaderId = userId,
                ImageUrl = relativePath,
                Caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim()
            };

            _context.Photos.Add(photo);
            await _context.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(nameof(GetPhoto), new { id = photo.Id }, ToResponse(photo, user.displayName));
        }
        catch (Exception ex)
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            _logger.LogError(ex, "Failed to save uploaded photo for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to save photo at this time.");
        }
    }

    private static PhotoResponse ToResponse(Photo photo, string uploaderDisplayName)
        => new(photo.Id, photo.UploaderId, uploaderDisplayName, photo.ImageUrl, photo.Caption, photo.CreatedAt);
}

public record PhotoUploadRequest(IFormFile Image, string? Caption);

public record PhotoResponse(Guid Id, string UploaderId, string UploaderDisplayName, string ImageUrl, string? Caption, DateTimeOffset CreatedAt);
