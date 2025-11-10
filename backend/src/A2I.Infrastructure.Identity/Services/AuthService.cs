using A2I.Application.Common;
using A2I.Infrastructure.Identity.Entities;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Security;
using FluentResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace A2I.Infrastructure.Identity.Services;

public interface IAuthService
{
    Task<Result<LoginResponse>> RegisterAsync(RegisterRequest request);
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string ipAddress);
    Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, string ipAddress);
    Task<Result> RevokeTokenAsync(string refreshToken, string ipAddress);
    Task<Result<UserInfo>> GetUserInfoAsync(Guid userId);
}

public class AuthService : IAuthService
{
    private readonly AppIdentityDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDistributedCache _cache;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IJwtService jwtService,
        IDistributedCache cache,
        AppIdentityDbContext dbContext)
    {
        _userManager = userManager;
        _jwtService = jwtService;
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Result<LoginResponse>> RegisterAsync(RegisterRequest request)
    {
        // Validate passwords match
        if (request.Password != request.ConfirmPassword)
            return Errors.Validation("Passwords do not match");

        // Check if user already exists
        var existingUser = await _userManager.FindByNameAsync(request.Username);
        if (existingUser != null)
            return Errors.Conflict("Username already exists");

        var existingEmail = await _userManager.FindByEmailAsync(request.Email);
        if (existingEmail != null)
            return Errors.Conflict("Email already exists");

        // Create new user
        var user = new ApplicationUser
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.ToDictionary(x => x.Code, x => x.Description);
            return Errors.Validation(errors);
        }

        // Add to default role
        await _userManager.AddToRoleAsync(user, "User");

        // Generate email confirmation token
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        // TODO: Send email confirmation link

        // Return success without LoginResponse (registration doesn't auto-login)
        return Result.Ok<LoginResponse>(null!)
            .WithSuccess("Registration successful. Please check your email to confirm your account.");
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string ipAddress)
    {
        // Find user
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null)
            return Errors.Unauthorized("Invalid username or password");

        // Check if account is locked out
        if (await _userManager.IsLockedOutAsync(user))
            return Errors.Forbidden("Account is locked out. Please try again later.");

        // Check if email is confirmed (if required)
        if (!user.EmailConfirmed && _userManager.Options.SignIn.RequireConfirmedEmail)
            return Errors.Unauthorized("Email not confirmed. Please confirm your email first.");

        // Check password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);

        if (!isPasswordValid)
        {
            // Increment failed login count
            await _userManager.AccessFailedAsync(user);

            // Check if account is now locked out after this failed attempt
            if (await _userManager.IsLockedOutAsync(user))
                return Errors.Forbidden("Account is locked out due to multiple failed login attempts.");

            return Errors.Unauthorized("Invalid username or password");
        }

        // Reset failed login count on successful login
        await _userManager.ResetAccessFailedCountAsync(user);

        // Get user roles
        var roles = await _userManager.GetRolesAsync(user);

        // Generate JWT token
        var accessToken = _jwtService.GenerateToken(
            user.Id.ToString(),
            user.UserName!,
            roles.ToList());

        // Generate refresh token
        var refreshTokenValue = _jwtService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenValue,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = ipAddress
        };

        // Remove old refresh tokens for this user (keep only last 5)
        var oldTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(5)
            .ToListAsync();

        _dbContext.RefreshTokens.RemoveRange(oldTokens);

        // Add new refresh token
        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        var userInfo = new UserInfo(
            user.Id,
            user.UserName!,
            user.Email!,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled);

        var loginResponse = new LoginResponse(
            accessToken,
            refreshTokenValue,
            "Bearer",
            3600,
            userInfo);

        return Result.Ok(loginResponse);
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, string ipAddress)
    {
        var token = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == refreshToken);

        if (token == null || !token.IsActive)
            return Errors.Unauthorized("Invalid or expired refresh token");

        var user = token.User;

        // Generate new tokens
        var roles = await _userManager.GetRolesAsync(user);

        var newAccessToken = _jwtService.GenerateToken(
            user.Id.ToString(),
            user.UserName!,
            roles.ToList());

        var newRefreshToken = _jwtService.GenerateRefreshToken();

        // Revoke old refresh token and replace it
        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;
        token.ReplacedByToken = newRefreshToken;

        // Create new refresh token
        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = newRefreshToken,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = ipAddress
        };

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync();

        var userInfo = new UserInfo(
            user.Id,
            user.UserName!,
            user.Email!,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled);

        var loginResponse = new LoginResponse(
            newAccessToken,
            newRefreshToken,
            "Bearer",
            3600,
            userInfo);

        return Result.Ok(loginResponse);
    }

    public async Task<Result> RevokeTokenAsync(string refreshToken, string ipAddress)
    {
        var token = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.Token == refreshToken);

        if (token == null || !token.IsActive)
            return Errors.NotFound("Invalid refresh token");

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;

        await _dbContext.SaveChangesAsync();

        return Result.Ok();
    }

    public async Task<Result<UserInfo>> GetUserInfoAsync(Guid userId)
    {
        var cacheKey = $"user_info_{userId}";
        var cachedUserInfo = await _cache.GetStringAsync(cacheKey);
        
        if (cachedUserInfo != null)
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<UserInfo>(cachedUserInfo);
            return Result.Ok(data!);
        }
        
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return Errors.NotFound("User not found");
        }
        
        var userInfo = new UserInfo(
            user.Id,
            user.UserName!,
            user.Email!,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled);
        
        var serializedUserInfo = System.Text.Json.JsonSerializer.Serialize(userInfo);
        await _cache.SetStringAsync(cacheKey, serializedUserInfo, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });
        
        return Result.Ok(userInfo);
    }
}