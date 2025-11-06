using FluentResults;

namespace A2I.Application.Common;

public class NotFoundError(string message) : Error(message);

public class ValidationError(string message) : Error(message);

public class UnauthorizedError(string message) : Error(message);

public class ForbiddenError(string message) : Error(message);

public class ConflictError(string message) : Error(message);

public class ExternalServiceError(string message) : Error(message);

public class DatabaseError(string message) : Error(message);

public class UnexpectedError(string message) : Error(message);