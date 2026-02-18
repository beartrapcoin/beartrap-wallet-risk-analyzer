namespace BearTrap.Hackathon.Infrastructure.Caching;

/// <summary>
/// Wrapper class for caching boolean values.
/// Required because IChainDataCache uses generic constraint <T> where T : class,
/// and bool is a value type. This allows boolean results to be cached.
/// </summary>
public sealed class CachedBool
{
    public bool Value { get; }

    public CachedBool(bool value)
    {
        Value = value;
    }

    public static implicit operator bool(CachedBool cached) => cached.Value;
    public static implicit operator CachedBool(bool value) => new(value);

    public override string ToString() => Value.ToString();
    public override bool Equals(object? obj) => obj is CachedBool cb && cb.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
}
