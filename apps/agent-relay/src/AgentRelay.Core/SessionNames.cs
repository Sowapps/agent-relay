namespace AgentRelay.Core;

/// <summary>Owns session name policy shared by application, retained sessions and MCP schema.</summary>
public static class SessionNames
{
    public const int MaximumLength = 120;

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumLength || name.Any(char.IsControl))
            throw new RelayException("invalid_session", $"A readable session name of at most {MaximumLength} characters is required.");
        return name.Trim();
    }
}
