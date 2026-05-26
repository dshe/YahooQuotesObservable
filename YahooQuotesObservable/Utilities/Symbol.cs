using System.Globalization;
namespace YahooQuotesObservable;

public readonly struct Symbol : IEquatable<Symbol>, IComparable<Symbol>
{
    // 2 states: uninitialized or invalid(default, name is null), ok(name is not null)
    // The default value of a struct is the value produced when all of its fields equal their default values.
    // This struct has only the string field 'name'.
    // String is a reference type with default value of null.
    private readonly string? name;
    private Symbol(string name) => this.name = name;

    public string Name
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name, nameof(name));
            return name;
        }
    }

    public string Suffix
    {
        get
        {
            if (name is null)
                return "";
            int pos = Name.IndexOf('.', StringComparison.Ordinal);
            if (pos == -1 || pos == name.Length - 1)
                return "";
            return Name[(pos + 1)..];
        }
    }

    public bool IsValid => name is not null;
    public bool IsCurrency => name is not null && name.Length == 5 && name.EndsWith("=X", StringComparison.Ordinal);
    public bool IsCurrencyRate => name is not null && name.Length == 8 && name.EndsWith("=X", StringComparison.Ordinal);
    public bool IsStock => name is not null && !name.EndsWith("=X", StringComparison.Ordinal);

    public string Currency
    {
        get
        {
            if (IsCurrency)
                return Name[..3];
            if (IsCurrencyRate)
                return Name[3..6];
            throw new InvalidOperationException("Symbol is neither currency nor currency rate.");
        }
    }

    public override string ToString() => name ?? "<invalid symbol>";
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(name ?? "");
    public bool Equals(Symbol other) => string.Equals(name, other.name, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is Symbol symbol && Equals(symbol);
    public int CompareTo(Symbol other) => string.CompareOrdinal(name, other.name);
    public static bool operator ==(Symbol left, Symbol right) => left.Equals(right);
    public static bool operator !=(Symbol left, Symbol right) => !(left == right);
    public static bool operator <(Symbol left, Symbol right) => left.CompareTo(right) < 0;
    public static bool operator <=(Symbol left, Symbol right) => left.CompareTo(right) <= 0;
    public static bool operator >(Symbol left, Symbol right) => left.CompareTo(right) > 0;
    public static bool operator >=(Symbol left, Symbol right) => left.CompareTo(right) >= 0;

    public static Symbol Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (TryCreate(name, out Symbol symbol))
            return symbol;
        throw new ArgumentException($"Could not convert '{name}' to Symbol.");
    }

    public static bool TryCreate(string name, out Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(name);
        symbol = default;
        if (name.Length == 0)
            return false;

        bool hasDot = false, hasEq = false;
        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c))
                return false;
            if (c == '.')
            {
                if (hasDot)
                    return false;
                hasDot = true;
            }
            if (c == '=')
            {
                if (hasEq)
                    return false;
                hasEq = true;
            }
        }

        name = name.ToUpper(CultureInfo.InvariantCulture);
        if (name.Contains("=X", StringComparison.Ordinal))
        {
            if (!name.EndsWith("=X", StringComparison.Ordinal)
                || (name.Length != 5 && name.Length != 8)
                || (name.Length == 8 && string.Equals(name[0..3], name[3..6], StringComparison.Ordinal)))
                return false;
        }
        symbol = new Symbol(name);
        return true;
    }
}
