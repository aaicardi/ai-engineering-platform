namespace AIHarness.Prompting;

/// <summary>A required context source file is missing.</summary>
public abstract class ContextSourceNotFoundException(string message, string path)
    : FileNotFoundException(message, path);

public sealed class GovernanceNotFoundException(string path)
    : ContextSourceNotFoundException($"No se encontró CLAUDE.md en '{path}'.", path);

public sealed class AgentNotFoundException(string agentName, string path, IReadOnlyList<string> available)
    : ContextSourceNotFoundException(
        $"El agente '{agentName}' no existe ({path}). Disponibles: {Format(available)}.", path)
{
    public string AgentName { get; } = agentName;
    public IReadOnlyList<string> Available { get; } = available;

    private static string Format(IReadOnlyList<string> items) => items.Count == 0 ? "(ninguno)" : string.Join(", ", items);
}

public sealed class SpecNotFoundException(string specName, string path)
    : ContextSourceNotFoundException($"La especificación '{specName}' no existe ({path}).", path)
{
    public string SpecName { get; } = specName;
}
