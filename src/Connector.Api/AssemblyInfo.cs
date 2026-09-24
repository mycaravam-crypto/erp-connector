using System.Runtime.CompilerServices;

// Lets Connector.Integration.Tests call ImportDefinitionEndpoints.ValidateRequestAsync
// (internal — the save-time AllowedWritableColumns validator) directly against a real Postgres schema,
// instead of it only being reachable through HTTP.
[assembly: InternalsVisibleTo("Connector.Integration.Tests")]
