using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ATProtoNet.Architecture.Tests;

/// <summary>A type of an assembly and what it references: types by full name, members as <c>Type::member</c>.</summary>
public sealed record TypeUsage(TypeDefinition Type, IReadOnlySet<string> Types, IReadOnlySet<string> Members);

/// <summary>Reads every type's references out of an assembly: signatures, attributes and method bodies.</summary>
public static class Dependencies
{
    /// <summary>Every type of <paramref name="module"/>, nested types included, with its references.</summary>
    public static IEnumerable<TypeUsage> Of(ModuleDefinition module) =>
        module.GetTypes().Where(t => t.Name != "<Module>").Select(Of);

    private static TypeUsage Of(TypeDefinition type)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        void Add(TypeReference? reference)
        {
            switch (reference)
            {
                case null or GenericParameter: return;
                case GenericInstanceType generic:
                    Add(generic.ElementType);
                    foreach (var argument in generic.GenericArguments) Add(argument);
                    return;
                case TypeSpecification specification: Add(specification.ElementType); return;
                default: types.Add(reference.FullName); return;
            }
        }
        void AddAttributes(ICustomAttributeProvider provider)
        {
            foreach (var attribute in provider.CustomAttributes) Add(attribute.AttributeType);
        }
        void AddMember(MemberReference member, TypeReference memberType)
        {
            Add(member.DeclaringType);
            Add(memberType);
            members.Add($"{member.DeclaringType.FullName}::{member.Name}");
        }

        Add(type.BaseType);
        AddAttributes(type);
        foreach (var i in type.Interfaces) Add(i.InterfaceType);
        foreach (var p in type.GenericParameters) foreach (var c in p.Constraints) Add(c.ConstraintType);
        foreach (var f in type.Fields) { Add(f.FieldType); AddAttributes(f); }
        foreach (var p in type.Properties) { Add(p.PropertyType); AddAttributes(p); }
        foreach (var e in type.Events) Add(e.EventType);
        foreach (var method in type.Methods)
        {
            AddAttributes(method);
            Add(method.ReturnType);
            foreach (var p in method.Parameters) Add(p.ParameterType);
            foreach (var p in method.GenericParameters) foreach (var c in p.Constraints) Add(c.ConstraintType);
            if (!method.HasBody) continue;
            foreach (var v in method.Body.Variables) Add(v.VariableType);
            foreach (var h in method.Body.ExceptionHandlers) Add(h.CatchType);
            foreach (var instruction in method.Body.Instructions)
                switch (instruction.Operand)
                {
                    case TypeReference t: Add(t); break;
                    case MethodReference m:
                        AddMember(m, m.ReturnType);
                        foreach (var p in m.Parameters) Add(p.ParameterType);
                        if (m is GenericInstanceMethod g) foreach (var a in g.GenericArguments) Add(a);
                        break;
                    case FieldReference f: AddMember(f, f.FieldType); break;
                }
        }
        types.Remove(type.FullName);
        return new(type, types, members);
    }
}
