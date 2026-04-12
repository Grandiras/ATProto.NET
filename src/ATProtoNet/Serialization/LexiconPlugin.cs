using System.Reflection;

namespace ATProtoNet.Serialization;

/// <summary>
/// Assembly-level attribute marking a NuGet package / assembly as a Lexicon plugin
/// that contributes AT Protocol record types, union variants, or XRPC endpoint models.
/// </summary>
/// <example>
/// <code>
/// [assembly: LexiconPlugin(typeof(MyLexiconPlugin))]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class LexiconPluginAttribute : Attribute
{
    /// <summary>
    /// The type implementing <see cref="ILexiconPlugin"/> that registers types.
    /// </summary>
    public Type PluginType { get; }

    public LexiconPluginAttribute(Type pluginType)
    {
        ArgumentNullException.ThrowIfNull(pluginType);
        if (!pluginType.IsAssignableTo(typeof(ILexiconPlugin)))
            throw new ArgumentException($"Type '{pluginType.Name}' must implement {nameof(ILexiconPlugin)}.", nameof(pluginType));

        PluginType = pluginType;
    }
}

/// <summary>
/// Interface for Lexicon plugins that register custom record types and union variants.
/// Implement this to distribute custom lexicons as NuGet packages with auto-registration.
/// </summary>
/// <example>
/// <code>
/// public class MyAppLexicons : ILexiconPlugin
/// {
///     public void Register(ILexiconTypeRegistrar registrar)
///     {
///         registrar.RegisterRecordType&lt;TodoItem&gt;("com.example.todo.item");
///         registrar.RegisterUnionVariant&lt;EmbedBase, CustomEmbed&gt;("com.example.embed.custom");
///     }
/// }
/// </code>
/// </example>
public interface ILexiconPlugin
{
    /// <summary>
    /// Called during initialization to register custom lexicon types.
    /// </summary>
    /// <param name="registrar">The registrar for adding types.</param>
    void Register(ILexiconTypeRegistrar registrar);
}

/// <summary>
/// Provides methods for plugins to register their lexicon types.
/// </summary>
public interface ILexiconTypeRegistrar
{
    /// <summary>
    /// Registers a record type that can be deserialized from AT Protocol records.
    /// The type should have a <c>$type</c> property matching the NSID.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="nsid">The Lexicon NSID (e.g., "com.example.todo.item").</param>
    void RegisterRecordType<T>(string nsid) where T : class;

    /// <summary>
    /// Registers a derived type for a polymorphic union base type.
    /// This extends the built-in <c>[JsonDerivedType]</c> mappings at runtime.
    /// </summary>
    /// <typeparam name="TBase">The union base type (e.g., <c>EmbedBase</c>).</typeparam>
    /// <typeparam name="TDerived">The new derived type.</typeparam>
    /// <param name="typeDiscriminator">The <c>$type</c> discriminator value.</param>
    void RegisterUnionVariant<TBase, TDerived>(string typeDiscriminator)
        where TBase : class
        where TDerived : class, TBase;
}
