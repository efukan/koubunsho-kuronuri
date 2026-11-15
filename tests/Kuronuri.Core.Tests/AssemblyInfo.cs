using Xunit;

// PDFium is not thread-safe; the tests share one process-wide library instance.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
