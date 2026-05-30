using PerftChecker.Epd;

namespace PerftChecker.Runner;

public enum CaseStatus { Pass, Mismatch, Timeout, EngineError }

public sealed record CaseResult(
    EpdCase           Case,
    CaseStatus        Status,
    ulong?            Actual,
    double            ElapsedSeconds,
    string?           ErrorMessage,
    string?           RawEngineOutput,
    DrillDownReport?  DrillDown = null);
