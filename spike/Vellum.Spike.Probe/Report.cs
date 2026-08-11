using System.Text;

namespace Vellum.Spike.Probe;

/// <summary>Accumulates probe output to console and to a results file.</summary>
public sealed class Report
{
    private readonly StringBuilder _sb = new();
    private int _failures;

    public Report(string title)
    {
        Line($"# {title}");
        Line($"_generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}_");
    }

    public int Failures => _failures;

    public void Section(string name)
    {
        Line("");
        Line($"## {name}");
        Line("");
    }

    public void Sub(string name)
    {
        Line("");
        Line($"### {name}");
        Line("");
    }

    public void Line(string text = "")
    {
        _sb.AppendLine(text);
        Console.WriteLine(text);
    }

    public void Table(string header, params string[] rows)
    {
        var cols = header.Split('|').Length;
        Line("| " + header + " |");
        Line("|" + string.Concat(Enumerable.Repeat("---|", cols)));
        foreach (var r in rows) Line("| " + r + " |");
    }

    /// <summary>Records an explicit expectation. Failures are surfaced, never swallowed.</summary>
    public bool Check(bool condition, string what, string detail = "")
    {
        if (!condition) _failures++;
        var mark = condition ? "PASS" : "**FAIL**";
        Line($"- {mark} — {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        return condition;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, _sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"\n[written] {path}");
    }
}
