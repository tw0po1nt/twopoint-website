using System;
using System.Collections;

IEnumerable<DateTime> noDateTimes = [];
IEnumerable<DateTime> oneDateTime = [new DateTime(1969, 07, 20)];
IEnumerable<DateTime> manyDateTimes = [
  new DateTime(1969, 07, 20),
  new DateTime(1995, 08, 24),
  new DateTime(2007, 06, 29),
  new DateTime(1995, 08, 24)
];

// TryFirst
// Output: Just "1/1/0001 12:00:00 AM"
Console.WriteLine(noDateTimes.TryFirstValue());

// Output: Just "7/20/1969 12:00:00 AM"
Console.WriteLine(oneDateTime.TryFirstValue());

// Output: Just "1/1/0001 12:00:00 AM"
Console.WriteLine(oneDateTime.TryFirstValue(dt => dt == new DateTime(2026, 12, 31))); 

// Output: Just "7/20/1969 12:00:00 AM"
Console.WriteLine(manyDateTimes.TryFirstValue());

// Output: Just "8/24/1995 12:00:00 AM"
Console.WriteLine(manyDateTimes.TryFirstValue(dt => dt == new DateTime(1995, 08, 24)));

// TrySingle
// Output: Just "1/1/0001 12:00:00 AM"
Console.WriteLine(noDateTimes.TrySingleValue());

// Output: Just "7/20/1969 12:00:00 AM"
Console.WriteLine(oneDateTime.TrySingleValue());

// Output: Just "1/1/0001 12:00:00 AM"
Console.WriteLine(oneDateTime.TrySingleValue(dt => dt == new DateTime(2026, 12, 31))); 

// Output: Nothing 
Console.WriteLine(manyDateTimes.TrySingleValue());

// Output: Nothing 
Console.WriteLine(manyDateTimes.TrySingleValue(dt => dt == new DateTime(1995, 08, 24)));


// TYPES
public readonly struct NothingMaybe;

public readonly struct Maybe<T>
{
    private readonly T _value;
    private readonly bool _hasValue;

    private Maybe(T value, bool hasValue)
    {
        _value = value;
        _hasValue = hasValue;
    }

    internal Maybe(T value) : this(value, true) {}

    public TOut Match<TOut>(Func<TOut> onNothing, Func<T, TOut> onJust)
        => _hasValue ? onJust(_value) : onNothing();

    public static implicit operator Maybe<T>(NothingMaybe _) => new();

    public override string ToString() => Match(
        () => "Nothing",
        just => $"Just \"{just}\""
    );
}

public static class Maybe 
{
    public static NothingMaybe Nothing => new();

    public static Maybe<T> Just<T>(T value) => new(value);
    
    public static Maybe<T> Of<T>(T? value) where T : class => value is null
        ? new NothingMaybe()
        : Just(value);

    public static Maybe<T> Of<T>(Nullable<T> value) where T : struct => value is null
        ? new NothingMaybe()
        : Just(value.Value);
    
    public static T? OrNull<T>(this Maybe<T> value) where T : class => value.Match<T?>(
        () => default(T?),
        value => value
    );

    public static Nullable<T> OrNullable<T>(this Maybe<T> value) where T : struct => value.Match<Nullable<T>>(
        () => default(Nullable<T>),
        value => value
    );
}

public static class EnumerableExtensions
{
    public static Maybe<T> TryFirst<T>(this IEnumerable<T> xs) where T : class
        => Maybe.Of(xs.FirstOrDefault());

    public static Maybe<T> TryFirst<T>(this IEnumerable<T> xs, Func<T, bool> predicate) where T : class
        => Maybe.Of(xs.FirstOrDefault(predicate));

    public static Maybe<T> TryFirstValue<T>(this IEnumerable<T> xs) where T : struct
    {
        var x = xs.FirstOrDefault();
        return x.Equals(default(T)) ? Maybe.Nothing : Maybe.Just(x);
    }

    public static Maybe<T> TryFirstValue<T>(this IEnumerable<T> xs, Func<T, bool> predicate) where T : struct
    {
        var x = xs.FirstOrDefault(predicate);
        return x.Equals(default(T)) ? Maybe.Nothing : Maybe.Just(x);
    }

    public static Maybe<T> TrySingle<T>(this IEnumerable<T> xs) where T : class
        => xs.Count() > 1 ? Maybe.Nothing : Maybe.Of(xs.SingleOrDefault());

    public static Maybe<T> TrySingle<T>(this IEnumerable<T> xs, Func<T, bool> predicate) where T : class
        => xs.Count(predicate) > 1 ? Maybe.Nothing : Maybe.Of(xs.SingleOrDefault(predicate));

    public static Maybe<T> TrySingleValue<T>(this IEnumerable<T> xs) where T : struct
    {
        if (xs.Count() > 1) return Maybe.Nothing;
        var x = xs.SingleOrDefault();
        return x.Equals(default(T)) ? Maybe.Nothing : Maybe.Just(x); 
    }

    public static Maybe<T> TrySingleValue<T>(this IEnumerable<T> xs, Func<T, bool> predicate) where T : struct
    {
        if (xs.Count(predicate) > 1) return Maybe.Nothing;
        var x = xs.SingleOrDefault(predicate);
        return x.Equals(default(T)) ? Maybe.Nothing : Maybe.Just(x); 
    }
}