using System;

namespace UhfPrime.TestBench.ViewModels;

public sealed record FrameLogEntry(DateTimeOffset Timestamp, string Direction, string CommandSummary, string HexPayload);
