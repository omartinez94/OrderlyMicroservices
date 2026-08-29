// Phase 3 of CODE_STYLE_ITERATION_PLAN. One-shot Roslyn tool that walks
// every C# document in the solution and replaces `var <name> = ...;`
// with `<InferredType> <name> = ...;`. Carve-outs (per the plan):
//   - `using var x = ...` and `await using var x = ...`
//   - Anonymous types (`var x = new { ... }`)
//   - Tuple deconstructions (`var (a, b) = ...`)
//   - Unresolved types (`var x = null;`)
//   - Generated files (`*.g.cs`, `*.Designer.cs`, `*.AssemblyInfo.cs`,
//     `**/Migrations/**`, `obj/`, `bin/`) and `*Tests*` projects (the
//     plan exempts Tests via a permanent editorconfig override).

using System.Text.RegularExpressions;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

MSBuildLocator.RegisterDefaults();

string slnxPath = args.Length > 0 && !args[0].StartsWith("--")
    ? args[0]
    : FindSlnxInCurrentDir()
      ?? throw new InvalidOperationException("No .slnx found in current dir; pass the path as the first arg.");

bool dryRun = args.Contains("--dry-run");
bool verify = args.Contains("--verify");
bool includeTests = args.Contains("--include-tests");
bool verbose = args.Contains("--verbose");

List<string> projectFilters = ExtractFlagValues(args, "--project");

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

if (verify)
{
    Console.WriteLine("==> Running whitespace gate before conversion...");
    var format = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "format whitespace \"" + slnxPath + "\" --verify-no-changes --no-restore",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        },
    };
    format.Start();
    string stderr = await format.StandardError.ReadToEndAsync();
    await format.WaitForExitAsync();
    if (format.ExitCode != 0)
    {
        Console.Error.WriteLine("Whitespace gate failed before conversion; aborting.\n" + stderr);
        return 1;
    }
    Console.WriteLine("    Whitespace gate clean.");
}

Console.WriteLine($"==> Parsing {slnxPath} for project paths ...");
string[] projectPaths = ParseSlnxProjects(slnxPath);
Console.WriteLine($"    Found {projectPaths.Length} projects.");

Console.WriteLine($"==> Opening workspace ...");
using var workspace = MSBuildWorkspace.Create();
workspace.SkipUnrecognizedProjects = true;

// MSBuildWorkspace doesn't support .slnx (only .sln). Open one "root" project
// — typically the AppHost that transitively references every other production
// project — and let MSBuild's project-reference graph pull the rest into the
// workspace. We then enumerate the workspace's projects.
//
// Without this approach, calling OpenProjectAsync on each project in turn
// triggers "ya es parte del área de trabajo" errors because transitive
// references get loaded eagerly on the first OpenProjectAsync call.
Project? rootProject = null;
foreach (string csprojPath in projectPaths)
{
    string rootName = Path.GetFileNameWithoutExtension(csprojPath);
    if (rootName.Equals("YarpApiGateway", StringComparison.OrdinalIgnoreCase)
        || rootName.EndsWith(".AppHost", StringComparison.OrdinalIgnoreCase)
        || rootName.Equals("AppHost", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            rootProject = await workspace.OpenProjectAsync(csprojPath);
            Console.WriteLine($"    Root project opened: {rootName}");
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    FAILED to open root {csprojPath}: {ex.Message}");
        }
    }
}

if (rootProject is null)
{
    Console.Error.WriteLine("    FAILED to open any root project (YarpApiGateway / AppHost).");
    return 1;
}

Console.WriteLine($"    Workspace loaded; current project count = {workspace.CurrentSolution.Projects.Count()}");

// Open any project in the .slnx that didn't get loaded transitively.
// Skip projects that are already in the workspace to avoid the duplicate-open
// error.
foreach (string csprojPath in projectPaths)
{
    string fullPath = Path.GetFullPath(csprojPath);
    bool alreadyLoaded = workspace.CurrentSolution.Projects
        .Any(p => p.FilePath is not null
                  && string.Equals(Path.GetFullPath(p.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
    if (alreadyLoaded) continue;

    string name = Path.GetFileNameWithoutExtension(csprojPath);
    try
    {
        await workspace.OpenProjectAsync(csprojPath);
        Console.WriteLine($"    Loaded additional project: {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"    FAILED to open {name}: {ex.Message}");
    }
}

Console.WriteLine($"    Final project count = {workspace.CurrentSolution.Projects.Count()}");

int totalConverted = 0;
int totalCarveOuts = 0;
int totalUnresolved = 0;
int filesTouched = 0;
List<string> failures = [];

// Build a map from project file path -> Project (case-insensitive on Windows).
var projectsByPath = workspace.CurrentSolution.Projects
    .Where(p => p.FilePath is not null)
    .ToDictionary(p => Path.GetFullPath(p.FilePath!), StringComparer.OrdinalIgnoreCase);

foreach (string csprojPath in projectPaths)
{
    string projectName = Path.GetFileNameWithoutExtension(csprojPath);

    // Skip tests unless explicitly included.
    if (!includeTests && (projectName.Contains("Tests", StringComparison.Ordinal) || projectName.EndsWith(".Tests")))
    {
        if (verbose) Console.WriteLine($"    SKIP (Tests): {projectName}");
        continue;
    }

    if (projectFilters.Count > 0 && !projectFilters.Any(f => projectName.Contains(f, StringComparison.OrdinalIgnoreCase)))
    {
        if (verbose) Console.WriteLine($"    SKIP (filter): {projectName}");
        continue;
    }

    if (!projectsByPath.TryGetValue(Path.GetFullPath(csprojPath), out Project? project))
    {
        Console.WriteLine($"==> Project: {projectName}  (not in solution — skipping)");
        continue;
    }

    Console.WriteLine($"==> Project: {projectName}");

    foreach (Document document in project.Documents)
    {
        string? srcPath = document.FilePath;
        if (srcPath is null) continue;
        if (IsGenerated(srcPath)) continue;

        SyntaxTree? tree = await document.GetSyntaxTreeAsync();
        if (tree is null) continue;

        SyntaxNode root = await tree.GetRootAsync();
        SemanticModel? sm = await document.GetSemanticModelAsync();
        if (sm is null) continue;

        ConversionResult result = Convert(root, sm, dryRun);
        if (result.Changes == 0) continue;

        filesTouched++;
        totalConverted += result.Changes;
        totalCarveOuts += result.CarveOuts;
        totalUnresolved += result.Unresolved;

        if (verbose)
        {
            Console.WriteLine($"    {Path.GetRelativePath(Path.GetDirectoryName(slnxPath)!, srcPath)}: {result.Changes} converted, {result.CarveOuts} carve-outs, {result.Unresolved} unresolved");
        }

        if (!dryRun)
        {
            SyntaxNode newRoot = result.NewRoot!;
            File.WriteAllText(srcPath, newRoot.ToFullString());
        }
    }
}

Console.WriteLine();
Console.WriteLine($"==> Summary:");
Console.WriteLine($"    Files touched:  {filesTouched}");
Console.WriteLine($"    Converted:      {totalConverted}");
Console.WriteLine($"    Carve-outs:     {totalCarveOuts} (using/await using/anonymous/tuple)");
Console.WriteLine($"    Unresolved:     {totalUnresolved} (e.g. `var x = null;` — left alone)");

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"!! Failures:");
    foreach (string f in failures) Console.Error.WriteLine($"    {f}");
    return 1;
}

return 0;

static ConversionResult Convert(SyntaxNode root, SemanticModel sm, bool dryRun)
{
    var rewriter = new VarToExplicitRewriter(sm);
    SyntaxNode newRoot = rewriter.Visit(root);
    return new ConversionResult(
        Changes: rewriter.Conversions,
        CarveOuts: rewriter.CarveOuts,
        Unresolved: rewriter.Unresolved,
        NewRoot: dryRun ? null : newRoot);
}

static bool IsGenerated(string path)
{
    if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) return true;
    if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) return true;
    if (path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")) return true;
    string name = Path.GetFileName(path);
    if (name.EndsWith(".Designer.cs")) return true;
    if (name.EndsWith(".g.cs")) return true;
    if (name.EndsWith(".AssemblyInfo.cs")) return true;
    return false;
}

static string? FindSlnxInCurrentDir()
{
    string? dir = Directory.GetCurrentDirectory();
    while (dir is not null)
    {
        foreach (string f in Directory.EnumerateFiles(dir, "*.slnx"))
            return f;
        dir = Directory.GetParent(dir)?.FullName;
    }
    return null;
}

static string[] ParseSlnxProjects(string slnxPath)
{
    var paths = new List<string>();
    var xdoc = System.Xml.Linq.XDocument.Load(slnxPath);
    string baseDir = Path.GetDirectoryName(slnxPath)!;
    foreach (var proj in xdoc.Descendants("Project"))
    {
        string? rel = proj.Attribute("Path")?.Value;
        if (string.IsNullOrEmpty(rel)) continue;
        // .slnx paths are relative to the .slnx directory.
        string full = Path.GetFullPath(Path.Combine(baseDir, rel!));
        paths.Add(full);
    }
    return [.. paths];
}

static List<string> ExtractFlagValues(string[] argv, string flag)
{
    List<string> values = [];
    for (int i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i] == flag) values.Add(argv[i + 1]);
    }
    return values;
}

static void PrintUsage()
{
    Console.WriteLine("convert-var-to-explicit — Phase 3 of CODE_STYLE_ITERATION_PLAN");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  convert-var-to-explicit <slnx> [--dry-run] [--verify] [--verbose]");
    Console.WriteLine("                        [--include-tests] [--project <name>]...");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  <slnx>             Path to the solution file.");
    Console.WriteLine("  --dry-run          Print summary of changes without writing.");
    Console.WriteLine("  --verify           Run `dotnet format whitespace --verify-no-changes` first.");
    Console.WriteLine("  --include-tests    Include *Tests* projects (default: skip — exempt).");
    Console.WriteLine("  --project <name>   Filter by project name substring (repeatable).");
    Console.WriteLine("  --verbose          Print each file edited.");
    Console.WriteLine();
    Console.WriteLine("Carve-outs (NOT converted):");
    Console.WriteLine("  - `using var` / `await using var`");
    Console.WriteLine("  - Anonymous types: `var x = new { ... }`");
    Console.WriteLine("  - Tuple deconstructions: `var (a, b) = ...`");
    Console.WriteLine("  - Unresolved types: `var x = null;`");
    Console.WriteLine("  - Generated files (*.g.cs, *.Designer.cs, Migrations/, obj/, bin/)");
}

internal sealed record ConversionResult(int Changes, int CarveOuts, int Unresolved, SyntaxNode? NewRoot);

internal sealed class VarToExplicitRewriter(SemanticModel semanticModel) : CSharpSyntaxRewriter
{
    // FullyQualifiedFormat is the only format that's *guaranteed* to compile:
    // it always names the type by its full namespace, so the result doesn't
    // depend on the file's `using` directives being correct.
    //
    // We then pass that name + a position through ToMinimalDisplayString so
    // the qualifier collapses when the file's using directives (or `global
    // using`s from GlobalUsings.cs) make it unambiguous. For example:
    //   - file declares `global using MassTransit;` in GlobalUsings.cs
    //   - tool's FullyQualifiedFormat produces "global::MassTransit.IPublishEndpoint"
    //   - ToMinimalDisplayString produces "IPublishEndpoint?" (uses the global using)
    // Without this step, simply stripping "global::" produces
    // "MassTransit.IPublishEndpoint", which the C# compiler then resolves
    // relative to the current namespace (BuildingBlocks.Messaging.Outbox) and
    // looks for "MassTransit" there — finding BuildingBlocks.Messaging.MassTransit
    // instead, which has the WRONG IPublishEndpoint (or none at all).
    private static readonly SymbolDisplayFormat Format = SymbolDisplayFormat
        .FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers)
        .WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static string FormatTypeName(ITypeSymbol type, SemanticModel sm, int position)
    {
        string fullyQualified = type.ToDisplayString(Format);
        // ToMinimalDisplayString uses the file's using directives + the
        // current namespace to produce the shortest unambiguous name. This
        // collapses "global::MassTransit.IPublishEndpoint" to
        // "IPublishEndpoint?" when there's a `using MassTransit;` (or a
        // `global using MassTransit;` in GlobalUsings.cs).
        string minimal = type.ToMinimalDisplayString(sm, position, Format);
        return minimal.Replace("global::", string.Empty);
    }

    public int Conversions;
    public int CarveOuts;
    public int Unresolved;

    public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        // Visit children FIRST (against the original tree + semantic model),
        // THEN apply the rewrite to the already-visited node. The original
        // code passed `node.WithDeclaration(newDecl)` to base.Visit, which
        // asks Roslyn to walk a freshly-built tree; GetTypeInfo then fails
        // because the semantic model is bound to the original tree, not the
        // rewritten one. (Observed: `Microsoft.CodeAnalysis.CSharp.CheckSyntaxNode`
        // throws when GetTypeInfo is called on a node from a different tree.)
        var visited = (LocalDeclarationStatementSyntax)base.VisitLocalDeclarationStatement(node);

        VariableDeclarationSyntax decl = visited.Declaration;
        if (!decl.Type.IsVar)
        {
            return visited;
        }

        // Carve-out: `using var x = ...;` and `await using var x = ...;`
        // These are LocalDeclarationStatementSyntax with a UsingKeyword on the statement.
        if (visited.UsingKeyword != default)
        {
            CarveOuts++;
            return visited;
        }

        // Carve-out: lambda bodies live in a separate SyntaxTree. The parent's
        // SemanticModel doesn't cover them, so GetTypeInfo throws. Get the
        // lambda body's own semantic model when available.
        SemanticModel? effectiveSm = GetEffectiveSemanticModel(visited, semanticModel);
        if (effectiveSm is null)
        {
            Unresolved++;
            return visited;
        }

        ITypeSymbol? type = effectiveSm.GetTypeInfo(decl.Type).Type;
        if (type is null || IsAnonymousType(type))
        {
            if (type is null) Unresolved++;
            else CarveOuts++;
            return visited;
        }

        // Skip tuple deconstructions: var (a, b) = ...
        if (decl.Variables.Any(v => v.Parent is ParenthesizedVariableDesignationSyntax))
        {
            CarveOuts++;
            return visited;
        }

        string typeName = FormatTypeName(type, effectiveSm, decl.Type.SpanStart);
        TypeSyntax newType = SyntaxFactory.ParseTypeName(typeName)
            .WithTriviaFrom(decl.Type);
        VariableDeclarationSyntax newDecl = decl.WithType(newType);
        Conversions++;
        return visited.WithDeclaration(newDecl);
    }

    public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
    {
        // Same pattern as VisitLocalDeclarationStatement: visit the original first.
        var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);

        if (!visited.Type.IsVar)
        {
            return visited;
        }

        SemanticModel? effectiveSm = GetEffectiveSemanticModel(visited, semanticModel);
        if (effectiveSm is null)
        {
            Unresolved++;
            return visited;
        }

        ITypeSymbol? type = effectiveSm.GetTypeInfo(visited.Type).Type;
        if (type is null || IsAnonymousType(type))
        {
            if (type is null) Unresolved++;
            else CarveOuts++;
            return visited;
        }

        string typeName = FormatTypeName(type, effectiveSm, visited.Type.SpanStart);
        TypeSyntax newType = SyntaxFactory.ParseTypeName(typeName)
            .WithTriviaFrom(visited.Type);
        Conversions++;
        return visited.WithType(newType);
    }

    /// <summary>
    /// Lambda bodies, query expressions, and other "nested" constructs live in
    /// their own <see cref="SyntaxTree"/> with their own <see cref="SemanticModel"/>.
    /// The semantic model we get from the document only covers the outer tree,
    /// so calling <c>GetTypeInfo</c> on a node from a nested tree throws
    /// <c>ArgumentException("El nodo de sintaxis no está dentro del árbol de sintaxis")</c>.
    /// When the node is in a different tree, ask the Compilation (which covers
    /// all trees in the unit, including anonymous lambda bodies) for the
    /// semantic model on that tree. Compilation.GetSemanticModel works for
    /// trees that aren't in <c>compilation.SyntaxTrees</c> too — it returns a
    /// synthesized semantic model for any tree the Compilation knows about.
    /// </summary>
    private static SemanticModel? GetEffectiveSemanticModel(SyntaxNode node, SemanticModel documentSm)
    {
        if (node.SyntaxTree == documentSm.SyntaxTree)
        {
            return documentSm;
        }

        // Node is in a nested tree (lambda body, query expression body, etc.).
        // The Compilation has all the source trees (including anonymous
        // lambda-body trees); ask it directly for the semantic model bound to
        // that nested tree. Do NOT iterate compilation.SyntaxTrees — lambda
        // bodies are NOT listed there. Compilation.GetSemanticModel accepts
        // any tree the Compilation knows about and returns a usable model.
        Compilation? compilation = documentSm.Compilation;
        if (compilation is null) return null;

        try
        {
            return compilation.GetSemanticModel(node.SyntaxTree);
        }
        catch (ArgumentException)
        {
            // Compilation refused to model this tree (shouldn't happen for
            // any tree reachable from the document).
            return null;
        }
    }

    private static bool IsAnonymousType(ITypeSymbol type)
    {
        // Anonymous types live in the <>f__AnonymousType... family inside the
        // CompilerServices namespace. INamedTypeSymbol.IsAnonymousType is the
        // canonical check.
        //
        // We have to walk through THREE wrappers that the inferred type can
        // take when an anonymous type appears inside an expression:
        //   1. IArrayTypeSymbol — `var x = new[] { new { ... } }` infers to `<AnonymousType>[]`
        //   2. INamedTypeSymbol type arguments — `var x = list.Select(...).ToList()` infers to `List<AnonymousType>`
        //   3. Nullable wrappers — `var x = MaybeAnonymous()` might infer to `AnonymousType?`
        //
        // The previous name-prefix check missed both array-wrapped anonymous
        // types and the canonical `<>f__AnonymousType0` metadata name (which
        // CONTAINS "AnonymousType" but does NOT start with it).
        return ContainsAnonymousType(type);

        static bool ContainsAnonymousType(ITypeSymbol t)
        {
            if (t is IArrayTypeSymbol array)
            {
                return ContainsAnonymousType(array.ElementType);
            }
            if (t is INamedTypeSymbol named)
            {
                if (named.IsAnonymousType) return true;
                foreach (ITypeSymbol arg in named.TypeArguments)
                {
                    if (ContainsAnonymousType(arg)) return true;
                }
                return false;
            }
            // Fallback for pointer / dynamic / other exotic types.
            return t.Name.Contains("AnonymousType", StringComparison.Ordinal);
        }
    }
}
