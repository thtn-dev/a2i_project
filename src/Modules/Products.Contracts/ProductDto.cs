namespace Modules.Products.Contracts;

/// <param name="CreatedBy">Id of the user who created it (stable user id, never an email).</param>
public sealed record ProductDto(Guid Id, string Name, decimal Price, Guid CreatedBy);

public sealed record CreateProductRequest(string Name, decimal Price);
