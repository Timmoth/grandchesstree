using GrandChessTree.Api.ApiKeys;
using GrandChessTree.Api.Database;
using GrandChessTree.Api.Middleware;
using GrandChessTree.Api.Perft.V3;
using GrandChessTree.Api.timescale;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.OpenApi.Models;

namespace GrandChessTree.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            var services = builder.Services;
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddScoped<ApiKeyAuthenticator>();
            services.AddScoped<PerftReadings>();
            services.AddScoped<PerftFullTaskService>();
            services.AddScoped<PerftFastTaskService>();
            services.AddHostedService<PerftFullTaskBackgroundService>();
            services.AddHostedService<PerftFastTaskBackgroundService>();

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddMemoryCache();
            services.AddControllers(o =>
            {
            }).AddJsonOptions(c => { });
            services.AddResponseCaching();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                // Define a security scheme for the API key
                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header, // API key will be in the header
                    Name = "X-API-Key", // The name of the header where the API key is expected
                    Description = "API Key needed to access the endpoints"
                });

                // Enforce that the API key is required for all endpoints
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "ApiKey" // This should match the name of the defined security scheme
                    }
                },
                new string[] {}
            }
        });
            }); services.AddLogging(b =>
            {
                b.SetMinimumLevel(LogLevel.Information);
                b.AddSimpleConsole(c =>
                {
                    c.IncludeScopes = false;
                    c.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    c.ColorBehavior = LoggerColorBehavior.Enabled;
                });
            });

            services.AddOutputCache(options =>
            {
  
            });
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(b =>
                    b.WithOrigins("*")
                        .AllowAnyMethod()
                        .AllowAnyHeader());
            });

            var app = builder.Build();
            app.UseMiddleware<RequestTimingMiddleware>();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            app.UseCors();
            app.UseResponseCaching();
            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
