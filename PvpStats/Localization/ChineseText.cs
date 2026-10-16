using System;

namespace PvpStats.Localization;

/// <summary>Display translations only. Never use these values as game or database identifiers.</summary>
public static partial class ChineseText {
    public static string Text(string original) =>
        Texts.TryGetValue(original, out var translated) ? translated : original;

    public static string EnumName<T>(T value) where T : struct, Enum =>
        EnumNames.TryGetValue($"{typeof(T).Name}.{value}", out var translated) ? translated : value.ToString();

    public static string EnumName<T>(T? value) where T : struct, Enum =>
        value.HasValue ? EnumName(value.Value) : "";

    public static string ActionName(uint id, string fallback) =>
        Actions.TryGetValue(id, out var translated) ? translated : fallback;

    public static string StatusName(uint id, string fallback) =>
        Statuses.TryGetValue(id, out var translated) ? translated : fallback;

    /// <summary>Translate visible text using an untranslated, stable ImGui identifier.</summary>
    public static string Label(string original) {
        var separator = original.IndexOf("##", StringComparison.Ordinal);
        var visible = separator < 0 ? original : original[..separator];
        var translated = Text(visible);
        if(translated == visible) return original;
        var stableId = original.LastIndexOf("###", StringComparison.Ordinal);
        // Preserve existing ### suffixes. Plain labels receive stable IDs based on their original text.
        return translated + "###" + (stableId < 0 ? original : original[(stableId + 3)..]);
    }

    /// <summary>Enums stay unchanged in configuration while their labels use Chinese names.</summary>
    public static string EnumLabel<T>(T value, string originalLabel) where T : struct, Enum {
        var originalId = originalLabel.LastIndexOf("###", StringComparison.Ordinal);
        return EnumName(value) + "###" + (originalId < 0 ? originalLabel : originalLabel[(originalId + 3)..]);
    }
}
