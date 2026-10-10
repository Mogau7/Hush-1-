using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;

var b = WebApplication.CreateBuilder(args);
b.Services.AddHttpClient("mint", c => {
    c.BaseAddress = new Uri(b.Configuration["MINT_URL"] ?? "http://localhost:8081");
    c.Timeout = TimeSpan.FromSeconds(5);
});
var origins = (b.Configuration["ALLOWED_ORIGINS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
b.Services.AddCors(o => o.AddDefaultPolicy(p => {
    if (origins.Length > 0) p.WithOrigins(origins); else p.AllowAnyOrigin();
    p.WithHeaders("Content-Type").WithMethods("GET", "POST");
}));
b.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});
var app = b.Build();
app.UseCors();
app.UseRateLimiter();
app.UseExceptionHandler(e => e.Run(async ctx => {
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Something went wrong." });
}));
var cs = b.Configuration.GetConnectionString("db")!;
await InitDb();

app.MapGet("/health", () => "ok");

app.MapGet("/api/leaderboard", async () => {
    using var c = new SqlConnection(cs);
    return await c.QueryAsync<LbRow>("SELECT TOP 10 Operative, Fragments, Scans, Since FROM vw_Leaderboard ORDER BY Fragments DESC, Since");
});

app.MapGet("/api/stats", async () => {
    using var c = new SqlConnection(cs);
    return new {
        scans = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Scans"),
        operatives = await c.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT Operative) FROM Scans")
    };
});

app.MapPost("/api/scan", async (ScanReq r, IHttpClientFactory f) => {
    var op = (r.Operative ?? "").Trim();
    var code = (r.Code ?? "").Trim().ToUpperInvariant();
    if (op.Length is < 1 or > 30 || code.Length is < 5 or > 40 || !Regex.IsMatch(code, "^[A-Z0-9-]+$"))
        return Results.BadRequest(new { error = "SIGNAL LOST" });
    Val? v;
    try { v = await f.CreateClient("mint").GetFromJsonAsync<Val>($"/validate?code={Uri.EscapeDataString(code)}"); }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
    { return Results.Json(new { error = "CODE SERVICE DOWN" }, statusCode: 503); }
    if (v is null || !v.valid) return Results.BadRequest(new { error = "SIGNAL LOST" });
    using var c = new SqlConnection(cs);
    try { await c.ExecuteAsync("dbo.sp_RecordScan", new { Op = op, Code = code, Flavor = v.flavor }, commandType: System.Data.CommandType.StoredProcedure); }
    catch (SqlException e) when (e.Number is 2627 or 2601) { return Results.Conflict(new { error = "CODE ALREADY BURNED" }); }
    var frag = await c.QuerySingleAsync<string>("SELECT Fragment FROM Flavors WHERE Code=@F", new { F = v.flavor });
    var n = await c.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT FlavorCode) FROM Scans WHERE Operative=@O", new { O = op });
    return Results.Ok(new { fragment = frag, flavor = v.flavor, collected = n, unlocked = n >= 3 });
});

app.MapPost("/api/orders", async (OrderReq o) => {
    string[] ok = { "MIX", "ECL", "STA", "GHO" };
    var name = (o.Name ?? "").Trim(); var email = (o.Email ?? "").Trim();
    if (name.Length is < 1 or > 60 || email.Length > 120 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")
        || !ok.Contains(o.Flavor ?? "") || o.Qty is < 1 or > 5)
        return Results.BadRequest(new { error = "Check your details." });
    using var c = new SqlConnection(cs);
    var id = await c.ExecuteScalarAsync<int>("INSERT Orders(Name,Email,Flavor,Cases) OUTPUT INSERTED.Id VALUES(@Name,@Email,@Flavor,@Qty)",
        new { Name = name, Email = email, o.Flavor, o.Qty });
    return Results.Ok(new { reference = $"HSH-{id:D5}" });
});

app.Run();

async Task InitDb()
{
    var db = new SqlConnectionStringBuilder(cs).InitialCatalog;
    var batches = Regex.Split(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema.sql")), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    for (var i = 0; ; i++)
    {
        try
        {
            try
            {
                using var mc = new SqlConnection(new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString);
                await mc.ExecuteAsync("IF DB_ID(@d) IS NULL BEGIN DECLARE @s nvarchar(300)=N'CREATE DATABASE '+QUOTENAME(@d); EXEC(@s) END", new { d = db });
            }
            catch (SqlException e) when (e.Number is 229 or 262 or 40508 or 40615) { /* hosted SQL: database already provided */ }
            using var c = new SqlConnection(cs);
            foreach (var batch in batches) await c.ExecuteAsync(batch);
            app.Logger.LogInformation("Database ready.");
            return;
        }
        catch (Exception e) when (i < 60)
        {
            app.Logger.LogInformation("Waiting for the database ({Msg})", e.Message);
            await Task.Delay(2000);
        }
    }
}

record ScanReq(string? Operative, string? Code);
record Val(bool valid, string flavor);
record LbRow(string Operative, int Fragments, int Scans, DateTime Since);
record OrderReq(string? Name, string? Email, string? Flavor, int Qty);
