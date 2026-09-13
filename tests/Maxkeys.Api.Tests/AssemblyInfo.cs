using Xunit;

// PR10 adds a second and third WebApplicationFactory<Program> fixture (Hs256ApiTestFixture,
// JwksApiTestFixture) alongside the existing ApiTestFixture. Each boots the real Program.Main,
// which reassigns Serilog's static ambient Log.Logger (SerilogSetup.Bootstrap) and later freezes
// it (Host.Build -> AddSerilog). xUnit runs different collections in parallel by default, so two
// hosts booting concurrently can freeze a bootstrap logger instance the other collection's host
// is still resolving, throwing "The logger is already frozen." Disabling parallelization keeps
// collections sequential, which is the safe option given this shared, ambient static — the
// alternative (a real per-host isolated logger) is a larger Serilog wiring change out of scope
// for this PR.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
