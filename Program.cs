var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () =>
    Results.Ok(new { status = "healthy" }));

app.MapGet("/version", () =>
{
    var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
    return Results.Ok(new { version });
});

app.Run();
