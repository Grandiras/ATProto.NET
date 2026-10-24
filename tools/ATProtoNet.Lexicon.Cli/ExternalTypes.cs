using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ATProtoNet.Lexicon.CodeGen;
using ATProtoNet.Lexicon.Runtime;

namespace ATProtoNet.Lexicon.Cli;

/// <summary>
/// The Lexicon definitions and string formats of referenced assemblies, read from their metadata without loading them (LEX-8):
/// <c>[LexiconDefinition]</c> on types and <c>[LexiconFormatType]</c> on assemblies. ATProtoNet's own formats always apply.
/// </summary>
public sealed class ExternalTypes : IExternalLexiconTypes
{
    private const string RuntimeNamespace = "ATProtoNet.Lexicon.Runtime";

    private readonly Dictionary<string, (string TypeName, ExternalTypeKind Kind)> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _formats = new(StringComparer.Ordinal);

    private ExternalTypes()
    {
        foreach (var attribute in typeof(LexiconFormatTypeAttribute).Assembly.GetCustomAttributes<LexiconFormatTypeAttribute>())
            _formats[attribute.Format] = attribute.Type.FullName!;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> FormatTypes => _formats;

    /// <inheritdoc />
    public bool TryResolve(string reference, out string typeName, out ExternalTypeKind kind)
    {
        var found = _definitions.TryGetValue(reference, out var definition);
        (typeName, kind) = found ? definition : ("", ExternalTypeKind.Object);
        return found;
    }

    /// <summary>Whether a referenced assembly defines anything of an NSID, so it is not installed (TOO-17).</summary>
    /// <param name="nsid">The NSID.</param>
    /// <returns>Whether any definition belongs to it.</returns>
    public bool Provides(string nsid) => _definitions.Keys.Any(reference => reference.Split('#')[0] == nsid);

    /// <summary>Reads assemblies.</summary>
    /// <param name="paths">The assembly files.</param>
    /// <param name="files">The file system.</param>
    /// <returns>Their definitions and formats.</returns>
    /// <exception cref="LexgenException">An assembly is missing or not a .NET assembly (exit 3).</exception>
    public static ExternalTypes Read(IEnumerable<string> paths, ILexgenFileSystem files)
    {
        var types = new ExternalTypes();
        foreach (var path in paths)
        {
            if (!files.FileExists(path)) throw new LexgenException(ExitCodes.Input, $"{path}: the referenced assembly does not exist.");
            try
            {
                using var pe = new PEReader(ImmutableArray.Create(files.ReadAllBytes(path)));
                types.Add(pe.GetMetadataReader());
            }
            catch (Exception e) when (e is BadImageFormatException or InvalidOperationException)
            {
                throw new LexgenException(ExitCodes.Input, $"{path}: not a .NET assembly ({e.Message}).");
            }
        }
        return types;
    }

    private void Add(MetadataReader reader)
    {
        var provider = new TypeNames();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (AttributeName(reader, attribute) != "LexiconFormatTypeAttribute") continue;
            var value = attribute.DecodeValue(provider);
            _formats[(string)value.FixedArguments[0].Value!] = (string)value.FixedArguments[1].Value!;
        }
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var handle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (AttributeName(reader, attribute) != "LexiconDefinitionAttribute") continue;
                var reference = (string)attribute.DecodeValue(provider).FixedArguments[0].Value!;
                _definitions[reference] = (FullName(reader, type), KindOf(reader, type));
            }
        }
    }

    private static string? AttributeName(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference) return null;
        var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference) return null;
        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        return reader.GetString(type.Namespace) == RuntimeNamespace ? reader.GetString(type.Name) : null;
    }

    private static string FullName(MetadataReader reader, TypeDefinition type) =>
        type.GetDeclaringType() is { IsNil: false } outer
            ? FullName(reader, reader.GetTypeDefinition(outer)) + "." + reader.GetString(type.Name)
            : reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);

    // Records implement IAtProtoRecord<T>, known values IKnownValue<T>, unions IUnion; everything else is an object.
    private static ExternalTypeKind KindOf(MetadataReader reader, TypeDefinition type)
    {
        foreach (var handle in type.GetInterfaceImplementations())
        {
            var implemented = reader.GetInterfaceImplementation(handle).Interface;
            var name = implemented.Kind switch
            {
                HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)implemented).Name),
                HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)implemented).Name),
                HandleKind.TypeSpecification => GenericName(reader, (TypeSpecificationHandle)implemented),
                _ => "",
            };
            switch (name)
            {
                case "IAtProtoRecord`1":
                    return ExternalTypeKind.Record;
                case "IKnownValue`1":
                    return ExternalTypeKind.KnownValues;
                case "IUnion":
                    return ExternalTypeKind.Union;
            }
        }
        return ExternalTypeKind.Object;
    }

    private static string GenericName(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return "";
        blob.ReadSignatureTypeCode();
        var generic = blob.ReadTypeHandle();
        return generic.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)generic).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)generic).Name),
            _ => "",
        };
    }

    // Attribute arguments as text: strings as themselves, System.Type as its serialized name.
    private sealed class TypeNames : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullName(reader, reader.GetTypeDefinition(handle));
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Namespace) + "." + reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSerializedName(string name) => name.Split(',')[0].Replace('+', '.');
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
