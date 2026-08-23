using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Rewrites the value passing through it, either by filling a template or by evaluating a C#
/// expression.
/// </summary>
/// <remarks>
/// Two inputs: the code to change, and the rule describing the change. The rule input is optional,
/// and with nothing wired the rule typed on the node is used, which is what keeps the default path
/// free: stripping a markdown fence must not require a prompt and a model in front of it, because
/// the repair loop depends on it.
///
/// When something is wired to the rule pin, whatever arrives is the rule. That is the shape worth
/// having: a prompt describing a change in plain English, a model turning it into a pattern, and
/// this node applying it. The model authors the rule and the node executes it. Nothing here calls
/// a model, so a patch is fast and repeatable and the code never leaves the machine to be
/// reformatted.
///
/// Five modes, all mechanical. Inject puts standing text around whatever passes through, which is
/// the most common thing anybody wants and beats editing five system prompts. Extract keeps the
/// part that matches, because model output is always more than was asked for. Replace is the
/// general case, Trim cuts to a context budget, and Script is the escape hatch, compiled through
/// Roslyn and cached against the expression text.
///
/// This node used to exist mainly to take a markdown fence off a model reply, which made it
/// mandatory in every graph: boilerplate wired every time to undo an artifact of how models format
/// text. That moved into the model node, where it is a setting rather than a wire, so what is left
/// here is the reshaping somebody actually asked for.
///
/// It passes repair requests through rather than answering them, because it did not write the
/// code and cannot fix it. Its own upstream is asked, and whatever comes back is put through this
/// transform on the way out. That matters for the ordinary pipeline, where this node is what
/// strips a markdown fence from a model reply: without the pass through, a repaired reply would
/// arrive at the compiler still wrapped in one and could never compile.
/// </remarks>
public sealed partial class ReshapeNode : NodeBase, ICodeRepairSource
{
    /// <summary>The placeholder replaced with the incoming value in template mode.</summary>
    public const string InputPlaceholder = "{{input}}";

    /// <summary>
    /// How long a pattern may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// A rule can now be written by a model, and a model can write a pattern that backtracks for
    /// the rest of the afternoon on input it did not anticipate. A bounded failure naming the rule
    /// beats a run that never ends.
    /// </remarks>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The starting expression for script mode, kept for a graph that already chose it.
    /// </summary>
    public const string DefaultScriptExpression =
        "Regex.Replace(input.Trim(), @\"(?s)^```[A-Za-z0-9#+_-]*\\s*\\r?\\n(.*?)\\r?\\n?```$\", \"$1\").Trim()";

    /// <summary>
    /// Options for the script compiler, built on first use rather than in a static constructor.
    /// </summary>
    /// <remarks>
    /// Roslyn builds a reference from an assembly by reading the file it was loaded from, and in a
    /// single file publish there is no such file: the assemblies are inside the executable and
    /// report no location, so asking for them throws. Doing that in a static constructor made the
    /// whole type unusable, and because a binding to any property of a node runs its type
    /// initializer, adding a Transform node took the published application down with it.
    ///
    /// So it is built lazily and the failure is caught. Anything that still works keeps working:
    /// template mode does not compile anything, and a script node reports plainly that it could
    /// not build a compiler rather than crashing the window.
    /// </remarks>
    private static readonly Lazy<ScriptOptions?> ScriptCompilationOptions = new(() =>
    {
        var options = ScriptOptions.Default.WithImports(
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text",
            "System.Text.RegularExpressions");

        try
        {
            return options.WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Regex).Assembly);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    });

    /// <summary>
    /// True when a script transform can be compiled in this build.
    /// </summary>
    /// <remarks>
    /// Asked at startup and reported, because this is a capability that fails quietly: the default
    /// transform is the one that strips a markdown fence off a model reply, the repair loop depends
    /// on it, and a build where it cannot compile should say so rather than wait to be found out
    /// mid run.
    /// </remarks>
    public static bool CanCompileScripts => ScriptCompilationOptions.Value is not null;

    /// <summary>Which transform is applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInjectMode))]
    [NotifyPropertyChangedFor(nameof(IsExtractMode))]
    [NotifyPropertyChangedFor(nameof(IsReplaceMode))]
    [NotifyPropertyChangedFor(nameof(IsTrimMode))]
    [NotifyPropertyChangedFor(nameof(IsScriptMode))]
    private ReshapeMode _mode = ReshapeMode.Inject;

    /// <summary>Standing text put in front of whatever passes through, in inject mode.</summary>
    [ObservableProperty]
    private string _injectBefore = string.Empty;

    /// <summary>Standing text put after whatever passes through, in inject mode.</summary>
    [ObservableProperty]
    private string _injectAfter = string.Empty;

    /// <summary>The pattern whose match is kept, in extract mode.</summary>
    [ObservableProperty]
    private string _extractPattern = string.Empty;

    /// <summary>The pattern matched in replace mode.</summary>
    [ObservableProperty]
    private string _regexPattern = string.Empty;

    /// <summary>What a match is replaced with in replace mode.</summary>
    [ObservableProperty]
    private string _regexReplacement = string.Empty;

    /// <summary>The most characters allowed through, in trim mode.</summary>
    [ObservableProperty]
    private int _maximumCharacters = 8000;

    /// <summary>Which end a trim cuts from.</summary>
    [ObservableProperty]
    private TrimFrom _trimFrom = TrimFrom.End;

    /// <summary>The template applied when a rule arrives as one. Occurrences of <c>{{input}}</c> are substituted.</summary>
    [ObservableProperty]
    private string _template = InputPlaceholder;

    /// <summary>The C# expression evaluated in script mode.</summary>
    [ObservableProperty]
    private string _scriptExpression = DefaultScriptExpression;

    private ScriptRunner<object>? _compiled;
    private string? _compiledFor;

    public ReshapeNode()
        : base("Reshape")
    {
        Source = AddInput("Code", PinType.Code);
        Rule = AddInput("Rule", PinType.Text);
        Result = AddOutput("Code", PinType.Code);
    }

    /// <summary>Receives the value to rewrite.</summary>
    public Pin Source { get; }

    /// <summary>
    /// Receives the rule to apply. Optional: with nothing wired, the rule on the node is used.
    /// </summary>
    /// <remarks>
    /// This is how a model authors a rule, and it is the only way one does. A prompt describing a
    /// change feeds a model, the model writes a pattern, and this node applies it. The call happens
    /// in the model node, where it is visible on the canvas and where every other model call in
    /// this application happens.
    ///
    /// This node had a Model pin of its own for one version and it has been taken off again. It
    /// bought slightly less wiring and gave up the only guarantee that made this node worth
    /// leaving in the middle of a graph: that it costs nothing, behaves identically every time,
    /// and never sends anything anywhere.
    /// </remarks>
    public Pin Rule { get; }

    /// <summary>Carries the rewritten value onwards.</summary>
    public Pin Result { get; }

    /// <summary>
    /// Literal pairs a graph saved before the presets may still carry, kept only to be reported.
    /// </summary>
    /// <remarks>
    /// These were part of the old template mode: substitutions applied to the whole text after the
    /// template was filled. The presets replaced that editor and left them applying invisibly, with
    /// no way to see or change them, which is worse than either keeping or removing them.
    ///
    /// Removed rather than given an editor. Everything they did, replace mode does with a pattern,
    /// so an editor would be a second way to say the same thing, and a transform nobody can see
    /// silently changing text is exactly the failure this application refuses everywhere else. A
    /// graph carrying any is named on load so the pairs can be rebuilt deliberately.
    /// </remarks>
    public IReadOnlyList<string> RetiredReplacements { get; private set; } = Array.Empty<string>();

    /// <inheritdoc />
    public override string TypeKey => "Reshape";

    /// <summary>True when inject mode is selected. Drives which editor is shown.</summary>
    public bool IsInjectMode => Mode == ReshapeMode.Inject;

    /// <summary>True when extract mode is selected.</summary>
    public bool IsExtractMode => Mode == ReshapeMode.Extract;

    /// <summary>True when replace mode is selected.</summary>
    public bool IsReplaceMode => Mode == ReshapeMode.Replace;

    /// <summary>True when trim mode is selected.</summary>
    public bool IsTrimMode => Mode == ReshapeMode.Trim;

    /// <summary>True when script mode is selected.</summary>
    public bool IsScriptMode => Mode == ReshapeMode.Script;

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        // A list is reshaped entry by entry, and the rule is resolved fresh for each so a rule
        // arriving down a wire is read the same way it would be for a single value. Reshaping the
        // printed form of a list was never a thing anybody wanted.
        if (FanOut.TryItems(ctx.GetValue(Source), out var items))
        {
            return await FanOut.OverAsync(
                this,
                Source,
                items,
                ctx,
                ct,
                (itemContext, index, token) =>
                {
                    StatusMessage = $"{index + 1} of {items.Count}";
                    return ReshapeOnceAsync(itemContext, token);
                }).ConfigureAwait(false);
        }

        return await ReshapeOnceAsync(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>Applies the rule to whatever is on the source pin, once.</summary>
    private async Task<NodeResult> ReshapeOnceAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var input = ctx.GetText(Source);
        var wired = ctx.GetSourceNode(Rule) is not null;
        var rule = ResolveRule(ctx);

        var output = await ApplyAsync(rule, input, ct).ConfigureAwait(false);

        StatusMessage = $"{rule.Kind}{(wired ? " from the rule pin" : string.Empty)}: "
                        + $"{input.Length} to {output.Length} characters";

        return NodeResult.FromPin(Result, output);
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings()
        => new JsonObject
        {
            ["mode"] = Mode.ToString(),
            ["injectBefore"] = InjectBefore,
            ["injectAfter"] = InjectAfter,
            ["extractPattern"] = ExtractPattern,
            ["regexPattern"] = RegexPattern,
            ["regexReplacement"] = RegexReplacement,
            ["maximumCharacters"] = MaximumCharacters,
            ["trimFrom"] = TrimFrom.ToString(),
            ["template"] = Template,
            ["scriptExpression"] = ScriptExpression
        };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        Mode = ReadMode(settings["mode"]?.GetValue<string>());

        InjectBefore = settings["injectBefore"]?.GetValue<string>() ?? string.Empty;
        InjectAfter = settings["injectAfter"]?.GetValue<string>() ?? string.Empty;
        ExtractPattern = settings["extractPattern"]?.GetValue<string>() ?? string.Empty;
        RegexPattern = settings["regexPattern"]?.GetValue<string>() ?? string.Empty;
        RegexReplacement = settings["regexReplacement"]?.GetValue<string>() ?? string.Empty;
        MaximumCharacters = settings["maximumCharacters"]?.GetValue<int>() ?? 8000;
        Template = settings["template"]?.GetValue<string>() ?? InputPlaceholder;
        ScriptExpression = settings["scriptExpression"]?.GetValue<string>() ?? DefaultScriptExpression;

        TrimFrom = Enum.TryParse<TrimFrom>(settings["trimFrom"]?.GetValue<string>(), out var from)
            ? from
            : TrimFrom.End;

        // Read only so a graph that carried them can be told what it lost. They are not applied
        // and they are not written back, so the next save is clean.
        RetiredReplacements = settings["replacements"] is JsonArray array
            ? array.OfType<JsonObject>()
                .Select(e => $"{e["find"]?.GetValue<string>()} to {e["replace"]?.GetValue<string>()}")
                .Where(p => !p.StartsWith(" to", StringComparison.Ordinal))
                .ToList()
            : Array.Empty<string>();
    }

    /// <inheritdoc />
    public bool CanRepair(NodeExecutionContext ctx, out string reason)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        reason = string.Empty;

        var upstreamNode = ctx.GetSourceNode(Source);

        if (upstreamNode is not ICodeRepairSource upstream)
        {
            reason = $"{Title} passes repair requests upstream, and nothing that can revise is wired into it.";
            return false;
        }

        return upstream.CanRepair(ctx.ForNode(upstreamNode), out reason);
    }

    /// <inheritdoc />
    public async Task<string> ReviseAsync(CodeRepairRequest request, NodeExecutionContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        var upstreamNode = ctx.GetSourceNode(Source);

        if (upstreamNode is not ICodeRepairSource upstream)
        {
            throw new InvalidOperationException(
                $"{Title} cannot produce a new attempt: nothing that can revise is wired into it.");
        }

        var revised = await upstream
            .ReviseAsync(request, ctx.ForNode(upstreamNode), ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(revised))
        {
            return string.Empty;
        }

        // The revised reply goes through this transform exactly as the first one did, so whatever
        // this node is for, unwrapping a fence or renaming a symbol, still applies to the fix.
        return await ApplyAsync(ResolveRule(ctx), revised, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The rule in force: whatever arrived on the pin, or the one typed on the node.
    /// </summary>
    /// <remarks>
    /// Two sources, and no third. A rule arriving on the Rule pin wins, because something upstream
    /// already produced one. Otherwise the node's own settings are the rule.
    ///
    /// Nothing here calls a model. That is the whole value of this node: it costs nothing, it does
    /// the same thing every time, and nothing passing through it leaves the machine. A model can
    /// still author the rule, through a prompt and a model wired into the Rule pin, which puts the
    /// call in the node whose job it is and draws it on the canvas where it can be seen.
    ///
    /// A pin with nothing connected reads as absent rather than as an empty rule. A pin that is
    /// connected and produced nothing is a different thing and is refused, because that is a
    /// wiring mistake and silently reshaping nothing would hide it.
    /// </remarks>
    private ReshapeRule ResolveRule(NodeExecutionContext ctx)
        => ctx.GetSourceNode(Rule) is null
            ? OwnRule()
            : ReshapeRule.Parse(ctx.GetText(Rule), Mode, RegexReplacement ?? string.Empty);

    /// <summary>
    /// The rule typed on this node, in whichever form the mode says.
    /// </summary>
    /// <remarks>
    /// Inject composes its two fields into a template, so that the mode with the friendliest
    /// editor and the rule form a model is most likely to write are the same thing underneath.
    /// </remarks>
    private ReshapeRule OwnRule() => Mode switch
    {
        ReshapeMode.Inject => new ReshapeRule(
            ReshapeMode.Inject,
            $"{InjectBefore}{InputPlaceholder}{InjectAfter}",
            string.Empty),

        ReshapeMode.Extract => new ReshapeRule(ReshapeMode.Extract, ExtractPattern ?? string.Empty, string.Empty),

        ReshapeMode.Trim => new ReshapeRule(
            ReshapeMode.Trim,
            MaximumCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TrimFrom.ToString()),

        ReshapeMode.Script => new ReshapeRule(ReshapeMode.Script, ScriptExpression ?? string.Empty, string.Empty),

        _ => new ReshapeRule(ReshapeMode.Replace, RegexPattern ?? string.Empty, RegexReplacement ?? string.Empty)
    };

    /// <summary>Applies a rule to a value, mechanically and without asking anything.</summary>
    private async Task<string> ApplyAsync(ReshapeRule rule, string input, CancellationToken ct)
        => rule.Kind switch
        {
            ReshapeMode.Inject => ApplyTemplate(rule.Primary, input),
            ReshapeMode.Extract => ApplyExtract(rule, input),
            ReshapeMode.Trim => ApplyTrim(rule, input),
            ReshapeMode.Script => await RunScriptAsync(rule.Primary, input, ct).ConfigureAwait(false),
            _ => ApplyPattern(rule, input)
        };

    /// <summary>
    /// Keeps the part that matches and drops the rest.
    /// </summary>
    /// <remarks>
    /// The first capturing group when the pattern has one, and the whole match when it does not,
    /// because a pattern written to find a plan usually brackets the plan and a pattern written to
    /// find a line usually does not.
    ///
    /// A pattern that finds nothing passes the text through. Extracting nothing from a reply that
    /// simply did not contain the shape asked for would hand an empty file to whatever is next,
    /// and an empty file compiles.
    /// </remarks>
    private string ApplyExtract(ReshapeRule rule, string input)
    {
        if (string.IsNullOrEmpty(rule.Primary))
        {
            throw new ReshapeRuleException(
                $"{Title} has nothing to extract with. Give it a pattern, or wire a rule into it.");
        }

        try
        {
            var match = Regex.Match(input, rule.Primary, RegexOptions.None, PatternTimeout);

            if (!match.Success)
            {
                return input;
            }

            return match.Groups.Count > 1 && match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Value;
        }
        catch (ArgumentException ex)
        {
            throw new ReshapeRuleException(
                $"{Title} could not read its pattern: {ex.Message}{Environment.NewLine}{rule.Primary}", ex);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new ReshapeRuleException(
                $"{Title} gave up on its pattern after {PatternTimeout.TotalSeconds:0} seconds. "
                + $"It matches this input too slowly to use:{Environment.NewLine}{rule.Primary}", ex);
        }
    }

    /// <summary>
    /// Cuts to a length, from whichever end was asked for.
    /// </summary>
    /// <remarks>
    /// Nothing is added to say it was cut. This feeds a context budget, and a marker would be one
    /// more thing counted against the budget it exists to respect.
    /// </remarks>
    private string ApplyTrim(ReshapeRule rule, string input)
    {
        if (!int.TryParse(rule.Primary, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var limit) || limit <= 0)
        {
            throw new ReshapeRuleException(
                $"{Title} was given \"{rule.Primary}\" as a length to trim to, which is not a number of characters.");
        }

        if (input.Length <= limit)
        {
            return input;
        }

        return string.Equals(rule.Replacement, nameof(Nodes.TrimFrom.Start), StringComparison.OrdinalIgnoreCase)
            ? input[^limit..]
            : input[..limit];
    }

    /// <summary>
    /// Matches the pattern and replaces what it finds.
    /// </summary>
    /// <remarks>
    /// A pattern that matches nothing is not a failure: that is a rule with nothing to say about
    /// this particular input, and the code passes through. A pattern that will not compile, or one
    /// that runs away, is a failure, because neither can be what anybody meant.
    /// </remarks>
    private string ApplyPattern(ReshapeRule rule, string input)
    {
        if (string.IsNullOrEmpty(rule.Primary))
        {
            throw new ReshapeRuleException($"{Title} has no pattern to apply. Type one, or wire a rule into it.");
        }

        try
        {
            return Regex.Replace(input, rule.Primary, rule.Replacement ?? string.Empty, RegexOptions.None, PatternTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ReshapeRuleException(
                $"{Title} could not read its pattern: {ex.Message}{Environment.NewLine}{rule.Primary}", ex);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new ReshapeRuleException(
                $"{Title} gave up on its pattern after {PatternTimeout.TotalSeconds:0} seconds. "
                + $"It matches this input too slowly to use:{Environment.NewLine}{rule.Primary}", ex);
        }
    }

    private string ApplyTemplate(string template, string input)
        => (template ?? string.Empty).Replace(InputPlaceholder, input, StringComparison.Ordinal);

    private async Task<string> RunScriptAsync(string expression, string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return input;
        }

        var runner = GetOrCompileRunner(expression);

        object? value;
        try
        {
            value = await runner(new ReshapeScriptGlobals { input = input }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{Title} script failed at run time: {ex.Message}", ex);
        }

        return value?.ToString() ?? string.Empty;
    }

    private ScriptRunner<object> GetOrCompileRunner(string expression)
    {
        if (_compiled is not null && string.Equals(_compiledFor, expression, StringComparison.Ordinal))
        {
            return _compiled;
        }

        if (ScriptCompilationOptions.Value is not { } options)
        {
            throw new InvalidOperationException(
                $"{Title} cannot compile a script in this build: the script compiler needs the runtime assemblies as files, "
                + "and a single file executable keeps them inside itself. Use Find and replace instead, or run from a build "
                + "that is not published as a single file.");
        }

        try
        {
            var script = CSharpScript.Create<object>(expression, options, typeof(ReshapeScriptGlobals));
            _compiled = script.CreateDelegate();
            _compiledFor = expression;
            return _compiled;
        }
        catch (CompilationErrorException ex)
        {
            _compiled = null;
            _compiledFor = null;
            var diagnostics = string.Join("; ", ex.Diagnostics.Select(d => d.GetMessage()));
            throw new InvalidOperationException($"{Title} script did not compile: {diagnostics}", ex);
        }
    }

    /// <summary>
    /// The mode a saved node asked for, including the two names that no longer exist.
    /// </summary>
    /// <remarks>
    /// Regex became Replace and Template became Inject, so a graph saved before the presets opens
    /// on the mode that does what it used to do rather than falling back to the default and
    /// silently changing what the node is for. That is the same rule the type keys follow: a
    /// rename is only free if every name it ever had still resolves.
    /// </remarks>
    private static ReshapeMode ReadMode(string? saved)
    {
        if (saved is null)
        {
            return ReshapeMode.Inject;
        }

        if (Enum.TryParse<ReshapeMode>(saved, ignoreCase: true, out var mode))
        {
            return mode;
        }

        return saved.ToLowerInvariant() switch
        {
            "regex" => ReshapeMode.Replace,
            "template" => ReshapeMode.Inject,
            _ => ReshapeMode.Inject
        };
    }

    partial void OnScriptExpressionChanged(string value)
    {
        // Drop the cached compilation so the next run picks up the edited expression.
        _compiled = null;
        _compiledFor = null;
    }
}
