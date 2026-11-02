using Xunit;

// ActivityListener and MeterListener observe process-wide sources. Serializing the unit suite keeps
// instrumentation assertions deterministic without changing product behavior.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
