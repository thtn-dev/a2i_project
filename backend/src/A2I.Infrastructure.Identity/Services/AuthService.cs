using A2I.Infrastructure.Identity.Entities;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace A2I.Infrastructure.Identity.Services;

public interface IAuthService
{
    Task<(bool Success, string Message, LoginResponse? Data)> RegisterAsync(RegisterRequest request);
    Task<(bool Success, string Message, LoginResponse? Data)> LoginAsync(LoginRequest request, string ipAddress);
    Task<(bool Success, string Message, LoginResponse? Data)> RefreshTokenAsync(string refreshToken, string ipAddress);
    Task<(bool Success, string Message)> RevokeTokenAsync(string refreshToken, string ipAddress);
    Task<UserInfo?> GetUserInfoAsync(Guid userId);
}

public class AuthService : IAuthService
{
    private readonly AppIdentityDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IJwtService jwtService,
        AppIdentityDbContext dbContext)
    {
        _userManager = userManager;
        _jwtService = jwtService;
        _dbContext = dbContext;
    }

    public async Task<(bool Success, string Message, LoginResponse? Data)> RegisterAsync(RegisterRequest request)
    {
        // Validate passwords match
        if (request.Password != request.ConfirmPassword) return (false, "Passwords do not match", null);

        // Check if user already exists
        var existingUser = await _userManager.FindByNameAsync(request.Username);
        if (existingUser != null) return (false, "Username already exists", null);

        var existingEmail = await _userManager.FindByEmailAsync(request.Email);
        if (existingEmail != null) return (false, "Email already exists", null);

        // Create new user
        var user = new ApplicationUser
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = false // Require email confirmation
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, $"Registration failed: {errors}", null);
        }

        // Optionally add to default role
        await _userManager.AddToRoleAsync(user, "User");

        // Generate email confirmation token
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        // TODO: Send email confirmation link

        return (true, "Registration successful. Please check your email to confirm your account.", null);
    }

    public async Task<(bool Success, string Message, LoginResponse? Data)> LoginAsync(LoginRequest request,
        string ipAddress)
    {
        // Find user
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null) return (false, "Invalid username or password", null);

        // Check if account is locked out
        if (await _userManager.IsLockedOutAsync(user))
            return (false, "Account is locked out. Please try again later.", null);

        // Check if email is confirmed (if required)
        if (!user.EmailConfirmed && _userManager.Options.SignIn.RequireConfirmedEmail)
            return (false, "Email not confirmed. Please confirm your email first.", null);

        // Check password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);

        if (!isPasswordValid)
        {
            // Increment failed login count
            await _userManager.AccessFailedAsync(user);

            // Check if account is now locked out after this failed attempt
            if (await _userManager.IsLockedOutAsync(user))
                return (false, "Account is locked out due to multiple failed login attempts.", null);

            return (false, "Invalid username or password", null);
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
            ExpiresAt = DateTime.UtcNow.AddDays(7), // 7 days expiration
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
            3600, // 60 minutes in seconds
            userInfo);

        return (true, "Login successful", loginResponse);
    }

    public async Task<(bool Success, string Message, LoginResponse? Data)> RefreshTokenAsync(string refreshToken,
        string ipAddress)
    {
        var token = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == refreshToken);

        if (token == null || !token.IsActive) return (false, "Invalid or expired refresh token", null);

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

        return (true, "Token refreshed successfully", loginResponse);
    }

    public async Task<(bool Success, string Message)> RevokeTokenAsync(string refreshToken, string ipAddress)
    {
        var token = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.Token == refreshToken);

        if (token == null || !token.IsActive) return (false, "Invalid refresh token");

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;

        await _dbContext.SaveChangesAsync();

        return (true, "Token revoked successfully");
    }

    public async Task<UserInfo?> GetUserInfoAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return null;

        return new UserInfo(
            user.Id,
            user.UserName!,
            user.Email!,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled);
    }
}