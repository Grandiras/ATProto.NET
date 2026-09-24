namespace ATProtoNet.Serialization;

/// <summary>
/// A bundle of Lexicon union variants, for distributing custom Lexicons as a NuGet package.
/// Load it with <see cref="LexiconTypeRegistry.LoadPlugin{TPlugin}"/>.
/// </summary>
/// <example>
/// <code>
/// public class MyAppLexicons : ILexiconPlugin
/// {
///     public void Register(ILexiconTypeRegistrar registrar)
///     {
///         registrar.RegisterUnionVariant&lt;EmbedBase, CustomEmbed&gt;("com.example.embed.custom");
///     }
/// }
///
/// LexiconTypeRegistry.Instance.LoadPlugin&lt;MyAppLexicons&gt;();
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
    /// Registers a variant of a Lexicon union base, in addition to the variants the base declares
    /// with <c>[JsonDerivedType]</c>.
    /// </summary>
    /// <typeparam name="TBase">The union base type (e.g., <c>EmbedBase</c>).</typeparam>
    /// <typeparam name="TDerived">The new derived type.</typeparam>
    /// <param name="typeDiscriminator">The <c>$type</c> discriminator value.</param>
    void RegisterUnionVariant<TBase, TDerived>(string typeDiscriminator)
        where TBase : class
        where TDerived : class, TBase;
}
