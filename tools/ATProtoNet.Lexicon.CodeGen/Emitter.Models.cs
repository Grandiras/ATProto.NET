using System;
using System.Collections.Generic;
using System.Linq;

namespace ATProtoNet.Lexicon.CodeGen;

// Objects, records, unions and known values (LEX-38–LEX-59, LEX-74).
internal sealed partial class Emitter
{
    // The members every generated object has; a Lexicon property that would clash gets a "Value" suffix (LEX-35).
    private static readonly string[] ReservedMembers =
        ["ExtensionData", "LexiconType", "JsonTypeInfo", "Collection", "KeyType", "LexiconTypeTag", "ToString", "Equals", "GetHashCode", "EqualityContract", "Items", "WriteTo", "ReadFrom", "WithCreatedAt"];

    private sealed record Property(string Key, string Name, LexSchema Schema, CsType Type, bool Required, bool Nullable, string Path);

    private List<Property> Properties(TypeDecl decl)
    {
        var (properties, required, nullable) = decl.Schema switch
        {
            LexRecord r => (r.Record.Properties, r.Record.Required, r.Record.Nullable),
            LexObject o => (o.Properties, o.Required, o.Nullable),
            LexParams p => (p.Properties, p.Required, (IReadOnlyList<string>)[]),
            _ => throw new InvalidOperationException(decl.Path),
        };
        var nested = new HashSet<string>(decl.Nested.Select(n => n.Name), StringComparer.Ordinal);
        return
        [
            .. properties.Select(p =>
            {
                var path = decl.Path + "." + p.Key;
                var name = plan.Inline(p.Value) is null ? plan.Rename(path, Naming.Pascal(p.Key)) : Naming.Pascal(p.Key);
                while (name == decl.Name || nested.Contains(name) || Array.IndexOf(ReservedMembers, name) >= 0) name += "Value";
                return new Property(p.Key, name, p.Value, plan.TypeOf(p.Value, decl.Document.Id, path), required.Contains(p.Key), nullable.Contains(p.Key), path);
            }),
        ];
    }

    private string Fallback(TypeDecl decl) => decl.LexiconType is { } type ? $"The Lexicon definition <c>{type}</c>." : $"The <c>{decl.Path}</c> object.";

    private void Object(CodeWriter writer, TypeDecl decl)
    {
        var schema = decl.Schema is LexRecord record ? record.Record : decl.Schema!;
        var properties = Properties(decl);
        var self = decl.Name;
        var interfaces = new List<string>();
        if (decl.Kind == DeclKind.Record) interfaces.Add($"{Runtime}IAtProtoRecord<{self}>");
        else if (decl.LexiconType is not null) interfaces.Add($"{Runtime}ILexiconType<{self}>");
        else interfaces.Add($"{Runtime}ILexiconObject");
        var createdAt = properties.FirstOrDefault(p => p is { Key: "createdAt", Required: false, Type.Name: "global::ATProtoNet.AtDatetime" });
        if (decl.Kind == DeclKind.Record && createdAt is not null) interfaces.Add($"{Runtime}ICreatedAtRecord<{self}>");
        var page = decl.Name == "Output" ? Page(properties) : null;
        if (page is not null) interfaces.Add($"{Runtime}ICursorPage<{page.Type.ItemType.Name}>");

        Docs(writer, decl.Schema!.Description ?? schema.Description, Fallback(decl));
        Attributes(writer, decl.LexiconType, decl.Schema.Description, decl.Experimental);
        using (writer.Block($"{Visibility} sealed partial record {self} : {string.Join(", ", interfaces)}"))
        {
            if (decl.LexiconType is { } type)
            {
                writer.Line($"/// <summary>The <c>$type</c> of <c>{type}</c>.</summary>")
                    .Line($"public static string LexiconType => {Naming.Literal(type)};")
                    .Line()
                    .Line("/// <summary>The source-generated contract.</summary>")
                    .Line($"public static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{self}> JsonTypeInfo => {TypeInfo(decl.Unit, Planner.Of(decl))};")
                    .Line();
            }
            else Require(decl.Unit, Planner.Of(decl));
            if (decl.Kind == DeclKind.Record)
            {
                var key = ((LexRecord)decl.Schema).Key;
                var keyType = key.Literal is { } literal ? $"global::ATProtoNet.Lexicon.RecordKeyType.ForLiteral({Naming.Literal(literal)})" : "global::ATProtoNet.Lexicon.RecordKeyType." + Naming.Pascal(key.Text);
                writer.Line("/// <summary>The collection.</summary>")
                    .Line($"public static global::ATProtoNet.Nsid Collection {{ get; }} = global::ATProtoNet.Nsid.Parse({Naming.Literal(decl.LexiconType!)});")
                    .Line()
                    .Line($"/// <summary>The record key type: <c>{key.Text}</c>.</summary>")
                    .Line($"public static global::ATProtoNet.Lexicon.RecordKeyType KeyType {{ get; }} = {keyType};")
                    .Line();
                if (createdAt is not null)
                    writer.Line("/// <inheritdoc />")
                        .Line($"public static {self} WithCreatedAt({self} record, global::ATProtoNet.AtDatetime createdAt) => record with {{ {createdAt.Name} = createdAt }};")
                        .Line();
            }
            // Absorbs an incoming $type, so it never lands in ExtensionData, and leads a record's JSON with its own (LEX-42, LEX-57).
            var tag = decl.Kind == DeclKind.Record ? $"string LexiconTypeTag {{ get => {Naming.Literal(decl.LexiconType!)}; init {{ }} }}" : "string? LexiconTypeTag { get => null; init { } }";
            writer.Line($"[{Json}JsonInclude, {Json}JsonPropertyName(\"$type\"), {Json}JsonPropertyOrder(-1)]")
                .Line("internal " + tag);
            foreach (var property in properties)
            {
                writer.Line();
                JsonProperty(writer, decl, property);
            }
            writer.Line()
                .Line("/// <summary>Properties this version of the schema does not declare, written back unchanged.</summary>")
                .Line($"[{Json}JsonExtensionData]")
                .Line($"public global::System.Collections.Generic.IDictionary<string, {JsonElement}>? ExtensionData {{ get; init; }}");
            if (page is not null)
                writer.Line()
                    .Line($"global::System.Collections.Generic.IReadOnlyList<{page.Type.ItemType.Name}> {Runtime}ICursorPage<{page.Type.ItemType.Name}>.Items => {page.Name}{(page.Required ? "" : " ?? []")};");
            writer.Line()
                .Line("/// <summary>The type name and <c>$type</c> only: Lexicon cannot mark secrets, so no member is printed (LEX-74).</summary>")
                .Line("/// <returns>The text.</returns>")
                .Line($"public override string ToString() => {Naming.Literal($"{TypeName(decl)} {{ $type = {decl.LexiconType ?? decl.Path} }}")};");
            foreach (var nested in decl.Nested)
            {
                writer.Line();
                Decl(writer, nested);
            }
        }
    }

    private static string TypeName(TypeDecl decl) => decl.Parent is { } parent ? TypeName(parent) + "." + decl.Name : decl.Name;

    // LEX-70: an output with an optional string cursor and exactly one array.
    private static Property? Page(List<Property> properties) =>
        properties.Any(p => p is { Key: "cursor", Required: false, Type.Name: "string" }) && properties.Count(p => p.Type.IsList) == 1
            ? properties.Single(p => p.Type.IsList)
            : null;

    private void JsonProperty(CodeWriter writer, TypeDecl decl, Property property)
    {
        Docs(writer, property.Schema.Description, $"The <c>{property.Key}</c> property.", Constraints(property.Schema));
        if (!property.Required) Obsolete(writer, property.Schema.Description); // C# refuses [Obsolete] on required members
        var attributes = $"{Json}JsonPropertyName({Naming.Literal(property.Key)})";
        var type = property.Type.Name;
        if (property.Type.IsList) Require(decl.Unit, property.Type);
        string declaration;
        if (property.Type.IsUnion)
        {
            Require(decl.Unit, property.Type);
            if (property.Required && !property.Nullable)
            {
                attributes += $", {Json}JsonConverter(typeof({Runtime}LexiconRequiredUnionConverter<{type}>))";
                declaration = $"public required {type} {property.Name} {{ get; init; }}";
            }
            else
            {
                attributes += $", {Json}JsonIgnore(Condition = {Json}JsonIgnoreCondition.WhenWritingDefault)";
                declaration = $"public {type} {property.Name} {{ get; init; }}";
            }
        }
        else if (property is { Required: true, Nullable: false })
            declaration = $"public required {type} {property.Name} {{ get; init; }}";
        else if (property.Required)
        {
            attributes += $", {Json}JsonIgnore(Condition = {Json}JsonIgnoreCondition.Never)";
            declaration = $"public required {type}? {property.Name} {{ get; init; }}";
        }
        else if (property.Nullable)
        {
            var inner = property.Type.IsValueType ? type + "?" : type;
            Require(decl.Unit, new(inner));
            attributes += $", {Json}JsonIgnore(Condition = {Json}JsonIgnoreCondition.WhenWritingDefault), {Json}JsonConverter(typeof({Runtime}OptionalJsonConverter<{inner}>))";
            declaration = $"public {Runtime}Optional<{type}?> {property.Name} {{ get; init; }}";
        }
        else
        {
            if (property.Type.IsValueType) Require(decl.Unit, new(type + "?"));
            declaration = $"public {type}? {property.Name} {{ get; init; }}";
        }
        writer.Line($"[{attributes}]").Line(declaration);
    }

    private void Union(CodeWriter writer, TypeDecl decl)
    {
        var union = decl.Union!;
        var cases = plan.Cases(union, decl.Document.Id, decl.Path).Select(c => c.Case.Name).ToList();
        if (!union.Closed) cases.Add(Runtime + "LexiconUnknown");
        Docs(writer, union.Description, $"The {(union.Closed ? "closed" : "open")} union <c>{decl.Path}</c>.");
        Attributes(writer, decl.Path, union.Description, decl.Experimental);
        writer.Line($"[{Json}JsonConverter(typeof({decl.Name}Converter))]")
            .Line($"{Visibility} union {decl.Name}({string.Join(", ", cases)});")
            .Line()
            .Line($"internal sealed class {decl.Name}Converter() : {Runtime}LexiconUnionConverter<{decl.Name}>({(union.Closed ? "null" : "static u => new(u)")}" + string.Concat(
                cases.Where(c => c != Runtime + "LexiconUnknown").Select(c => $", Case<{c}>(static v => new(v))")) + ");");
    }

    private void KnownValues(CodeWriter writer, TypeDecl decl)
    {
        var text = (LexString)decl.Schema!;
        var values = text.Enum ?? text.KnownValues!;
        var self = decl.Name;
        var reserved = new[] { self, "Value", "IsKnown", "Create", "ToString", "Equals", "GetHashCode", "Deconstruct" };
        var used = new HashSet<string>(StringComparer.Ordinal);
        Docs(writer, text.Description, $"The {(text.Enum is null ? "known" : "allowed")} values of <c>{decl.Path}</c>.");
        Attributes(writer, decl.Parent is null && decl.Path.IndexOf('.', decl.Path.IndexOf('#')) < 0 ? decl.Path.Replace("#main", "") : null, text.Description, decl.Experimental);
        writer.Line("/// <param name=\"Value\">The string.</param>")
            .Line($"[{Json}JsonConverter(typeof({Runtime}KnownValueJsonConverter<{self}>))]");
        using (writer.Block($"{Visibility} readonly partial record struct {self}(string Value) : {Runtime}IKnownValue<{self}>"))
        {
            foreach (var value in values)
            {
                var hash = value.IndexOf('#');
                var token = hash > 0 && plan.Catalog.TryResolve(value, decl.Document.Id, out var resolved) ? resolved.Definition as LexToken : null;
                var name = Naming.Pascal(hash > 0 ? value.Substring(hash + 1) : value);
                while (Array.IndexOf(reserved, name) >= 0 || !used.Add(name)) name += "Value";
                Docs(writer, token?.Description, $"<c>{Naming.Xml(value)}</c>.");
                Obsolete(writer, token?.Description);
                writer.Line($"public static {self} {name} {{ get; }} = new({Naming.Literal(value)});").Line();
            }
            writer.Line("/// <summary>Whether the value is one the schema lists.</summary>")
                .Line($"public bool IsKnown => Value is {string.Join(" or ", values.Select(Naming.Literal))};")
                .Line()
                .Line("/// <inheritdoc />")
                .Line($"public static {self} Create(string value) => new(value);")
                .Line()
                .Line("/// <summary>The string.</summary>")
                .Line("/// <param name=\"value\">The value.</param>")
                .Line($"public static implicit operator string({self} value) => value.Value;")
                .Line()
                .Line("/// <summary>The value for a string; any string converts.</summary>")
                .Line("/// <param name=\"value\">The string.</param>")
                .Line($"public static implicit operator {self}(string value) => new(value);")
                .Line()
                .Line("/// <inheritdoc />")
                .Line("public override string ToString() => Value;");
        }
    }
}
