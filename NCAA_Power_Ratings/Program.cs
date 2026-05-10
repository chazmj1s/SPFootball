using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NCAA_Power_Ratings;
using NCAA_Power_Ratings.Configuration;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Interfaces;
using NCAA_Power_Ratings.Services;
using NCAA_Power_Ratings.Utilities;


var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"CONNECTION: {cs}");

// Add database context configuration
// Program.cs - make options lifetime Singleton
builder.Services.AddDbContext<NCAAContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
    sqlOptions => sqlOptions.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorNumbersToAdd: null));
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
}, contextLifetime: ServiceLifetime.Scoped,
   optionsLifetime: ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<NCAAContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
    sqlOptions => sqlOptions.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorNumbersToAdd: null));
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// Register HttpClient for dependency injection
builder.Services.AddHttpClient();

builder.Services.AddScoped<IGameDataService, GameDataService>();
builder.Services.AddTransient<RecordProcessor>();
builder.Services.AddTransient<ScoreDeltaCalculator>();
builder.Services.AddTransient<TeamMetricsService>();
builder.Services.AddTransient<MatchupHistoryCalculator>();
builder.Services.AddTransient<GamePredictionService>();
builder.Services.AddSingleton<ProjectionCacheService>();
builder.Services.AddScoped<WeeklyRankingsService>();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
});

builder.Services.AddLogging(loggingBuilder => {
    loggingBuilder.AddConsole()
        .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Information);
    loggingBuilder.AddDebug();
});

builder.Services.Configure<CustomSettings>(builder.Configuration.GetSection("CustomSettings"));
builder.Services.Configure<MetricsConfiguration>(builder.Configuration.GetSection("MetricsConfiguration"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
        // Leave RoutePrefix at default so UI is at /swagger (matches launchSettings.json launching "swagger/index.html")
        // c.RoutePrefix = string.Empty; // uncomment only if you want the UI at /
    });
}
app.UseExceptionHandler("/Home/Error");
// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
app.UseHsts();

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

