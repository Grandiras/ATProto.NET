// Minimal PDS sample — demonstrates hosting an AT Protocol Personal Data Server
// See docs/pds.md for full documentation

using ATProtoNet.Pds;

var builder = WebApplication.CreateBuilder(args);

// Register PDS services with default in-memory stores
builder.Services.AddAtProtoPds(options =>
{
    options.Hostname = "localhost";

    // Persist this in production — without it every restart generates a new signing key
    // and invalidates all issued session tokens. PdsSessionService.GenerateSigningKey()
    // produces a value suitable for storing as a secret.
    options.SessionSigningKey = builder.Configuration["Pds:SessionSigningKey"];
});

var app = builder.Build();

// Map all AT Protocol XRPC endpoints
app.MapAtProtoPds();

app.MapGet("/", () => Results.Text(
    "ATProto.NET PDS Sample\n\n" +
    "Endpoints:\n" +
    "  POST /xrpc/com.atproto.server.createAccount\n" +
    "  POST /xrpc/com.atproto.server.createSession\n" +
    "  GET  /xrpc/com.atproto.server.getSession\n" +
    "  POST /xrpc/com.atproto.server.refreshSession\n" +
    "  GET  /xrpc/com.atproto.server.describeServer\n" +
    "  POST /xrpc/com.atproto.repo.createRecord\n" +
    "  GET  /xrpc/com.atproto.repo.getRecord\n" +
    "  POST /xrpc/com.atproto.repo.putRecord\n" +
    "  POST /xrpc/com.atproto.repo.deleteRecord\n" +
    "  GET  /xrpc/com.atproto.repo.listRecords\n" +
    "  POST /xrpc/com.atproto.repo.uploadBlob\n" +
    "  GET  /xrpc/com.atproto.sync.getBlob\n",
    "text/plain"));

app.Run();
