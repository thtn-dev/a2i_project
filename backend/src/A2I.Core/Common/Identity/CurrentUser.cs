namespace A2I.Core.Common.Identity;

public class CurrentUser
{
    
}

public record UserId(Guid Value)
{
    public override string ToString() => Value.ToString();
    public static UserId New() => new(Guid.NewGuid());
}