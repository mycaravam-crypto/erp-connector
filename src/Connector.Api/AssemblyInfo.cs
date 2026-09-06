using System.Runtime.CompilerServices;

// Phase 17 Slice 5: lets Connector.Integration.Tests call ImportDefinitionEndpoints.ValidateRequestAsync
// (internal — the save-time AllowedWritableColumns validator) directly against a real Postgres schema,
// instead of it only being reachable through HTTP.
[assembly: InternalsVisibleTo("Connector.Integration.Tests")]
