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

    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(45);

    var adminKey = builder.Configuration["AdminApi:ApiKey"]
        ?? throw new InvalidOperationException("AdminApi:ApiKey is not configured.");
    client.DefaultRequestHeaders.Add("X-Admin-Key", adminKey);
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
