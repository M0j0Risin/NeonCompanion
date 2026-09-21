namespace NeonCompanion.App;

/// <summary>
/// The verbs the turn spinner reads instead of <see cref="ChatScreen.ThinkingLabel"/> and
/// <see cref="TurnStages.WritingLabel"/> while <see cref="Settings.AppSettingsData.LlmUseFunVerbs"/>
/// is on: one is picked for every thinking and writing stage (<see cref="TurnStages"/>; a running
/// tool keeps its name). Lowercase like the other spinner labels; the words are the user's (the list of
/// 2026-09-15, replacing the first one whole), in the user's order, pinned.
/// </summary>
internal static class ThinkingVerbs
{
    public static readonly string[] All =
    [
        "bellowing",
        "bellyflopping",
        "belching",
        "blabbing",
        "blarneying",
        "blinking",
        "blotting",
        "blundering",
        "boinging",
        "boogering",
        "boogeying",
        "boondoggling",
        "boozing",
        "buckling",
        "burbling",
        "buzzing",
        "cackling",
        "cavorting",
        "chortling",
        "chuckling",
        "chuffing",
        "clacking",
        "clucking",
        "cooching",
        "crooning",
        "cracking",
        "crinkling",
        "doodling",
        "dribbling",
        "flabbergasting",
        "fidgeting",
        "fizzing",
        "fizzling",
        "flibberting",
        "floundering",
        "fobbing",
        "flossing",
        "frolicking",
        "frittering",
        "fudging",
        "fumbling",
        "fuzzing",
        "gadding",
        "giggling",
        "giddying",
        "gnashing",
        "gobbling",
        "grinning",
        "grinding",
        "grunting",
        "gurgling",
        "growling",
        "hiccuping",
        "hickering",
        "huffing",
        "howling",
        "humping",
        "jiggling",
        "jigging",
        "jittering",
        "jingling",
        "jostling",
        "jowling",
        "jiving",
        "jazzing",
        "knickering",
        "kooking",
        "laughing",
        "mawkering",
        "mumbling",
        "muttering",
        "noodling",
        "ooohing",
        "peeping",
        "panting",
        "pattering",
        "piffing",
        "pooching",
        "puffing",
        "primping",
        "purling",
        "quivering",
        "roaring",
        "scampering",
        "scraping",
        "scratching",
        "scurrying",
        "shimmying",
        "shuffling",
        "shrieking",
        "skittering",
        "slobbering",
        "snickering",
        "snodging",
        "snoodling",
        "snorking",
        "snorkeling",
        "snorting",
        "snuffing",
        "snotling",
        "snotting",
        "sputtering",
        "splashing",
        "splintering",
        "squealing",
        "squeezing",
        "stammering",
        "stuttering",
        "tinkling",
        "trembling",
        "twinkling",
        "waddling",
        "waltzing",
        "wiggling",
        "wobbling",
        "wriggling",
        "whooping",
        "whooshing",
        "yipping",
        "yodeling",
        "yowling",
        "zipping",
    ];

    /// <summary>One of <see cref="All"/>, drawn from <paramref name="random"/>.</summary>
    public static string Pick(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return All[random.Next(All.Length)];
    }

    /// <summary>
    /// <see cref="Pick(Random)"/> but never <paramref name="except"/> (the verb shown just before —
    /// a stage change under the fun verbs reads as one only when the word changes): drawn again
    /// while it matches. Null excludes nothing.
    /// </summary>
    public static string Pick(Random random, string? except)
    {
        string verb = Pick(random);
        while (string.Equals(verb, except, StringComparison.Ordinal))
        {
            verb = Pick(random);
        }

        return verb;
    }
}
