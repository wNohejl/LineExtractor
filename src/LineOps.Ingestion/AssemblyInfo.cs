using System.Runtime.CompilerServices;

// Parsing helpers are internal so they are not part of the adapter's public surface,
// but they are exactly what the fixture tests need to exercise directly.
[assembly: InternalsVisibleTo("LineOps.Tests")]
