using System.Text.RegularExpressions;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Common.Identity;

public class CurrentUser
{
    public UserId Id { get; init; } = null!;
    public string UserName { get; init; } = null!;
    public EmailAddress Email { get; init; } = null!;
}

public record UserId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static UserId New() => new(Guid.NewGuid());
}


public class EmailAddress : ValueObject
{
    // Regex pattern theo RFC 5322 (simplified)
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100)
    );

    private const int MaxLength = 254; // RFC 5321
    
    public string Value { get; }
    public string Domain => Value.Split('@')[1];
    public string LocalPart => Value.Split('@')[0];

    private EmailAddress(string value)
    {
        Value = value;
    }

    public static EmailAddress Create(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty.", nameof(email));

        email = email.Trim().ToLowerInvariant();

        if (!IsValid(email))
            throw new ArgumentException($"Invalid email address: {email}", nameof(email));

        return new EmailAddress(email);
    }

    public static bool TryCreate(string email, out EmailAddress? emailAddress)
    {
        emailAddress = null;

        if (string.IsNullOrWhiteSpace(email))
            return false;

        email = email.Trim().ToLowerInvariant();

        if (!IsValid(email))
            return false;

        emailAddress = new EmailAddress(email);
        return true;
    }

    public static bool IsValid(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        email = email.Trim();

        if (email.Length > MaxLength)
            return false;

        if (!email.Contains('@'))
            return false;

        var parts = email.Split('@');
        if (parts.Length != 2)
            return false;

        var localPart = parts[0];
        var domain = parts[1];

        if (string.IsNullOrEmpty(localPart) || localPart.Length > 64)
            return false;

        if (string.IsNullOrEmpty(domain) || domain.Length > 255)
            return false;

        if (!domain.Contains('.'))
            return false;

        return EmailRegex.IsMatch(email);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToLowerInvariant(); 
    }

    public override string ToString() => Value;

    public static implicit operator string(EmailAddress email) => email.Value;

    public static explicit operator EmailAddress(string email) => Create(email);
}