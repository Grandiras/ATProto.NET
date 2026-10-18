// Deliberate rule violations for LayerRulesTests; nothing else uses these types.
namespace ATProtoNet.Architecture.Samples.Low
{
    public class UsesHigh { public High.Top? Value { get; set; } }

    public class ReadsClock { public static DateTime When() => DateTime.UtcNow; }

    public class Clean { public int Value { get; set; } }
}

namespace ATProtoNet.Architecture.Samples.High
{
    public class Top { public Low.Clean? Value { get; set; } }
}

namespace ATProtoNet.Architecture.Samples.Ring.A
{
    public class First { public B.Second? Next { get; set; } }
}

namespace ATProtoNet.Architecture.Samples.Ring.B
{
    public class Second { public A.First? Next { get; set; } }
}
