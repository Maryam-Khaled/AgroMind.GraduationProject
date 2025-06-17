
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

			// Add services to the container.

			builder.Services.AddControllers();
			// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen();

			builder.Services.AddHttpContextAccessor();
			builder.Services.AddDbContext<AgroMindContext>(Options =>
			{
				//Configuration >- el property el maska el file el appsetting
				Options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));

			});

			builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
			{
				options.TokenLifespan = TimeSpan.FromHours(2); // Set your desired expiration
			});
			#region IdentityServices

			//builder.Services.AddDbContext<AppIdentityDbContext>(Options =>
			//{
			//	Options.UseSqlServer(builder.Configuration.GetConnectionString("IdentityConnection"));

			//});
			builder.Services.AddIdentityServices(builder.Configuration); //Extension Method have Services of Identity
			#endregion
			//builder.Services.AddScoped<ICartRepository, CartRepository>();
			// Make Redis optional for Azure deployment
			builder.Services.AddSingleton<IConnectionMultiplexer>(Options =>
			{
				var connection = builder.Configuration.GetConnectionString("RedisConnection");
				if (string.IsNullOrWhiteSpace(connection))
				{
					// Return a dummy connection or skip Redis for now
					// In production, you should set up Azure Redis Cache
					Console.WriteLine("WARNING: Redis connection not configured. Cart functionality will be disabled.");
					return null;
				}
				try
				{
					return ConnectionMultiplexer.Connect(connection);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"WARNING: Could not connect to Redis: {ex.Message}. Cart functionality will be disabled.");
					return null;
				}
			});

			//builder.Services.AddScoped<IGenericRepositories<Product, int>, GenericRepository<Product, int>>();

			//This AddScoped For Generic to didn't Add Service for each Repository
			builder.Services.AddScoped(typeof(IGenericRepositories<,>), typeof(GenericRepository<,>));
			//builder.Services.AddAutoMapper(M => M.AddProfile(new MappingProfiles()));
			builder.Services.AddAutoMapper(typeof(MappingProfiles));

			builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

			builder.Services.AddScoped<IServiceManager, ServiceManager>();
			builder.Services.AddScoped<ITokenService, TokenService>(); //  to register TokenService

			builder.Services.AddControllers()
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
		options.JsonSerializerOptions.PropertyNamingPolicy = null;
		options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
		//options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonConverter<DateTime>());
	});

			builder.Services.AddCors(options =>
			{
				options.AddPolicy("AllowVercel",
					builder => builder
					.WithOrigins(
						"http://work-space-agromind-82bke0n9m-maryam-khaled-abobakrs-projects.vercel.app", // Your Vercel domain
						"https://work-space-agromind-82bke0n9m-maryam-khaled-abobakrs-projects.vercel.app", // Your Vercel domain with HTTPS
						"http://localhost:3000", // For local development
						"https://localhost:3000", // For local development with HTTPS
						"http://localhost:5132", // For local backend development
						"https://localhost:7057" // For local backend development with HTTPS
					)
					.AllowAnyMethod()
					.AllowAnyHeader()
					.AllowCredentials()); // Allow credentials for authentication
			});

			//Add all services BEFORE builder.Build()

			var app = builder.Build();



			#region Update DB
			//To Allow CLR To Inject Object From AgroMindDbContext
			using var Scope = app.Services.CreateScope(); //Cretae Scope : is Container has Servises Of LifeTime Type :Scoped
														  //Like :AgroMindDbContext() "Act Db"


			var Services = Scope.ServiceProvider;

			var context = Services.GetRequiredService<AgroMindContext>();
			var loggerFactory = Services.GetRequiredService<ILoggerFactory>();
			//var logger = Services.GetRequiredService<ILogger<Program>>();
			var logger = loggerFactory.CreateLogger<Program>();

			var roleManager = Services.GetRequiredService<RoleManager<IdentityRole>>();
			var userManager = Services.GetRequiredService<UserManager<AppUser>>();

			//var validationKeyFromConfig = app.Configuration["JWT:key"]; // Use app.Configuration to access settings after build
			//if (string.IsNullOrEmpty(validationKeyFromConfig))
			//{
			//	logger.LogError("Program.cs: JWT:key is missing or empty in the configuration for validation!");
			//}
			//else
			//{
			//	logger.LogInformation($"Program.cs: JWT Key from config for validation: '{validationKeyFromConfig}' (Length: {validationKeyFromConfig.Length})");
			//}


			try // if DB kant Mawgoda
			{

				await context.Database.MigrateAsync(); //Update-Database

				await AppIdentityDbContextSeed.SeedRolesAsync(roleManager, logger);
				await AppIdentityDbContextSeed.SeedUserAsync(userManager, roleManager, logger);
				await AgroContextSeed.SeedAsync(context); //Seeding Data
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "There Are Problems during Apply Migrations !");// What Message Act => LogError -> red and Message of error
			}
			#endregion


			//builder.Logging.AddConsole();
			//builder.Logging.SetMinimumLevel(LogLevel.Debug);



			// Configure the HTTP request pipeline.
			if (app.Environment.IsDevelopment())
			{
				app.UseSwagger();
				app.UseSwaggerUI();
			}
			app.UseHttpsRedirection();// Redirects HTTP to HTTPS
			app.UseCors("AllowVercel"); // Place CORS


			app.UseRouting();

			//app.UseStaticFiles();
			app.UseAuthentication();// Processes JWT token
			app.UseAuthorization();// Checks roles based on processed token
			app.MapControllers();// Maps routes





			app.Run();
		}
	}
}
