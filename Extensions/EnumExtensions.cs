using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace KamiLib.Extensions;

public static class EnumExtensions {

    public static Func<ResourceManager?>? GetResourceManagerFunc { get; set; }
    
    public static Func<CultureInfo?>? GetCultureInfoFunc { get; set; }
    
    // 描述與後備名稱只看列舉值本身，查一次就記住；翻譯每次照查（語言可以在執行期切換）。
    // 不在列舉定義裡的值（例如旗標組合）不記，免得表無限長大。
    private readonly record struct Entry(string Text, bool Localize);

    private static readonly ConcurrentDictionary<Enum, Entry> BoxedEntries = new();

    private static class TypedEntries<T> where T : struct, Enum {
        public static readonly ConcurrentDictionary<T, Entry> Map = new();
    }

    public static string GetDescription(this Enum value) {
        if (!BoxedEntries.TryGetValue(value, out var entry)) {
            if (!TryResolve(value, out entry)) return entry.Text;
            BoxedEntries.TryAdd(value, entry);
        }

        return Localize(entry);
    }

    /// <summary>同 <see cref="GetDescription(Enum)"/>；呼叫端拿著具體列舉型別時走這裡，查表不必裝箱。</summary>
    public static string GetDescription<T>(this T value) where T : struct, Enum {
        if (!TypedEntries<T>.Map.TryGetValue(value, out var entry)) {
            if (!TryResolve(value, out entry)) return entry.Text;
            TypedEntries<T>.Map.TryAdd(value, entry);
        }

        return Localize(entry);
    }

    private static bool TryResolve(Enum value, out Entry entry) {
        var type = value.GetType();
        if (Enum.GetName(type, value) is not { } name) {
            entry = new Entry(value.ToString(), false);
            return false;
        }

        entry = type.GetField(name) is { } field && Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr
            ? new Entry(attr.Description, true)
            : new Entry(value.ToString(), false);

        return true;
    }

    private static string Localize(Entry entry) {
        if (!entry.Localize) return entry.Text;

        if (GetResourceManagerFunc?.Invoke() is { } resourceManager && GetCultureInfoFunc?.Invoke() is { } cultureInfo) {
            if (resourceManager.GetString(entry.Text, cultureInfo) is { } localizedString) {
                return localizedString;
            }
        }

        return entry.Text;
    }
}