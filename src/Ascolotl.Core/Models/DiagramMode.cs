namespace Ascolotl.Core.Models;

/// <summary>How diagram blocks ([mermaid], [plantuml], [graphviz]...) are drawn.</summary>
public enum DiagramMode
{
    /// <summary>Not drawn: they stay as code. Nothing runs, nothing leaves the computer.</summary>
    Off,

    /// <summary>With the programs installed on this computer (Graphviz, PlantUML, Mermaid CLI).</summary>
    LocalTools,

    /// <summary>By a Kroki web service, which receives each diagram's source.</summary>
    Kroki,
}
