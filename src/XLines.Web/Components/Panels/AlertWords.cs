using XLines.Core.Entities;

namespace XLines.Web.Components.Panels;

/// <summary>
/// An alert's severity as the desk says it. One definition, because Ops lists the alerts and
/// Incidents opens them, and a severity read as "Warning" in one and "Warn" in the other would be
/// two words for one thing.
/// </summary>
public static class AlertWords
{
    /// <summary>Severity as a word, not an enum member: Warn reads as Warning.</summary>
    public static string Severity(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Warn => "Warning",
        _ => severity.ToString()
    };
}
