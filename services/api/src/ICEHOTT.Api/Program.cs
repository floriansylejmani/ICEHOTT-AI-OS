using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

// Phase 0: no identities, tenant APIs, billing or external AI provider.
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health/live");
app.MapGet("/api/system", () => TypedResults.Ok(new
{
    product = "ICEHOTT AI OS",
    version = "0.1.0",
    phase = "Phase 0",
    environment = app.Environment.EnvironmentName
})).ExcludeFromDescription();

app.Run();

public partial class Program { }
