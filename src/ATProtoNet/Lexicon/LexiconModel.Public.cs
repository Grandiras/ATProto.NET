namespace ATProtoNet.Lexicon;

// The schema model's sources are linked into ATProtoNet.Lexicon.CodeGen, where they stay internal (LEX-26): they declare no
// accessibility, and this file, compiled only here, makes them public.

public abstract partial record LexSchema;
public abstract partial record LexUserType;
public sealed partial record LexRecord;
public sealed partial record LexQuery;
public sealed partial record LexProcedure;
public sealed partial record LexSubscription;
public sealed partial record LexPermissionSet;
public sealed partial record LexPermission;
public sealed partial record LexSpace;
public sealed partial record LexToken;
public sealed partial record LexObject;
public sealed partial record LexParams;
public sealed partial record LexString;
public sealed partial record LexInteger;
public sealed partial record LexBoolean;
public sealed partial record LexBytes;
public sealed partial record LexCidLink;
public sealed partial record LexBlob;
public sealed partial record LexArray;
public sealed partial record LexUnknown;
public sealed partial record LexRef;
public sealed partial record LexUnion;
public sealed partial record LexUnknownType;
public sealed partial record LexBody;
public sealed partial record LexError;
public sealed partial record RecordKeyType;
public sealed partial class LexiconDocument { }
public static partial class LexiconLimits { }
public sealed partial record LexiconDiagnostic;
public static partial class LexiconRules { }
public readonly partial record struct LexRefTarget;
public sealed partial record ExternalDefinition;
public partial interface IExternalLexicons { }
public sealed partial record ResolvedDefinition;
public sealed partial class LexiconCatalog { }
public static partial class LexiconLint { }
