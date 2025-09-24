using System.ComponentModel.DataAnnotations;
using LoveForU.Models;
using LoveForUApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoveForUApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly LoveForUContext _context;
    private readonly ILineAuthService _lineAuthService;
    private readonly IJwtTokenService _jwtTokenService;

    public UserController(
        LoveForUContext context,
        ILineAuthService lineAuthService,
        IJwtTokenService jwtTokenService)
    {
        _context = context;
        _lineAuthService = lineAuthService;
        _jwtTokenService = jwtTokenService;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<UserResponse>>> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _context.Users.AsNoTracking().ToListAsync(cancellationToken);
        return Ok(users.Select(ToResponse));
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> GetUser(string id, CancellationToken cancellationToken)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(user));
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> PutUser(string id, UserUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(id, request.Id, StringComparison.Ordinal))
        {
            return BadRequest("User id mismatch");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.displayName = request.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(request.PictureUrl))
        {
            user.pictureUrl = request.PictureUrl;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<UserResponse>> PostUser(LineLoginRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return BadRequest("Access token is required");
        }

        var profile = await _lineAuthService.GetProfileAsync(request.AccessToken, cancellationToken);
        if (profile is null)
        {
            return Unauthorized();
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == profile.UserId, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = profile.UserId,
                displayName = profile.DisplayName,
                pictureUrl = profile.PictureUrl ?? string.Empty
            };

            _context.Users.Add(user);
        }
        else
        {
            if (!string.Equals(user.displayName, profile.DisplayName, StringComparison.Ordinal))
            {
                user.displayName = profile.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(profile.PictureUrl) &&
                !string.Equals(user.pictureUrl, profile.PictureUrl, StringComparison.Ordinal))
            {
                user.pictureUrl = profile.PictureUrl!;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        var jwt = _jwtTokenService.GenerateToken(user);
        Response.Cookies.Append(_jwtTokenService.CookieName, jwt, _jwtTokenService.BuildCookieOptions());

        return Ok(ToResponse(user));
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken cancellationToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static UserResponse ToResponse(User user) => new(user.Id, user.displayName, user.pictureUrl);
}

public record LineLoginRequest([param: Required] string AccessToken);

public record UserResponse(string Id, string DisplayName, string PictureUrl);

public record UserUpdateRequest
{
    [Required]
    public string Id { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? PictureUrl { get; init; }
}
