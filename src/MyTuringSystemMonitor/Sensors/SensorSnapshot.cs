namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// An immutable set of sensor readings taken at a single instant. The rendering layer
/// consumes plain <see cref="double"/> values keyed by <see cref="SensorKeys"/>; a value
/// is <c>null</c> (or simply absent) when the reading is unavailable.
/// </summary>
public sealed class SensorSnapshot
{
    private readonly IReadOnlyDictionary<string, double?> _values;

    public SensorSnapshot(IReadOnlyDictionary<string, double?> values, DateTimeOffset timestamp)
    {
        _values = values;
        Timestamp = timestamp;
    }

    public DateTimeOffset Timestamp { get; }

    public IReadOnlyDictionary<string, double?> Values => _values;

    /// <summary>Returns the value for <paramref name="key"/>, or null if missing/unavailable.</summary>
    public double? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public static SensorSnapshot Empty { get; } =
        new(new Dictionary<string, double?>(), DateTimeOffset.MinValue);

    /// <summary>Small mutable helper used by providers to accumulate readings.</summary>
    public sealed class Builder
    {
        private readonly Dictionary<string, double?> _values = new();

        public Builder Set(string key, double? value)
        {
            _values[key] = value;
            return this;
        }

        public SensorSnapshot Build() => new(_values, DateTimeOffset.Now);
    }
}
