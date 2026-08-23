using System.Collections;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Running a node once per entry of a list, whatever the list holds.
/// </summary>
/// <remarks>
/// A wire carries one item or many, identically, and that was true of exactly two types. A model
/// node iterated a list of file tasks and the compiler check and the file writer iterated a list of
/// generated files; anything else arriving as a list was turned into a string and handed over
/// whole, so a node emitting five of something got one reply about all five.
///
/// Nothing here knows what an item is. It reads the list, hands the node one entry at a time
/// through <see cref="NodeExecutionContext.WithValue"/>, and gathers what came back into a list per
/// output pin. The node's own code is not told which is happening: it reads its pin, and the pin
/// answers with one item.
///
/// The executor is not involved in any of this and is not told about it. Iteration is a node
/// deciding what to do with the value it was given, which is the same thing every node does.
/// </remarks>
public static class FanOut
{
    /// <summary>
    /// True when the value on a wire is a list to be worked through rather than a single thing.
    /// </summary>
    /// <remarks>
    /// A string is a sequence of characters and is never this, which is the one special case and
    /// is not a judgement about types so much as about what anybody could possibly mean.
    ///
    /// An empty list is a list. A node handed one runs zero times and produces an empty result,
    /// which is the correct answer to a plan with nothing in it and is a great deal better than
    /// running once against the word "empty".
    /// </remarks>
    public static bool TryItems(object? value, out IReadOnlyList<object?> items)
    {
        if (value is not IEnumerable sequence || value is string)
        {
            items = Array.Empty<object?>();
            return false;
        }

        var list = new List<object?>();

        foreach (var item in sequence)
        {
            list.Add(item);
        }

        items = list;
        return true;
    }

    /// <summary>
    /// Runs <paramref name="once"/> for every item, and gathers the results per output pin.
    /// </summary>
    /// <param name="node">The node being run, whose output pins the results are gathered onto.</param>
    /// <param name="itemPin">The input pin each item is handed to.</param>
    /// <param name="items">What to work through.</param>
    /// <param name="ctx">The context the node was given.</param>
    /// <param name="ct">Cancellation, checked between items as well as within them.</param>
    /// <param name="once">The node's ordinary single item execution.</param>
    public static async Task<NodeResult> OverAsync(
        NodeBase node,
        Pin itemPin,
        IReadOnlyList<object?> items,
        NodeExecutionContext ctx,
        CancellationToken ct,
        Func<NodeExecutionContext, int, CancellationToken, Task<NodeResult>> once)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(once);

        var gathered = node.Outputs.ToDictionary(pin => pin.Id, _ => new List<object?>());

        for (var index = 0; index < items.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await once(ctx.WithValue(itemPin, items[index]), index, ct).ConfigureAwait(false);

            foreach (var pin in node.Outputs)
            {
                if (result.Outputs.TryGetValue(pin.Id, out var produced))
                {
                    gathered[pin.Id].Add(produced);
                }
            }
        }

        return NodeResult.FromValues(
            gathered.Select(pair => new KeyValuePair<Guid, object?>(pair.Key, Narrow(pair.Value))));
    }

    /// <summary>
    /// Returns the gathered values as a list of their own type when they all share one.
    /// </summary>
    /// <remarks>
    /// A list of objects that happen all to be generated files is not an
    /// <c>IReadOnlyList&lt;GeneratedFile&gt;</c>, and every node downstream asks exactly that
    /// question. Without this, iterating a plan would produce a pile the file writer no longer
    /// recognised, and the iteration would have broken the thing it was meant to generalise.
    ///
    /// Nothing is named here. The type comes from the items themselves, so this works for a type
    /// added tomorrow exactly as it does for the two that exist.
    /// </remarks>
    public static object Narrow(IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var types = values
            .Where(v => v is not null)
            .Select(v => v!.GetType())
            .Distinct()
            .ToList();

        if (types.Count != 1)
        {
            return values;
        }

        var typed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(types[0]))!;

        foreach (var value in values)
        {
            typed.Add(value);
        }

        return typed;
    }
}
