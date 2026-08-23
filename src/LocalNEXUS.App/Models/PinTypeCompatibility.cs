namespace LocalNEXUS.App.Models;

/// <summary>
/// Decides which pin types may flow into which.
/// </summary>
/// <remarks>
/// The rule is equality. Types match or they do not connect, and there is no exception to it.
/// <para>
/// There used to be one: a <see cref="PinType.Code"/> output could feed a <see cref="PinType.Text"/>
/// input, because a model node takes Text and emits Code and otherwise could only ever be fed by a
/// prompt. What it produced in practice was a Code pin wired into everything that reads prose, so
/// the pin meaning "a file was produced" was also the pin somebody drew from when they wanted an
/// answer, and a model asked a question replied with a class. The model node grew its own Text
/// output instead, which says the same thing honestly: it has two things to offer and they are
/// different, rather than one that can be read as either.
/// </para>
/// <para>
/// New pin types are added to <see cref="PinType"/> and, if they need to interoperate with an
/// existing type, given a rule here. Nothing else consults the type system.
/// </para>
/// </remarks>
public static class PinTypeCompatibility
{
    /// <summary>True when a value produced as <paramref name="source"/> may be consumed as <paramref name="target"/>.</summary>
    public static bool CanFlow(PinType source, PinType target)
    {
        // A model is a reference to something that can be asked a question, not a value that can
        // be read. It joins only to itself, and it says so before the exception below rather than
        // relying on equality to cover it, so that a future exception cannot quietly widen to
        // include it. Coercing a model to text would turn a wire that means "use this model" into
        // one that means "paste this model's name", and the second one compiles.
        if (source is PinType.Model || target is PinType.Model)
        {
            return source == target;
        }

        // Types match, and there is no longer an exception. Code used to be allowed into Text, so
        // the pin that means "a file was produced" was also the pin somebody drew from when they
        // wanted prose, and a node that produced both had no way to say which was which. A model
        // that has something to say now says it on a Text pin.
        return source == target;
    }

    /// <summary>A short explanation of why a flow is not permitted, for the pending wire label.</summary>
    public static string DescribeRefusal(PinType source, PinType target)
        => $"{source} does not fit {target}";

    /// <summary>A short description of a permitted flow, for the pending wire label.</summary>
    public static string DescribeFlow(PinType source, PinType target)
        => source == target ? source.ToString() : $"{source} to {target}";
}
