namespace AIHarness.Prompting;

/// <summary>
/// Reads the text files that make up a prompt (CLAUDE.md, agent definitions, specs). They may come from an external
/// repository, so symbolic links (which could point at any local file) and oversized files are rejected.
/// </summary>
public static class SafeFile
{
    /// <summary>Largest file accepted by <see cref="ReadText"/>.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <exception cref="InvalidDataException">The file is a symbolic link or larger than <see cref="MaxBytes"/>.</exception>
    public static string ReadText(string path)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null)
            throw new InvalidDataException($"'{path}' es un enlace simbólico; no se permite.");
        if (file.Length > MaxBytes)
            throw new InvalidDataException($"'{path}' supera {MaxBytes / 1024} KB.");
        return File.ReadAllText(path);
    }
}
