using System;
using System.Collections.Generic;
using System.Linq;

namespace ATProtoNet.Lexicon.CodeGen;

// Method classes, descriptors and parameters (LEX-60–LEX-65).
internal sealed partial class Emitter
{
    private const string Xrpc = "global::ATProtoNet.Xrpc.";
    private const string Empty = Xrpc + "XrpcEmpty";

    // What a method takes and returns, shared by its descriptor and its namespace-client method.
    private sealed record MethodShape(string Kind, string? Parameters, bool ParametersOptional, string? Input, bool InputOptional, string? Output, CsType? Item, string Descriptor);

    private MethodShape Shape(TypeDecl method)
    {
        var (kind, parameters, input, output, message) = method.Schema switch
        {
            LexQuery q => ("query", q.Parameters, null, q.Output, null),
            LexProcedure p => ("procedure", p.Parameters, p.Input, p.Output, null),
            LexSubscription s => ("subscription", s.Parameters, (LexBody?)null, (LexBody?)null, s.Message),
            _ => throw new InvalidOperationException(method.Path),
        };
        var id = method.Document.Id;
        string? Body(LexBody? body, TypeDecl? decl, string binary) =>
            body is null ? null
            : body.Encoding != "application/json" && kind != "subscription" ? binary
            : decl is not null ? decl.Ref
            : body.Schema is { } schema ? plan.TypeOf(schema, id, method.Path).Name
            : "global::ATProtoNet.Data.DataModelValue";
        var parametersType = method.Parameters?.Ref;
        var inputType = Body(input, method.Input, Xrpc + "XrpcBinaryContent");
        var outputType = Body(output ?? message, method.Output ?? method.Message, Xrpc + "XrpcBinaryResponse");
        var item = method.Output is { } outputDecl && method.Parameters is not null && Page(Properties(outputDecl)) is { } page
            && parameters!.Properties.TryGetValue("cursor", out var cursor) && cursor is LexString ? page.Type.ItemType : (CsType?)null;
        var descriptor = kind switch
        {
            "query" => $"{Xrpc}XrpcQuery<{parametersType ?? Empty}, {outputType ?? Empty}>",
            "subscription" => $"{Xrpc}XrpcSubscription<{parametersType ?? Empty}, {outputType ?? "global::ATProtoNet.Data.DataModelValue"}>",
            _ when parametersType is not null => $"{Xrpc}XrpcProcedure<{parametersType}, {inputType ?? Empty}, {outputType ?? Empty}>",
            _ when outputType is null => $"{Xrpc}XrpcProcedure<{inputType ?? Empty}>",
            _ => $"{Xrpc}XrpcProcedure<{inputType ?? Empty}, {outputType}>",
        };
        return new(kind, parametersType, parameters is null || parameters.Required.Count == 0, inputType,
            input?.Schema is LexObject { Required.Count: 0 } && input.Encoding == "application/json", outputType, item, descriptor);
    }

    private void Method(CodeWriter writer, TypeDecl decl)
    {
        var shape = Shape(decl);
        var id = decl.Document.Id.ToString();
        var (input, output, errors) = decl.Schema switch
        {
            LexQuery q => (null, q.Output, q.Errors),
            LexProcedure p => (p.Input, p.Output, p.Errors),
            LexSubscription s => ((LexBody?)null, s.Message, s.Errors),
            _ => throw new InvalidOperationException(decl.Path),
        };
        var arguments = new List<string> { $"nsid: {Naming.Literal(id)}" };
        if (input is not null)
        {
            arguments.Add($"inputEncoding: {Naming.Literal(input.Encoding)}");
            if (input.Encoding == "application/json") arguments.Add($"input: {TypeInfo(decl.Unit, new(shape.Input!))}");
        }
        if (output is not null && shape.Kind != "subscription")
        {
            arguments.Add($"outputEncoding: {Naming.Literal(output.Encoding)}");
            if (output.Encoding == "application/json") arguments.Add($"output: {TypeInfo(decl.Unit, new(shape.Output!))}");
        }
        if (shape.Kind == "subscription" && shape.Output is not null) arguments.Add($"message: {TypeInfo(decl.Unit, new(shape.Output))}");
        if (errors.Count > 0) arguments.Add($"errors: [{string.Join(", ", errors.Select(e => Naming.Literal(e.Name)))}]");
        if (plan.Proxy(id) is { } proxy) arguments.Add($"defaultProxy: {Naming.Literal(proxy)}");

        Docs(writer, decl.Schema!.Description, $"The XRPC {shape.Kind} <c>{id}</c>.");
        Attributes(writer, null, decl.Schema.Description, decl.Experimental);
        using (writer.Block($"{Visibility} static partial class {decl.Name}"))
        {
            writer.Line($"/// <summary>The descriptor of <c>{id}</c>: NSID, encodings, declared errors and contracts.</summary>")
                .Line($"public static {shape.Descriptor} Descriptor {{ get; }} = new({string.Join(", ", arguments)});");
            foreach (var nested in decl.Nested)
            {
                writer.Line();
                Decl(writer, nested);
            }
            if (errors.Count == 0) return;
            writer.Line()
                .Line($"/// <summary>The errors <c>{id}</c> declares, for <c>XrpcException.Is</c>.</summary>");
            using (writer.Block("public static class Errors"))
                foreach (var error in errors)
                {
                    writer.Separate();
                    Docs(writer, error.Description, $"The error <c>{Naming.Xml(error.Name)}</c>.");
                    writer.Line($"public const string {Naming.Pascal(error.Name)} = {Naming.Literal(error.Name)};");
                }
        }
    }

    // A Parameters record: query parameters with a generated writer and reader (LEX-63, LEX-70).
    private void Parameters(CodeWriter writer, TypeDecl decl)
    {
        var properties = Properties(decl);
        var interfaces = $"{Runtime}IXrpcParameters<{decl.Name}>";
        if (properties.Any(p => p is { Key: "cursor", Required: false, Type.Name: "string" })) interfaces += $", {Runtime}ICursorParameters<{decl.Name}>";
        Docs(writer, decl.Schema!.Description, $"The parameters of <c>{decl.Document.Id}</c>.");
        using (writer.Block($"{Visibility} sealed partial record {decl.Name} : {interfaces}"))
        {
            foreach (var property in properties)
            {
                Docs(writer, property.Schema.Description, $"The <c>{property.Key}</c> parameter.", Constraints(property.Schema));
                if (!property.Required) Obsolete(writer, property.Schema.Description);
                writer.Line(property.Required ? $"public required {property.Type.Name} {property.Name} {{ get; init; }}" : $"public {property.Type.Name}? {property.Name} {{ get; init; }}")
                    .Line();
            }
            writer.Line("/// <inheritdoc />");
            using (writer.Block($"public void WriteTo({Runtime}XrpcParameterWriter writer)"))
                foreach (var property in properties)
                    writer.Line(property.Type.IsList
                        ? $"writer.AddAll({Naming.Literal(property.Key)}, {property.Name}, static v => {Format(property.Type.ItemType, "v")});"
                        : property.Required
                            ? $"writer.Add({Naming.Literal(property.Key)}, {Format(property.Type, property.Name)});"
                            : $"if ({property.Name} is {{ }} {Camel(property.Name)}) writer.Add({Naming.Literal(property.Key)}, {Format(property.Type, Camel(property.Name))});");
            writer.Line()
                .Line("/// <inheritdoc />")
                .Line($"public static {decl.Name} ReadFrom({Runtime}XrpcParameterReader reader) => new()")
                .Line("{");
            foreach (var property in properties) writer.Line($"    {property.Name} = {Read(property)},");
            writer.Line("};")
                .Line()
                .Line("/// <summary>The type name only (LEX-74).</summary>")
                .Line("/// <returns>The text.</returns>")
                .Line($"public override string ToString() => {Naming.Literal(TypeName(decl))};");
        }
    }

    private static string Camel(string name) => "@" + char.ToLowerInvariant(name[0]) + name.Substring(1);

    private string Format(CsType type, string value) => type.Name switch
    {
        "string" => value,
        "bool" => $"{value} ? \"true\" : \"false\"",
        "int" or "long" => $"{value}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)",
        _ when type.IsKnownValues => $"{value}.Value",
        _ => $"{value}.ToString()",
    };

    private string Parser(CsType type) => type.Name switch
    {
        "string" => "static t => t",
        "bool" => $"{Runtime}XrpcParameterReader.ParseBoolean",
        "long" => $"{Runtime}XrpcParameterReader.ParseInt64",
        "int" => $"static t => checked((int){Runtime}XrpcParameterReader.ParseInt64(t))",
        _ when type.IsKnownValues => $"{type.Name}.Create",
        _ => $"{type.Name}.Parse",
    };

    private string Read(Property property)
    {
        var key = Naming.Literal(property.Key);
        var type = property.Type;
        var value = type.IsList ? $"reader.GetAll({key}, {Parser(type.ItemType)})"
            : type.Name == "string" ? $"reader.GetString({key})"
            : type.IsValueType ? $"reader.GetValue({key}, {Parser(type)})"
            : $"reader.Get({key}, {Parser(type)})";
        return !property.Required ? value
            : type.IsValueType && !type.IsList ? $"{Runtime}XrpcParameterReader.RequiredValue({key}, {value})"
            : $"{Runtime}XrpcParameterReader.Required({key}, {value})";
    }
}
