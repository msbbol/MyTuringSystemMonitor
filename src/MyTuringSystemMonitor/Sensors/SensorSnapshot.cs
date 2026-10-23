namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// An immutable set of sensor readings taken at a single instant. The rendering layer
/// consumes plain <see cref="double"/> values keyed by <see cref="SensorKeys"/>; a value
/// is <c>null</c> (or simply absent) when the reading is unavailable. A parallel text channel
/// carries string readings (e.g. the busiest process name).
/// </summary>
public sealed class SensorSnapshot
{
    private static readonly IReadOnlyDictionary<string, string?> NoTexts = new Dictionary<string, string?>();

    private readonly IReadOnlyDictionary<string, double?> _values;
    private readonly IReadOnlyDictionary<string, string?> _texts;

    public SensorSnapshot(
        IReadOnlyDictionary<string, double?> values,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, string?>? texts = null)
    {
        _values = values;
        _texts = texts ?? NoTexts;
        Timestamp = timestamp;
    }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyDictionary<string, double?> Values => _values;

    public IReadOnlyDictionary<string, string?> Texts => _texts;

    /// <summary>Returns the numeric value for <paramref name="key"/>, or null if missing/unavailable.</summary>
    public double? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>Returns the text value for <paramref name="key"/>, or null if missing.</summary>
    public string? GetText(string key) => _texts.TryGetValue(key, out var v) ? v : null;

    public static SensorSnapshot Empty { get; } =
        new(new Dictionary<string, double?>(), DateTimeOffset.MinValue);

    /// <summary>Small mutable helper used by providers to accumulate readings.</summary>
    public sealed class Builder
    {
        private readonly Dictionary<string, double?> _values = new();
        private Dictionary<string, string?>? _texts;

        public Builder Set(string key, double? value)
        {
            _values[key] = value;
            return this;
        }

        public Builder SetText(string key, string? value)
        {
            (_texts ??= new Dictionary<string, string?>())[key] = value;
            return this;
        }

        public SensorSnapshot Build() => new(_values, DateTimeOffset.Now, _texts);
    }
}
