using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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


var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"CONNECTION: {cs}");

// Add database context configuration
// Program.cs - make options lifetime Singleton
builder.Services.AddDbContext<NCAAContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null));
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
}, contextLifetime: ServiceLifetime.Scoped,
   optionsLifetime: ServiceLifetime.Singleton);

// Register HttpClient for dependency injection
builder.Services.AddHttpClient();

builder.Services.AddScoped<RecordProcessor>();
builder.Services.AddScoped<ScoreDeltaCalculator>();
builder.Services.AddScoped<MatchupHistoryCalculator>();

builder.Services.AddScoped<TeamMetricsService>();
builder.Services.AddScoped<IGameDataService, GameDataService>();
builder.Services.AddScoped<GamePredictionService>();
builder.Services.AddScoped<ProjectionCacheService>();
builder.Services.AddScoped<WeeklyRankingsService>();
builder.Services.AddScoped<RollingAverageService>();
builder.Services.AddScoped<ProductionGameDataService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<DeveloperService>();

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
// Remove the IsDevelopment() check entirely
app.UseSwagger();
app.UseSwaggerUI(c =>
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NCAA Power Ratings API V1"));

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

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

