namespace BlindSpotScanner.Domain.Enums;

public enum FindingKind
{
    UncoveredEndpoint,
    WeakTest,
    FalsePositive,
    SkippedTest,
    UncoveredSeam,
    Risk,
    NoAssertion,
    UncheckedFields,
    ErrorWithoutStateCheck,
}
