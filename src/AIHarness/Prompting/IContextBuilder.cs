namespace AIHarness.Prompting;

/// <summary>Unified prompt assembled from the three context levels (governance, agent, spec).</summary>
public sealed record PromptContext(string AgentName, string SpecFileName, string Prompt);

/// <summary>
/// Assembles the execution prompt for an agent by merging CLAUDE.md,
/// <c>.claude/agents/&lt;agent&gt;.md</c> and <c>openspec/specs/&lt;spec&gt;.md</c>.
/// </summary>
public interface IContextBuilder
{
    /// <summary>Builds the unified prompt.</summary>
    /// <param name="agentName">Agent name without extension (e.g. <c>developer</c>).</param>
    /// <param name="specName">Spec file name, with or without the <c>.md</c> extension.</param>
    /// <exception cref="ArgumentException">A name is empty or contains path characters.</exception>
    /// <exception cref="GovernanceNotFoundException">CLAUDE.md does not exist.</exception>
    /// <exception cref="AgentNotFoundException">The agent file does not exist.</exception>
    /// <exception cref="SpecNotFoundException">The spec file does not exist.</exception>
    PromptContext Build(string agentName, string specName);

    /// <summary>Writes the prompt to <paramref name="outputPath"/>, creating parent directories.</summary>
    void Export(PromptContext context, string outputPath);
}
