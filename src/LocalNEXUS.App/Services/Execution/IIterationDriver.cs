using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// A node that runs the chain hanging off one of its own output pins, once per item.
/// </summary>
/// <remarks>
/// The fifth capability advertised this way, after code repair, model handles, tool calling and
/// planning. The executor never names a node type, and it does not name one here either: it asks
/// whether a node is a driver, and whether any driver claims the node it is about to run.
///
/// Two things follow from a node driving its neighbours, and both are what this interface exists
/// to tell the executor. The nodes downstream of <see cref="IterationOutput"/> must not also be run
/// in the ordinary pass, or every one of them would run once more after the loop with whatever the
/// last iteration happened to leave behind. And the wires leaving that pin have already been held
/// at, once per item, so holding again afterwards would stop a second time on a wire nothing is
/// about to travel down.
///
/// Nothing about ordering changes. The driven nodes are still topologically after the driver, so
/// the ordinary sort puts them in exactly the order the driver runs them in, and it is that order
/// the driver uses.
/// </remarks>
public interface IIterationDriver
{
    /// <summary>The output pin whose downstream chain this node runs itself.</summary>
    Pin IterationOutput { get; }
}
