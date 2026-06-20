using Xunit;

// Integration tests spin up a web host (and, in SQL mode, a dedicated LocalDB database) per
// test class. Running them serially keeps rate-limiter windows, timing measurements, and
// per-class databases from contending with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
