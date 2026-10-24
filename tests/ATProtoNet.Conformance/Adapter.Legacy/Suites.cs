using ATProtoNet.Conformance.Suites;

namespace ATProtoNet.Conformance.Legacy;

public sealed class Syntax : SyntaxConformance<LegacyAdapter>;

public sealed class XrpcWire : XrpcWireConformance<LegacyAdapter>;

public sealed class DataModel : DataModelConformance<LegacyAdapter>;

public sealed class CidCar : CidCarConformance<LegacyAdapter>;

public sealed class HostileInput : HostileInputConformance<LegacyAdapter>;

public sealed class Crypto : CryptoConformance<LegacyAdapter>;

public sealed class Mst : MstConformance<LegacyAdapter>;

public sealed class Repo : RepoConformance<LegacyAdapter>;

public sealed class Firehose : FirehoseConformance<LegacyAdapter>;
public sealed class Jetstream : JetstreamConformance<LegacyAdapter>;
public sealed class Labels : LabelsConformance<LegacyAdapter>;
public sealed class Tokens : TokensConformance<LegacyAdapter>;
public sealed class Lexicon : LexiconConformance<LegacyAdapter>;
public sealed class Spaces : SpacesConformance<LegacyAdapter>;
public sealed class Identity : IdentityConformance<LegacyAdapter>;
