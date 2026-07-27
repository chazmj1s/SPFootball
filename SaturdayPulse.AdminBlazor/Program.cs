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
