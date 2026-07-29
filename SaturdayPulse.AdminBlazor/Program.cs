using MudBlazor.Services;
using SaturdayPulse.AdminBlazor.Components;
using SaturdayPulse.AdminBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor Components + global Interactive Server render mode
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Typed HttpClient for the API, base URL comes from appsettings / appsettings.Development
builder.Services.AddHttpClient<AdminApiService>(client =>
{
    var baseUrl = builder.Configuration["AdminApi:BaseUrl"]
        ?? throw new InvalidOperationException("AdminApi:BaseUrl is not configured.");

    // HttpClient.BaseAddress combines with relative request URIs using standard
    // Uri rules: without a trailing slash, the last segment (here "api") is
    // treated as replaceable and gets dropped instead of kept. Every AdminApiService
    // call uses a relative path with no leading slash specifically so this
    // trailing-slash normalization is what keeps "/api" in the final request URL.
    if (!baseUrl.EndsWith('/')) baseUrl += "/";

    client.BaseAddress = new Uri(baseUrl);

    // Default HttpClient.Timeout is 100s — nowhere near enough for a 60-year
    // historical backfill. Streaming endpoints make this less critical than it
    // used to be (you SEE it's alive instead of it silently timing out), but the
    // timeout still applies to the full duration of the request, streaming or not.
    client.Timeout = TimeSpan.FromMinutes(45);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
