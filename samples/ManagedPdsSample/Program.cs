using ATProtoNet.Admin;
using ATProtoNet.Server;

var builder = WebApplication.CreateBuilder(args);

// Binds AtProto:Pds:Url and AtProto:Pds:AdminPassword. In an Aspire solution the
// AppHost supplies both — see README.md. Locally, set them in user secrets:
//
//   dotnet user-secrets set "AtProto:Pds:Url" "http://localhost:3000"
//   dotnet user-secrets set "AtProto:Pds:AdminPassword" "<PDS_ADMIN_PASSWORD>"
builder.AddAtProtoPdsAdmin();

var app = builder.Build();

// What the server will accept: its DID, handle domains, and whether signups need an invite.
app.MapGet("/pds", async (PdsAdminClient pds, CancellationToken ct) =>
{
    var server = await pds.DescribeServerAsync(ct);

    return Results.Ok(new
    {
        server.Did,
        server.AvailableUserDomains,
        InviteRequired = server.InviteCodeRequired ?? false,
    });
});

// Create an account on our own PDS. An invite code is minted automatically when the
// server requires one, so this works whether or not signups are gated.
app.MapPost("/accounts", async (SignupRequest signup, PdsAdminClient pds, CancellationToken ct) =>
{
    var account = await pds.CreateAccountAsync(
        new CreatePdsAccountRequest
        {
            Handle = signup.Handle,
            Email = signup.Email,
            Password = signup.Password,
        },
        ct);

    // account.AccessJwt / account.RefreshJwt is a ready-to-use session for the new
    // user — hand it to AtProtoClient.ResumeSessionAsync to act on their behalf.
    return Results.Created($"/accounts/{account.Did}", new { account.Did, account.Handle });
});

app.MapGet("/accounts/{did}", async (string did, PdsAdminClient pds, CancellationToken ct) =>
{
    var account = await pds.GetAccountAsync(did, ct);

    return Results.Ok(new { account.Did, account.Handle, account.Email, account.IndexedAt });
});

app.MapDelete("/accounts/{did}", async (string did, PdsAdminClient pds, CancellationToken ct) =>
{
    await pds.DeleteAccountAsync(did, ct);
    return Results.NoContent();
});

// Hand an invite code to a user so they can sign up through any AT Protocol client.
app.MapPost("/invites", async (PdsAdminClient pds, CancellationToken ct) =>
{
    var code = await pds.CreateInviteCodeAsync(cancellationToken: ct);
    return Results.Ok(new { code });
});

app.Run();

internal sealed record SignupRequest(string Handle, string Password, string? Email);
