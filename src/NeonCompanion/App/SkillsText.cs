using System.Globalization;
using NeonCompanion.Llm;
using NeonCompanion.Skills;
using NeonCompanion.UI;
using Spectre.Console;

namespace NeonCompanion.App;

/// <summary>What <c>/skills</c> shows, read when it opens: the skills switch, the <c>Project file</c> toggle, the catalog as of its last scan, the roots (for the scope page), and the project notes as they stand on disk (read while the skills are on, whatever the toggle says).</summary>
public sealed record SkillsFacts(
    bool Enabled,
    bool ProjectFile,
    IReadOnlyList<Skill> Skills,
    IReadOnlyList<Skill> Shadowed,
    IReadOnlyList<SkillProblem> Problems,
    SkillRoots Roots,
    ProjectNotes? Project);

/// <summary>
/// The words for <c>/skills</c>: the pane's tabs (an Offered tab — the catalog, what is
/// shadowed and what was skipped — and a Project tab — the <c>Project file</c> toggle with which of
/// <c>NEON.md</c> / <c>AGENTS.md</c> the working directory holds; the Roots tab, the three folders in
/// precedence order, went later on 2026-09-19 at the user's call — the scope page names each root's
/// folder), the same as plain lines for a console without the pane; the Options and Reflection tabs
/// between Offered and Project (2026-09-19, the settings rows that were <c>/settings</c>' Skills tab)
/// are <see cref="SettingsMenu"/>'s — <see cref="SettingsMenu.SkillsTabFields"/> — and only their
/// titles live here. Pure statics, every string pinned; the <see cref="AboutText"/> shape. Every
/// user-written or path string is escaped: a description is the skill author's.
///
/// <para>Since 2026-09-18 the pane is a <see cref="MenuPane"/> (<see cref="SkillsMenu"/>): each tab's
/// content is a list of markup rows — <see cref="LoadedRows"/> carries the <see cref="Skill"/> a row
/// stands for, so Enter on it opens the scope picker — cut at the edge by the menu, never wrapped;
/// the blank separators of <see cref="LoadedLines"/> are left out, a menu row being a cursor stop.
/// The plain lines are uncut: the console wraps them.</para>
/// </summary>
public static class SkillsText
{
    /// <summary>The pane's strip label.</summary>
    public const string Label = "Skills";

    /// <summary>The first tab: <c>Offered</c> since 2026-09-19 (the user's call, the same word as <c>/tools</c>' first tab; <c>Loaded</c> before), not the pane's own word again.</summary>
    public const string OfferedTabTitle = "Offered";

    /// <summary>The second tab since 2026-09-19 (the user's ask): the skill settings, <c>/settings</c>' Skills tab until then (<see cref="SettingsMenu.SkillsTabFields"/>).</summary>
    public const string OptionsTabTitle = "Options";

    /// <summary>The third tab since later on 2026-09-19 (the user's ask): the reflection's rows, the Options tab's tail until then (<see cref="SettingsMenu.SkillsTabFields"/>'s second list).</summary>
    public const string ReflectionTabTitle = "Reflection";
    public const string ProjectTabTitle = "Project";

    /// <summary>The Project tab's hint row: Enter or Space flips the one row (later on 2026-09-19), the <c>/tools</c> Offered tab's words. Pinned.</summary>
    public const string ProjectKeys = ToolsText.OfferedKeys;

    /// <summary>The first line of every tab while the setting is off. Pinned.</summary>
    public const string OffLine = "Agent skills is off (the Options tab of /skills): no skill is listed, no skill tool offered, and the project notes are not read.";

    public const string NoneLine = "(no skill installed: a folder with a SKILL.md under one of the roots, or ask the model to write one)";
    public const string ShadowedHeading = "Shadowed (a higher root holds the name):";
    public const string ProblemsHeading = "Skipped:";

    /// <summary>The Project tab's one row (the <c>Working directory</c> row above it went later on 2026-09-19): the label of the <c>Project file</c> toggle.</summary>
    public const string NotesLabel = "Project file";

    /// <summary>The Project tab's Notes value when neither file is there (the user's wording, 2026-09-16). Pinned.</summary>
    public static readonly string NoNotesLine = "none (" + ProjectFile.PrimaryFileName + " or " + ProjectFile.SecondaryFileName + " in the working directory)";

    /// <summary><c>NEON.md (1,234 characters)</c>.</summary>
    public static string NotesLine(ProjectNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.FileName + " (" + notes.Text.Length.ToString("N0", CultureInfo.InvariantCulture) + " characters)";
    }

    /// <summary>One catalog row: the name padded to the column, the scope padded to nine, the description. Pinned.</summary>
    public static string SkillLine(Skill skill, int nameWidth)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill.Name.PadRight(nameWidth) + "  " + SkillScopes.Name(skill.Scope).PadRight(9) + skill.Description;
    }

    /// <summary>A shadowed skill's row: the name, its scope, the scope that hides it. Pinned.</summary>
    public static string ShadowedLine(Skill skill, int nameWidth)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill.Name.PadRight(nameWidth) + "  " + SkillScopes.Name(skill.Scope).PadRight(9) + "shadowed by the " + SkillScopes.Name(skill.ShadowedBy ?? SkillScope.Profile) + " skills";
    }

    /// <summary>A skipped folder's row: the folder's path and the reason. Pinned.</summary>
    public static string ProblemLine(SkillProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem.Directory + ": " + problem.Reason;
    }

    /// <summary>The name column: the longest name among the listed and the shadowed, at least four.</summary>
    public static int NameWidth(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Math.Max(4, facts.Skills.Concat(facts.Shadowed).Select(s => s.Name.Length).DefaultIfEmpty(0).Max());
    }

    /// <summary>
    /// The Loaded tab as lines: the catalog (with a warning under a skill that has one), then the
    /// shadowed and the skipped as blocks under their headings, a blank line between blocks and none
    /// after the last; <see cref="OffLine"/> or <see cref="NoneLine"/> alone when that is the case.
    /// </summary>
    public static IReadOnlyList<string> LoadedLines(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Enabled)
        {
            return [OffLine];
        }

        if (facts.Skills.Count == 0)
        {
            return [NoneLine];
        }

        var lines = new List<string>();
        int width = NameWidth(facts);
        foreach (var skill in facts.Skills)
        {
            lines.Add(SkillLine(skill, width));
            if (skill.Warning is { } warning)
            {
                lines.Add(new string(' ', width + 2) + "(" + warning + ")");
            }
        }

        if (facts.Shadowed.Count > 0)
        {
            lines.Add("");
            lines.Add(ShadowedHeading);
            foreach (var skill in facts.Shadowed)
            {
                lines.Add("  " + ShadowedLine(skill, width));
            }
        }

        if (facts.Problems.Count > 0)
        {
            lines.Add("");
            lines.Add(ProblemsHeading);
            foreach (var problem in facts.Problems)
            {
                lines.Add("  " + ProblemLine(problem));
            }
        }

        return lines;
    }

    /// <summary>
    /// The Project tab's one row (later on 2026-09-19; two rows, the working directory over it, from
    /// 2026-09-16 until then): the <c>Project file</c> label, the toggle's state, and which notes file
    /// the working directory holds (<see cref="NoNotesLine"/> when none, or while the skills are off)
    /// — what is on disk, whatever the toggle says. Pinned.
    /// </summary>
    public static (string Label, bool On, string Value) ProjectRow(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return (NotesLabel, facts.ProjectFile, facts.Enabled && facts.Project is { } notes ? NotesLine(notes) : NoNotesLine);
    }

    /// <summary>The row's index on the Project tab: past <see cref="OffLine"/> while the skills are off — where the cursor opens.</summary>
    public static int ProjectRowIndex(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Enabled ? 0 : 1;
    }

    /// <summary>The status line after a flip of the toggle: <c>Project file: off</c>, the <see cref="ToolsText.FlippedNotice"/> shape. Pinned.</summary>
    public static string ProjectFlippedNotice(bool on) => ToolsText.FlippedNotice(NotesLabel, on);

    /// <summary>The Project tab as lines: <see cref="OffLine"/> first while the setting is off, then <see cref="ProjectRow"/> in the pane's columns — <c>Project file  on   NEON.md (1,234 characters)</c>.</summary>
    public static IReadOnlyList<string> ProjectLines(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var lines = new List<string>(2);
        if (!facts.Enabled)
        {
            lines.Add(OffLine);
        }

        var (label, on, value) = ProjectRow(facts);
        lines.Add(label.PadRight(ProjectLabelWidth) + ToolsText.State(on).PadRight(ToolsText.StateWidth) + value);
        return lines;
    }

    /// <summary>The Project tab's label column on the pane: the label plus the help gap (past <c>Working directory</c> until later on 2026-09-19, when that row went).</summary>
    public static readonly int ProjectLabelWidth = NotesLabel.Length + SlashCommands.HelpColumnGap;

    /// <summary>
    /// The Loaded tab as menu rows (2026-09-18): <see cref="LoadedLines"/>' content without its blank
    /// separators, each as markup — a catalog row with the name in the label colour, the scope and
    /// the description escaped after it, the <see cref="Skill"/> it stands for beside it; its warning
    /// dim under it; the two headings in the label colour; a shadowed row dim with its skill beside
    /// it (its scope is pickable too: the duplicate is the thing to clean up); a skipped folder dim;
    /// <see cref="OffLine"/> or <see cref="NoneLine"/> dim and alone. Null beside every row that is
    /// not a skill's.
    /// </summary>
    public static IReadOnlyList<(string Markup, Skill? Skill)> LoadedRows(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Enabled)
        {
            return [(Theme.DimMarkup(OffLine), null)];
        }

        if (facts.Skills.Count == 0)
        {
            return [(Theme.DimMarkup(NoneLine), null)];
        }

        var rows = new List<(string, Skill?)>();
        int width = NameWidth(facts);
        foreach (var skill in facts.Skills)
        {
            rows.Add((Styled(Theme.AccentCyan, skill.Name.PadRight(width)) + Markup.Escape("  " + SkillScopes.Name(skill.Scope).PadRight(9) + skill.Description), skill));
            if (skill.Warning is { } warning)
            {
                rows.Add((Theme.DimMarkup(new string(' ', width + 2) + "(" + warning + ")"), null));
            }
        }

        if (facts.Shadowed.Count > 0)
        {
            rows.Add((Styled(Theme.AccentCyan, ShadowedHeading), null));
            foreach (var skill in facts.Shadowed)
            {
                rows.Add((Theme.DimMarkup("  " + ShadowedLine(skill, width)), skill));
            }
        }

        if (facts.Problems.Count > 0)
        {
            rows.Add((Styled(Theme.AccentCyan, ProblemsHeading), null));
            foreach (var problem in facts.Problems)
            {
                rows.Add((Theme.DimMarkup("  " + ProblemLine(problem)), null));
            }
        }

        return rows;
    }

    /// <summary>
    /// The Project tab as menu rows (2026-09-18; the toggle since later on 2026-09-19): <see cref="OffLine"/>
    /// dim first while the skills are off, then <see cref="ProjectRow"/> in the <c>/tools</c> Offered
    /// tab's shape — the label in the label colour padded to <see cref="ProjectLabelWidth"/>, <c>on</c> /
    /// <c>off</c> in the ink padded to <see cref="ToolsText.StateWidth"/>, the value dim — the whole row
    /// dim while the skills are off (the toggle still flips and saves).
    /// </summary>
    public static IReadOnlyList<string> ProjectRowsMarkup(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var rows = new List<string>(2);
        if (!facts.Enabled)
        {
            rows.Add(Theme.DimMarkup(OffLine));
        }

        var (label, on, value) = ProjectRow(facts);
        string state = ToolsText.State(on).PadRight(ToolsText.StateWidth);
        rows.Add(facts.Enabled
            ? Styled(Theme.AccentCyan, label.PadRight(ProjectLabelWidth)) + Theme.ColorMarkup(Theme.Ink, state) + Theme.DimMarkup(value)
            : Theme.DimMarkup(label.PadRight(ProjectLabelWidth) + state + value));
        return rows;
    }

    private static string Styled(Style style, string text) => $"[{style.ToMarkup()}]{Markup.Escape(text)}[/]";

    /// <summary>The two content tabs as plain lines, for a console without the pane: each tab's title as a heading, its content indented; <see cref="SkillsMenu.Lines"/> splices the Options and Reflection tabs in between.</summary>
    public static IEnumerable<string> Lines(SkillsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        foreach (var (title, lines) in new[] { (OfferedTabTitle, LoadedLines(facts)), (ProjectTabTitle, ProjectLines(facts)) })
        {
            yield return title;
            foreach (var line in lines)
            {
                yield return "  " + line;
            }
        }
    }
}
