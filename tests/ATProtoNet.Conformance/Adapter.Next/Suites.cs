using ATProtoNet.Conformance.Suites;

namespace ATProtoNet.Conformance.Next;

// One class per suite whose phase has landed (design/05-rewrite-plan.md).

public sealed class Syntax : SyntaxConformance<NextAdapter>;

public sealed class DataModel : DataModelConformance<NextAdapter>;

public sealed class CidCar : CidCarConformance<NextAdapter>;

public sealed class HostileInput : HostileInputConformance<NextAdapter>;

public sealed class Crypto : CryptoConformance<NextAdapter>;

public sealed class Spaces : SpacesConformance<NextAdapter>;
