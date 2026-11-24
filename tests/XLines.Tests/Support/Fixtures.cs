using System.Text.Json;

namespace XLines.Tests.Support;

/// <summary>
/// The recorded provider payloads under <c>Fixtures/</c>, read from beside the test assembly where
/// the project copies them, so a test parses a real response shape without a network call.
/// </summary>
public static class Fixtures
{
    /// <summary>Where the named fixture sits once copied to the output directory.</summary>
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>The fixture as the provider sent it, for a stub handler to answer with or a test to edit.</summary>
    public static string Text(string name) => File.ReadAllText(Path(name));

    /// <summary>
    /// The fixture's root element, cloned so it outlives the document it was parsed from: a parser
    /// under test may hold it past the helper's return.
    /// </summary>
    public static JsonElement Json(string name) => JsonDocument.Parse(Text(name)).RootElement.Clone();
}
