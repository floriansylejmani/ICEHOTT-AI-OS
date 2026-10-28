namespace ICEHOTT.Domain;

/// <summary>Workspace identifiers are generated server-side and never trusted as authorization.</summary>
public readonly record struct WorkspaceId
{
    private WorkspaceId(Guid value) => Value = value;
    public Guid Value { get; }

    public static WorkspaceId New() => new(Guid.NewGuid());

    public static WorkspaceId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("Workspace id cannot be empty.", nameof(value))
            : new WorkspaceId(value);

    public override string ToString() => Value.ToString("D");
}

/// <summary>
/// A canonical workspace slug. Unique database constraints and membership
/// authorization will be introduced in Phase 1; slug is NEVER an auth credential.
/// </summary>
public sealed record WorkspaceSlug
{
    private WorkspaceSlug(string value) => Value = value;
    public string Value { get; }

    public static WorkspaceSlug Create(string? input)
    {
        if (input is null || input.Length is < 3 or > 48 ||
            input[0] == '-' || input[^1] == '-' ||
            !input.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            throw new ArgumentException(
                "Slug must contain 3–48 lowercase ASCII letters, digits, or hyphens, with no edge hyphen.",
                nameof(input));
        return new WorkspaceSlug(input);
    }

    public override string ToString() => Value;
}
