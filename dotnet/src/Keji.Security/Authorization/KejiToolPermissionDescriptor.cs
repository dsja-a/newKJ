namespace Keji.Security.Authorization;

public sealed class KejiToolPermissionDescriptor
{
    public string Name { get; }
    public KejiToolAccessLevel AccessLevel { get; }

    public KejiToolPermissionDescriptor(string name, KejiToolAccessLevel accessLevel)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        if (name.Length == 0)
            throw new ArgumentException("Tool name must not be empty.", nameof(name));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name must not be whitespace.", nameof(name));

        if (name.Length > 256)
            throw new ArgumentException("Tool name must not exceed 256 characters.", nameof(name));

        if (name.AsSpan().Trim().Length != name.Length)
            throw new ArgumentException("Tool name must not have leading or trailing whitespace.", nameof(name));

        if (!Enum.IsDefined(accessLevel))
            throw new ArgumentOutOfRangeException(nameof(accessLevel), accessLevel, "Invalid tool access level.");

        Name = name;
        AccessLevel = accessLevel;
    }
}
