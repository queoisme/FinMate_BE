using Xunit;

namespace FinMate.Tests.Integration.Controllers;

// AuthApiFactory injects config via process-wide Environment.SetEnvironmentVariable (DATABASE_URL,
// REDIS_URL, JWT_SECRET, ...) so that FinMate.API.Program's early builder.Configuration[...] reads
// pick it up. That is inherently unsafe if two factory instances (one per test class) initialize
// concurrently — xunit runs different collections in parallel by default. Putting every integration
// test class in this single collection forces them to run sequentially, avoiding the race.
[CollectionDefinition("Integration")]
public class IntegrationTestCollection
{
}
