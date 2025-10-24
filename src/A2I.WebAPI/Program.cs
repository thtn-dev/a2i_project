using A2I.Infrastructure.Database;
using A2I.WebAPI.Extensions;
using Hangfire;
using Hangfire.PostgreSql;
using Scalar.AspNetCore;

namespace A2I.WebAPI;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddAuthorization();
        builder.Services.AddControllers();
        builder.Services.AddDatabaseServices(builder.Configuration, builder.Environment);
        builder.Services.AddStripeServices(builder.Configuration);
      
        
        // Add Hangfire services
        builder.Services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage((options) =>
            {
                var databaseSection = builder.Configuration.GetSection(DatabaseOptions.SectionName);
                var dbOptions = databaseSection.Get<DatabaseOptions>();
                var connectionString = ServiceCollectionExtensions.BuildConnectionString(dbOptions!);
               
                options.UseNpgsqlConnection(connectionString);
            }, new PostgreSqlStorageOptions  
            {
                PrepareSchemaIfNecessary = true,
                InvisibilityTimeout = TimeSpan.FromMinutes(30),
                QueuePollInterval = TimeSpan.FromSeconds(15),
                UseNativeDatabaseTransactions = true,
                EnableTransactionScopeEnlistment = true
            });
            config.UseSimpleAssemblyNameTypeSerializer();
            config.UseRecommendedSerializerSettings();
        });

        builder.Services.AddHangfireServer(options =>
        {
            options.WorkerCount = 5; // Adjust based on load
            options.Queues = ["webhooks", "emails", "default"];
        });
        
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            // Authorization = new[] { new HangfireAuthorizationFilter() }
        });
        app.UseHttpsRedirection();

        app.UseAuthorization();
        app.MapControllers();

        var summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        app.MapGet("/weatherforecast", (HttpContext httpContext) =>
            {
                var forecast = Enumerable.Range(1, 5).Select(index =>
                        new WeatherForecast
                        {
                            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                            TemperatureC = Random.Shared.Next(-20, 55),
                            Summary = summaries[Random.Shared.Next(summaries.Length)]
                        })
                    .ToArray();
                return forecast;
            })
            .WithName("GetWeatherForecast");

        app.Run();
    }
}