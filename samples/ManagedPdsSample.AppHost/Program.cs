using ATProtoNet.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// The official Bluesky PDS container. Dev mode, a persistent data volume, and
// secrets persisted to this AppHost's user secrets so the accounts created on
// one run are still usable on the next.
var pds = builder.AddAtProtoPds("pds");

// Injects the PDS URL and admin password, and waits for the container to report
// healthy before starting. The API binds them with AddAtProtoPdsAdmin().
builder.AddProject<Projects.ManagedPdsSample>("api")
       .WithAtProtoPds(pds);

builder.Build().Run();
