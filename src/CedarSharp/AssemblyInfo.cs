using System.Runtime.CompilerServices;

// Managed integration tests may construct results/inputs from raw JSON to exercise
// response-contract decoding without making malformed shapes public API.
[assembly: InternalsVisibleTo("CedarSharp.Tests")]
