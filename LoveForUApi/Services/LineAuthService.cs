using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LoveForUApi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoveForUApi.Services;

public record LineProfile(string UserId, string DisplayName, string? PictureUrl);

public interface ILineAuthService
{
    Task<LineProfile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default);
}

internal sealed class LineAuthService : ILineAuthService
{
    private readonly HttpClient _httpClient;
    private readonly LineAuthOptions _options;
    private readonly ILogger<LineAuthService> _logger;

    public LineAuthService(HttpClient httpClient, IOptions<LineAuthOptions> options, ILogger<LineAuthService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ChannelId))
        {
            throw new InvalidOperationException("LINE ChannelId is not configured. Set 'LineAuth:ChannelId' via configuration.");
        }

        _logger = logger;

        if (_httpClient.BaseAddress is null && Uri.TryCreate(_options.BaseAddress, UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }
    }

    public async Task<LineProfile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token is required", nameof(accessToken));
        }

        if (!await ValidateTokenAsync(accessToken, cancellationToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "v2/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch LINE profile. StatusCode: {StatusCode}", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<LineProfilePayload>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.userId))
        {
            _logger.LogWarning("LINE profile payload missing userId");
            return null;
        }

        return new LineProfile(payload.userId, payload.displayName ?? string.Empty, payload.pictureUrl);
    }

    private async Task<bool> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"oauth2/v2.1/verify?access_token={Uri.EscapeDataString(accessToken)}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LINE token verification failed. StatusCode: {StatusCode}", response.StatusCode);
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenVerifyPayload>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            _logger.LogWarning("LINE token verification payload missing");
            return false;
        }

        if (!string.Equals(payload.client_id, _options.ChannelId, StringComparison.Ordinal))
        {
            _logger.LogWarning("LINE token client id mismatch. Expected {Expected}, got {Actual}", _options.ChannelId, payload.client_id);
            return false;
        }

        return payload.expires_in > 0;
    }

    private sealed record LineProfilePayload(string userId, string? displayName, string? pictureUrl);

    private sealed record TokenVerifyPayload(string client_id, int expires_in);
}
