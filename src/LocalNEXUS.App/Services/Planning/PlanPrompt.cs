using System.Text;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// Builds what the planner is asked, and what a coder is asked for one file of the plan.
/// </summary>
/// <remarks>
/// Kept apart from the nodes because the exact wording is the thing most likely to need changing,
/// and because both prompts have to agree about the shape of what comes back.
/// </remarks>
public static class PlanPrompt
{
    /// <summary>
    /// The system prompt a planning model runs under, for the kind of project that is open.
    /// </summary>
    /// <remarks>
    /// A model told about a Unity project that has no Unity in it is being given noise, and worse,
    /// an instruction it may set about satisfying. The rest of the sentence is the same either way,
    /// because none of it was ever about Unity.
    /// </remarks>
    public static string PlannerSystemPromptFor(ProjectKind kind)
        => (kind == ProjectKind.Unity
                ? "You plan changes to an existing Unity project. "
                : "You plan changes to an existing C# codebase. ")
           + "You never write code. "
           + "You answer only in the two sections you are asked for, using the exact row format given, "
           + "with no commentary, no explanation and no markdown fences.";

    /// <summary>
    /// The planner's message: what exists, what was asked for, and the format of the answer.
    /// </summary>
    public static string BuildPlannerMessage(
        string request,
        string projectMap,
        string candidateSummary,
        ContextBudget budget,
        ProjectKind kind)
    {
        var builder = new StringBuilder();

        builder.AppendLine(kind == ProjectKind.Unity
            ? "This Unity project already contains the following. Each line is a file and what it declares."
            : "This C# project already contains the following. Each line is a file and what it declares.");
        builder.AppendLine();
        builder.AppendLine(projectMap.Length == 0 ? "(the project has no C# files yet)" : projectMap);
        builder.AppendLine();

        if (candidateSummary.Length > 0)
        {
            builder.AppendLine("These files look closest to the request. Their members are listed so you can tell what they already do.");
            builder.AppendLine();
            builder.AppendLine(candidateSummary);
            builder.AppendLine();
        }

        builder.AppendLine("The request is:");
        builder.AppendLine(request.Trim());
        builder.AppendLine();
        builder.AppendLine("Answer in exactly two sections.");
        builder.AppendLine();
        // Both formats carry a filled in example, because without one they are three columns and
        // five columns of similar looking words and a model merges them. Asked to rename a class,
        // it replied with a single DECISIONS row whose reason column was the literal column names
        // of the plan format, and no PLAN section at all, ten times out of ten.
        //
        // The example names a thermostat, and that is the whole of why. It first named Health.cs,
        // which is a file the evaluation project actually contains, describing a change close
        // enough to one of its tasks that the model returned the example verbatim as its answer
        // and stopped. That task went from ten out of ten to three. An example has to be concrete,
        // or it becomes the column names problem over again, and it has to be about something no
        // project would contain, or it becomes the answer.
        //
        // The folder in it follows the project, and only the folder. Assets/Scripts is where a
        // Unity project keeps its code and is nowhere at all in any other, so showing it to a
        // model planning against a plain project invites it to invent the folder. The Unity
        // wording is unchanged to the byte, because this example is load bearing and the last
        // change to it cost a task seven runs in ten.
        var exampleFolder = kind == ProjectKind.Unity ? "Assets/Scripts" : "src";

        builder.AppendLine("DECISIONS");
        builder.AppendLine("One row per file listed above that is relevant, with exactly three columns:");
        builder.AppendLine("path | USE_AS_IS or EDIT or CREATE_NEW_REFERENCING <TypeName> or IGNORE | why");
        builder.AppendLine();
        builder.AppendLine("For example:");
        builder.AppendLine($"{exampleFolder}/Thermostat.cs | EDIT | the target temperature lives on this type");
        builder.AppendLine();
        builder.AppendLine("PLAN");
        builder.AppendLine("One row per file to write, in the order they must be written, with exactly five columns:");
        builder.AppendLine("order | CREATE or EDIT | path | main type name | what this file is for");
        builder.AppendLine();
        builder.AppendLine("For example:");
        builder.AppendLine($"1 | EDIT | {exampleFolder}/Thermostat.cs | Thermostat | clamp the target temperature to the safe range");
        builder.AppendLine();
        builder.AppendLine("Fill every column in with the real value. Do not repeat the column names back.");
        builder.AppendLine();
        builder.AppendLine("Rules.");
        builder.AppendLine("Order the plan by dependency: interfaces and data types first, then what implements them, then what uses them.");
        builder.AppendLine("Do not create a type that already exists above. Edit its file, or write something that references it.");

        // Only where it is true. The rule is real in Unity, where a component whose file name does
        // not match its class simply refuses to be added, and is a convention everywhere else.
        if (kind == ProjectKind.Unity)
        {
            builder.AppendLine("A MonoBehaviour file name must match its class name exactly.");
        }

        builder.AppendLine("Write as many files as the request genuinely needs, and no more.");
        builder.AppendLine();

        // The bar for asking is set here, in the prompt, because this is the only place that can
        // set it. A model that is invited to ask will ask about everything unless it is told very
        // plainly what does not count, and a tool that asks about everything is not used twice.
        builder.AppendLine("If, and only if, you cannot plan without knowing something that this project does not tell you,");
        builder.AppendLine("answer instead with a single section:");
        builder.AppendLine();
        builder.AppendLine("QUESTIONS");
        builder.AppendLine("One row per question, in this format:");
        builder.AppendLine("question | first option | second option | further options");
        builder.AppendLine();
        builder.AppendLine("Ask only about a fork you cannot settle from what is listed above, where choosing wrong means writing the file twice.");
        builder.AppendLine("Two existing types are equally plausible to extend, or the request names something that maps to more than one file above: ask.");
        builder.AppendLine("Never ask for confirmation of something you have already worked out.");
        builder.AppendLine("Never ask about naming, formatting, style or preference. Choose, and say so in the plan row.");
        builder.AppendLine("Never ask whether to proceed.");
        builder.AppendLine("Every question must name at least two concrete alternatives that exist in the project above. If you cannot name two, you do not have a real question, so plan instead.");
        builder.AppendLine("Ask everything you need at once. You get one opportunity.");

        return ContextBudget.Fit(builder.ToString(), budget.TotalCharacters, "the planning prompt");
    }

    /// <summary>
    /// The message a coder is given for one file of the plan, including what earlier files in the
    /// same run defined.
    /// </summary>
    public static string BuildCoderMessage(CodeTask task, string emittedSignatures, bool wholeFile)
    {
        var builder = new StringBuilder();

        builder.AppendLine(task.Operation == FileOperation.Create
            ? $"Write a new file {task.RelativePath} declaring {task.TypeName}."
            : $"Change the existing file {task.RelativePath}.");

        builder.AppendLine();
        builder.AppendLine("What this file is for:");
        builder.AppendLine(task.Intent.Length == 0 ? "(no further detail was given)" : task.Intent);
        builder.AppendLine();

        if (task.ProjectContext.Length > 0)
        {
            builder.AppendLine("What already exists in the project that this must fit into:");
            builder.AppendLine();
            builder.AppendLine(task.ProjectContext);
            builder.AppendLine();
        }

        if (emittedSignatures.Length > 0)
        {
            builder.AppendLine("Written earlier in this same request, so use these exactly as they are:");
            builder.AppendLine();
            builder.AppendLine(emittedSignatures);
            builder.AppendLine();
        }

        if (task.ExistingContent is { Length: > 0 })
        {
            builder.AppendLine($"The current content of {task.RelativePath}:");
            builder.AppendLine();
            builder.AppendLine(task.ExistingContent);
            builder.AppendLine();
        }

        builder.Append(wholeFile
            ? WholeFileInstruction(task)
            : EditFormatInstruction(task));

        return builder.ToString();
    }

    /// <summary>
    /// What to ask for when the whole file is wanted, plus the way out for a single member.
    /// </summary>
    /// <remarks>
    /// Naming the member and writing the new one asks nothing of the model about what the file
    /// already contains, which is the whole difficulty with a diff. It is offered rather than
    /// required, because a change touching several places at once is a whole file and pretending
    /// otherwise would cost more than it saved.
    /// </remarks>
    private static string WholeFileInstruction(CodeTask task)
    {
        var instruction = "Return the complete file. Output raw C# only: no markdown fences, no "
                          + "commentary. Write every member out in full: a comment saying the rest is "
                          + "unchanged deletes everything it stands for and will be refused.";

        if (task.Operation == FileOperation.Create)
        {
            return instruction;
        }

        return instruction
               + Environment.NewLine
               + Environment.NewLine
               + "If your change is confined to one or two members, you may instead name them and "
               + "give only those, which is shorter and cannot go wrong:"
               + Environment.NewLine
               + Environment.NewLine
               + $"@replace {task.TypeName}.MemberName" + Environment.NewLine
               + "the complete new declaration of that member" + Environment.NewLine
               + $"@add {task.TypeName}.NewMemberName" + Environment.NewLine
               + "the complete declaration of a member to add" + Environment.NewLine
               + $"@remove {task.TypeName}.MemberName" + Environment.NewLine
               + "@remove-using Some.Namespace"
               + Environment.NewLine
               + Environment.NewLine
               + "Indentation does not matter in that form. Do not mix it with the whole file.";
    }

    /// <summary>
    /// How large a file can be and still be worth asking for in full on a retry.
    /// </summary>
    /// <remarks>
    /// Roughly three hundred lines. Under it, asking for the whole file back is the surest repair
    /// there is, because a whole file has no block to fail to match: the failure mode is removed
    /// rather than described. Over it a whole file will not fit in a reply ceiling, so the retry
    /// asks for a diff again and has to rely on the model reading what it was sent.
    /// </remarks>
    public const int RetryWholeFileBudget = 12_000;

    /// <summary>
    /// Asks again for a file whose changes could not be applied.
    /// </summary>
    /// <remarks>
    /// The whole current file is sent, uncut, exactly as the first attempt sent it. That is worth
    /// being explicit about: the model already had the file and invented lines anyway, so the value
    /// here is not more context but the two things the first attempt did not have, which are that
    /// it was wrong and which lines it made up. The failure message carries those.
    ///
    /// Nothing is truncated. Sending less than the first attempt did would be answering a model
    /// that guessed by giving it less to work from.
    /// </remarks>
    /// <param name="task">The file being written.</param>
    /// <param name="emittedSignatures">What earlier files in this plan declared.</param>
    /// <param name="failure">Why the last answer could not be applied, naming the missing lines.</param>
    public static string BuildEditRetryMessage(CodeTask task, string emittedSignatures, string failure)
    {
        var existing = task.ExistingContent ?? string.Empty;
        var wholeFile = existing.Length is > 0 and <= RetryWholeFileBudget;

        var builder = new StringBuilder();

        builder.AppendLine($"Your previous answer for {task.RelativePath} could not be applied to the file.");
        builder.AppendLine();
        builder.AppendLine(failure);
        builder.AppendLine();
        builder.AppendLine(
            "Those lines are not in this file. Do not send them again. Work only from the content "
            + "below, which is the file exactly as it is now.");
        builder.AppendLine();

        builder.AppendLine("What this file is for:");
        builder.AppendLine(task.Intent.Length == 0 ? "(no further detail was given)" : task.Intent);
        builder.AppendLine();

        if (emittedSignatures.Length > 0)
        {
            builder.AppendLine("Written earlier in this same request, so use these exactly as they are:");
            builder.AppendLine();
            builder.AppendLine(emittedSignatures);
            builder.AppendLine();
        }

        if (existing.Length > 0)
        {
            builder.AppendLine($"The current content of {task.RelativePath}:");
            builder.AppendLine();
            builder.AppendLine(existing);
            builder.AppendLine();
        }

        builder.Append(wholeFile
            ? "Return the complete file with your change made. Output raw C# only: no markdown "
              + "fences, no commentary."
            : EditFormatInstruction(task));

        return builder.ToString();
    }

    /// <summary>
    /// The instruction for a diff shaped reply. Line tagged, because that is the format the
    /// research finds smaller models handle best, and because a tagged line can be matched even
    /// when its indentation comes back slightly wrong.
    /// </summary>
    private static string EditFormatInstruction(CodeTask task)
        => $"Return only the changes to {task.RelativePath}, as one or more blocks in exactly this format:"
           + Environment.NewLine
           + Environment.NewLine
           + "@@" + Environment.NewLine
           + "-lines to remove, each prefixed with a minus" + Environment.NewLine
           + "+lines to add, each prefixed with a plus" + Environment.NewLine
           + " lines that stay, each prefixed with a space" + Environment.NewLine
           + Environment.NewLine
           + "Include at least two unchanged lines above and below each change so it can be located. "
           + "Do not output the whole file, no markdown fences, no commentary.";
}
