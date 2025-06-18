using AgroMind.GP.APIs.Extensions;
using AgroMind.GP.APIs.Helpers;
using AgroMind.GP.Core.Contracts.Repositories.Contract;
using AgroMind.GP.Core.Contracts.Services.Contract;
using AgroMind.GP.Core.Contracts.UnitOfWork.Contract;
using AgroMind.GP.Core.Entities.Identity;
using AgroMind.GP.Repository.Data.Contexts;
using AgroMind.GP.Repository.Data.SeedingData;
using AgroMind.GP.Repository.Repositories;
using AgroMind.GP.Service.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AgroMind.GP.APIs
{
	public class Program
	{
		public static async Task Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			builder.Services.AddControllers();
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen();

			builder.Services.AddHttpContextAccessor();

			builder.Services.AddDbContext<AgroMindContext>(options =>
			{
				options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
			});

			builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
			{
				options.TokenLifespan = TimeSpan.FromHours(2);
			});

			builder.Services.AddIdentityServices(builder.Configuration);

			builder.Services.AddSingleton<IConnectionMultiplexer>(options =>
			{
				var connection = builder.Configuration.GetConnectionString("RedisConnection");
				if (string.IsNullOrWhiteSpace(connection))
				{
					Console.WriteLine("WARNING: Redis connection not configured. Cart functionality will be disabled.");
					return null!;
				}
				try
				{
					return ConnectionMultiplexer.Connect(connection);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"WARNING: Could not connect to Redis: {ex.Message}. Cart functionality will be disabled.");
					return null!;
				}
			});

			builder.Services.AddScoped<ICartRepository, CartRepository>();
			builder.Services.AddScoped(typeof(IGenericRepositories<,>), typeof(GenericRepository<,>));
			builder.Services.AddAutoMapper(typeof(MappingProfiles));
			builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
			builder.Services.AddScoped<IServiceManager, ServiceManager>();
			builder.Services.AddScoped<ITokenService, TokenService>();

			builder.Services.AddControllers().AddJsonOptions(options =>
			{
				options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
				options.JsonSerializerOptions.PropertyNamingPolicy = null;
				options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
			});

			// ✅ UPDATED CORS POLICY
			builder.Services.AddCors(options =>
			{
				options.AddPolicy("AllowVercel",
					policy => policy
						.SetIsOriginAllowed(origin =>
						{
							var allowedOrigins = new[]
							{
								"https://work-space-agromind-atevlbs8o-maryam-khaled-abobakrs-projects.vercel.app", // ✅ newly added
								"http://work-space-agromind-82bke0n9m-maryam-khaled-abobakrs-projects.vercel.app",
								"https://work-space-agromind-82bke0n9m-maryam-khaled-abobakrs-projects.vercel.app",
								"http://work-space-agromind.vercel.app",
								"https://work-space-agromind.vercel.app",
								"http://localhost:3000",
								"https://localhost:3000",
								"http://localhost:5132",
								"https://localhost:7057",
								"https://work-space-agromind-pygnysczd-maryam-khaled-abobakrs-projects.vercel.app"
							};

							if (allowedOrigins.Contains(origin))
								return true;

							return System.Text.RegularExpressions.Regex.IsMatch(
								origin,
								@"^https://work-space-agromind-.*-maryam-khaled-abobakrs-projects\.vercel\.app$");
						})
						.AllowAnyMethod()
						.AllowAnyHeader()
						.AllowCredentials());
			});

			var app = builder.Build();

			#region Update DB
			using var scope = app.Services.CreateScope();
			var services = scope.ServiceProvider;

			var context = services.GetRequiredService<AgroMindContext>();
			var loggerFactory = services.GetRequiredService<ILoggerFactory>();
			var logger = loggerFactory.CreateLogger<Program>();

			var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
			var userManager = services.GetRequiredService<UserManager<AppUser>>();

			try
			{
				await context.Database.MigrateAsync();

				await AppIdentityDbContextSeed.SeedRolesAsync(roleManager, logger);
				await AppIdentityDbContextSeed.SeedUserAsync(userManager, roleManager, logger);
				await AgroContextSeed.SeedAsync(context);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "There Are Problems during Apply Migrations!");
			}
			#endregion

			app.UseSwagger();
			app.UseSwaggerUI();

			app.UseCors("AllowVercel"); // ✅ CORS should come BEFORE routing

			app.UseHttpsRedirection();
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();
			app.MapControllers();

			app.Run();
		}
	}
}
