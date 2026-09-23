// DiagnosticLog is process-wide. The shell modes subscribe to it and forward every Warning to
// their own output, so a warning raised by a test class running in parallel would land inside
// another class's transcript (and a StringWriter is not thread-safe). The whole suite runs in
// well under a second serially; determinism is worth more than the parallel speed-up.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
