namespace SwitchPortFinder.Application.Common;

public class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }
}

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);
