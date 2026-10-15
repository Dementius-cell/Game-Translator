namespace GameTranslator.Tests.Smoke;

// WPF pack-URI resource packages are process-wide even when each test owns an STA thread.
// Concurrent window/theme loading can corrupt PackagePart's requested-stream bookkeeping.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfResourceCollection
{
    public const string Name = "WPF resource loading";
}
