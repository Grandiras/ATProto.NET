// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

/// <summary>How serious a <see cref="LexiconDiagnostic"/> is.</summary>
#if LEXICON_CODEGEN
internal
#else
public
#endif
enum LexiconSeverity
{
    /// <summary>The schema works, but breaks a rule or a style recommendation.</summary>
    Warning,

    /// <summary>The schema is invalid.</summary>
    Error,
}

/// <summary>A finding about a Lexicon document: a parse violation, an unresolved reference or a lint rule.</summary>
/// <param name="RuleId">The stable rule ID, <c>LXL001</c> onward.</param>
/// <param name="Severity">How serious the finding is.</param>
/// <param name="Message">What is wrong, in one sentence.</param>
/// <param name="DocumentId">The document's <c>id</c>, when known.</param>
/// <param name="Pointer">The JSON pointer of the offending node within the document (<c>""</c> for the root).</param>
sealed partial record LexiconDiagnostic(string RuleId, LexiconSeverity Severity, string Message, string? DocumentId, string Pointer)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{(DocumentId is null ? "" : DocumentId)}{(Pointer.Length == 0 ? "" : " " + Pointer)}: {(Severity == LexiconSeverity.Error ? "error" : "warning")} {RuleId}: {Message}";
}

/// <summary>The rule IDs of <see cref="LexiconDiagnostic"/>.</summary>
static partial class LexiconRules
{
    /// <summary>The document is not a Lexicon: <c>lexicon</c>, <c>id</c> or <c>defs</c> is missing or invalid.</summary>
    public const string DocumentShape = "LXL001";

    /// <summary>A primary type (<c>record</c>, <c>query</c>, …) is not the <c>main</c> definition, or there are several.</summary>
    public const string PrimaryType = "LXL002";

    /// <summary>A schema node is malformed: a required field is missing, a field has the wrong JSON kind, or a schema is <c>null</c>.</summary>
    public const string InvalidSchema = "LXL003";

    /// <summary>A definition kind that may not be named: <c>ref</c>, <c>unknown</c> or <c>params</c>.</summary>
    public const string ForbiddenDefinition = "LXL004";

    /// <summary>A <c>type</c> this version does not know; it is kept but not used.</summary>
    public const string UnknownType = "LXL005";

    /// <summary>A named <c>union</c> definition, which the Lexicon spec does not allow; accepted for compatibility.</summary>
    public const string NamedUnion = "LXL006";

    /// <summary>A parameter whose type is not <c>boolean</c>, <c>integer</c>, <c>string</c> or an array of those.</summary>
    public const string ParameterType = "LXL007";

    /// <summary>A reference that resolves to nothing.</summary>
    public const string UnresolvedRef = "LXL010";

    /// <summary>A reference to the wrong kind of definition: a <c>ref</c> to a token, a union member that is a union, or a cycle of aliases.</summary>
    public const string InvalidRef = "LXL011";

    /// <summary>A structure that refers to itself through required, non-nullable, non-array fields, so no value can exist.</summary>
    public const string InfiniteRecursion = "LXL012";

    /// <summary>A permission set is not <c>main</c>, has no <c>title</c> or has no permissions.</summary>
    public const string PermissionSetShape = "LXL020";

    /// <summary>A permission is malformed, or names a resource a permission set cannot grant.</summary>
    public const string PermissionInvalid = "LXL021";

    /// <summary>A permission parameter that does not apply to its resource.</summary>
    public const string PermissionParameter = "LXL022";

    /// <summary>A permission with a wildcard, or an NSID outside the set's namespace group.</summary>
    public const string PermissionScope = "LXL023";

    /// <summary>A permission resource authorization servers ignore.</summary>
    public const string PermissionIgnored = "LXL024";

    /// <summary>A space collection that is a wildcard or not an NSID.</summary>
    public const string SpaceCollection = "LXL030";

    /// <summary>A property name that is not camelCase.</summary>
    public const string StylePropertyName = "LXL040";

    /// <summary>A primary definition without a description.</summary>
    public const string StyleDescription = "LXL041";

    /// <summary>A <c>maxGraphemes</c> without a <c>maxLength</c>.</summary>
    public const string StyleGraphemes = "LXL042";
}
