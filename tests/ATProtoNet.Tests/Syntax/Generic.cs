using System.Reflection;

namespace ATProtoNet.Tests.Syntax;

// Runs a private static generic method of a test class for a type known only at run time, so a theory can range over the identifier types.
internal static class Generic
{
    public static object? Invoke(Type host, string method, Type type, params object[] args) =>
        host.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, null, args, null);
}
