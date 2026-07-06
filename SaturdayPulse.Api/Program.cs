using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using SaturdayPulse;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Data;
using SaturdayPulse.Infrastructure;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Services;
using SaturdayPulse.Utilities;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "SaturdayPulse.db");
builder.Services.AddDbContext<NCAAContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// ── CFBD HTTP Client ──────────────────────────────────────────────────────────
builder.Services.Configure<CfbdApiSettings>(
    builder.Configuration.GetSection("CfbdApi"));

builder.Services.AddHttpClient("cfbd", (sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<CfbdApiSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", settings.BearerToken);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

var testSettings = builder.Configuration.GetSection("CfbdApi").Get<CfbdApiSettings>();
Console.WriteLine($"DEBUG CfbdApi — BaseUrl: '{testSettings?.BaseUrl}' BearerToken empty: {string.IsNullOrEmpty(testSettings?.BearerToken)}");

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ScoreDeltaCalculator>();
builder.Services.AddScoped<MatchupHistoryCalculator>();
builder.Services.AddScoped<TeamMetricsService>();
builder.Services.AddScoped<IGameDataService, GameDataService>();
builder.Services.AddScoped<GamePredictionService>();
builder.Services.AddScoped<WeeklyRankingsService>();
builder.Services.AddScoped<RollingAverageService>();
builder.Services.AddScoped<ProductionGameDataService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<DeveloperService>();
builder.Services.AddScoped<IAvgScoreDifferentialService, AvgScoreDifferentialService>();
builder.Services.AddScoped<ProjectionAccuracyService>();
builder.Services.AddScoped<ConferenceTierService>();
builder.Services.AddScoped<RosterCapacityService>();

builder.Services.AddSingleton<ProjectionCacheService>();


// ── ASP.NET / Swagger ─────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SaturdayPulse API", Version = "v1" });
});

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConsole()
        .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Information);
    loggingBuilder.AddDebug();
});

builder.Services.Configure<CustomSettings>(builder.Configuration.GetSection("CustomSettings"));
builder.Services.Configure<MetricsConfiguration>(builder.Configuration.GetSection("MetricsConfiguration"));

builder.Services.AddCors();

// ── App pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply any pending migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NCAAContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SaturdayPulse API V1"));

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHsts();
app.UseHttpsRedirection();
app.UseRouting();

app.UseCors(policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
