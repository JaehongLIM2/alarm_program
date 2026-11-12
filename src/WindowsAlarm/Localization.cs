using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using WindowsAlarm.Services;

namespace WindowsAlarm;

// Translate at the presentation boundary: persisted schedules and service messages stay unchanged.
public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Current { get; } = new();
    private readonly Dictionary<string, string> _translations = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _sources = [];
    private readonly List<(Regex Pattern, string Translation)> _templates = [];
    public string Language { get; private set; } = "KR";
    public string SwitchLabel => Language == "JP" ? "JP → KR" : "KR → JP";
    public FontFamily Font => new(Language == "JP" ? "Yu Gothic UI, Meiryo" : "pack://application:,,,/WindowsAlarm;component/Assets/Fonts/#NanumSquareOTF");
    public event PropertyChangedEventHandler? PropertyChanged;

    private Localization()
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("WindowsAlarm.Assets.Translations.tsv")!;
        using var reader = new StreamReader(stream);
        // TSV columns: stable numeric key, Korean source, Japanese translation. Never renumber existing keys.
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            var source = parts[1].Replace("\\n", "\n");
            var translation = parts[2].Replace("\\n", "\n");
            _sources.Add(int.Parse(parts[0], CultureInfo.InvariantCulture), source);
            _translations.Add(source, translation);
            if (!source.Contains("{0}")) continue;
            var pattern = Regex.Escape(source);
            for (var i = 0; i < 10; i++)
                pattern = pattern.Replace(Regex.Escape("{" + i + "}"), "(?<p" + i + ">.*?)");
            _templates.Add((new Regex("\\A" + pattern + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)), translation));
        }
        _templates.Sort((a, b) => b.Pattern.ToString().Length.CompareTo(a.Pattern.ToString().Length));
    }

    public void Load(AppStore store) => SetLanguage(store.Load<LanguagePreference>("language.json")?.Language);
    public void Toggle(AppStore store)
    {
        var next = Language == "JP" ? "KR" : "JP";
        // Commit first so a failed save leaves both the screen and preference unchanged.
        store.Save("language.json", new LanguagePreference(next));
        SetLanguage(next);
    }
    internal void SetLanguage(string? language)
    {
        Language = language == "JP" ? "JP" : "KR";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    internal string Source(int key) => _sources[key];
    public string? Translate(string? source)
    {
        if (source is null || Language != "JP") return source;
        if (_translations.TryGetValue(source, out var translated)) return translated;
        foreach (var (pattern, translation) in _templates)
        {
            var match = pattern.Match(source);
            if (!match.Success) continue;
            return Regex.Replace(translation, @"\{(\d)\}", m =>
            {
                var value = match.Groups["p" + m.Groups[1].Value].Value;
                return value == source ? value : Translate(value)!;
            });
        }
        return source; // External OS errors and user-provided paths are left intact.
    }
    public static void SetText(TextBlock target, string? source) => Bind(target, TextBlock.TextProperty, source);
    public static void Bind(DependencyObject target, DependencyProperty property, string? source) =>
        BindingOperations.SetBinding(target, property, LocExtension.CreateBinding(new Binding { Source = source ?? "" }));
}

public sealed record LanguagePreference(string Language = "KR");

// A second binding to Language updates existing text, templates and open windows immediately.
public sealed class LocExtension : MarkupExtension
{
    public int Key { get; set; } = -1;
    public string? Path { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        CreateBinding(Path is not null ? new Binding(Path) : new Binding { Source = Localization.Current.Source(Key) }).ProvideValue(serviceProvider);
    internal static MultiBinding CreateBinding(Binding text)
    {
        var binding = new MultiBinding { Converter = TranslationConverter.Instance, Mode = BindingMode.OneWay };
        binding.Bindings.Add(text);
        binding.Bindings.Add(new Binding(nameof(Localization.Language)) { Source = Localization.Current });
        return binding;
    }
    private sealed class TranslationConverter : IMultiValueConverter
    {
        internal static readonly TranslationConverter Instance = new();
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            Localization.Current.Translate(values[0] as string) ?? "";
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
