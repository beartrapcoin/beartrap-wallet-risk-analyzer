using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Application.Services;
using BearTrap.Hackathon.Components;
using BearTrap.Hackathon.Data;
using BearTrap.Hackathon.Infrastructure.Bitquery;
using BearTrap.Hackathon.Infrastructure.Caching;
using BearTrap.Hackathon.Infrastructure.DataSources;
using BearTrap.Hackathon.Infrastructure.FourMeme;
using BearTrap.Hackathon.Infrastructure.Rpc;
using BearTrap.Hackathon.Services.DataSources;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ===== HTTP Client Configuration =====
// Typed HttpClient for Bitquery with gzip/deflate decompression
builder.Services.AddHttpClient<IBitqueryClient, BitqueryClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
});

// Typed HttpClient for Four.Meme REST API
builder.Services.AddHttpClient<IFourMemeClient, FourMemeClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// Typed HttpClient for BNB RPC provider
builder.Services.AddHttpClient<IBnbRpcClient, BnbRpcClient>((sp, c) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var rpcUrl = (configuration["BnbRpc:Url"] ?? string.Empty).Trim();

    if (Uri.TryCreate(rpcUrl, UriKind.Absolute, out var baseUri))
    {
        c.BaseAddress = baseUri;
    }

    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

// ===== Rate Limiting =====
// Configure rate limiter with token bucket policy (60 requests per minute per IP)
// This prevents excessive load during hackathon with 200+ testers
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 60,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 60,
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            message = "Too many requests. Please try again later.",
            retryAfter = 60
        });
    };
});

// ===== Caching =====
// Memory cache with request coalescing to prevent cache stampede
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IChainDataCache, MemoryChainDataCache>();

// Add Razor components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ===== Database Configuration =====
// EF Core SQLite - stores token snapshots and analysis history
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "beartrap_hackathon.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}", 
        sqliteOptions => sqliteOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

// ===== Application Services =====
// Risk analyzer orchestrates token analysis and scoring
builder.Services.AddScoped<RiskAnalyzer>();

// ===== Chain Data Provider Selection =====
// Reads ChainDataProvider config (default: Rpc for production)
// Supports: Mock (offchain), Rpc, or Bitquery
var provider = builder.Configuration["ChainDataProvider"]?.Trim();

if (string.IsNullOrWhiteSpace(provider))
{
    // Default to Rpc in production, Mock in development
    provider = builder.Environment.IsDevelopment() ? "Mock" : "Rpc";
}

if (provider.Equals("Mock", StringComparison.OrdinalIgnoreCase) ||
    provider.Equals("Offchain", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IChainDataSource, OffchainChainDataSource>();
    // Use mock data source in Offchain mode to avoid HTTP calls
    builder.Services.AddScoped<IFourMemeSource, MockFourMemeSource>();
}
else if (provider.Equals("Rpc", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IChainDataSource, RpcChainDataSource>();
    // Use Four.Meme API first, then enrich with RPC data only for specific addresses
    builder.Services.AddScoped<IFourMemeSource, BitqueryFourMemeSource>();
}
else
{
    builder.Services.AddScoped<IChainDataSource, BitqueryChainDataSource>();
    // Use Four.Meme API first, then enrich with Bitquery data only for specific addresses
    builder.Services.AddScoped<IFourMemeSource, BitqueryFourMemeSource>();
}

// ===== Data Source Adapters =====
// Provides token list adapters for compatibility with UI layer
builder.Services.AddScoped<IFourMemeMainListSource, FourMemeSource>();

var app = builder.Build();


// ===== Middleware Pipeline =====
// Enable rate limiting middleware - rejects requests exceeding 60/minute per IP
app.UseRateLimiter();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ===== Database Migration =====
// Apply pending migrations on startup (creates database if it doesn't exist)
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("DatabaseMigration");
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    try
    {
        logger.LogInformation("Starting database migration check...");
        logger.LogInformation("Database connection string: {ConnectionString}", db.Database.GetConnectionString());
        logger.LogInformation("Database file path: {DbPath}", dbPath);
        logger.LogInformation("Database file exists: {Exists}", File.Exists(dbPath));
        
        var pendingMigrations = db.Database.GetPendingMigrations().ToList();
        var appliedMigrations = db.Database.GetAppliedMigrations().ToList();
        
        logger.LogInformation("Applied migrations: {Count}", appliedMigrations.Count);
        foreach (var migration in appliedMigrations)
        {
            logger.LogInformation("  - {Migration}", migration);
        }
        
        logger.LogInformation("Pending migrations: {Count}", pendingMigrations.Count);
        foreach (var migration in pendingMigrations)
        {
            logger.LogInformation("  - {Migration}", migration);
        }
        
        if (pendingMigrations.Any())
        {
            logger.LogInformation("Applying {Count} pending migrations...", pendingMigrations.Count);
            db.Database.Migrate();
            logger.LogInformation("Migrations applied successfully.");
        }
        else
        {
            logger.LogInformation("Database is up to date. No migrations to apply.");
        }
        
        // Verify tables exist
        var canConnect = db.Database.CanConnect();
        logger.LogInformation("Database connection test: {CanConnect}", canConnect);
        
        if (canConnect)
        {
            var tokenSnapshotCount = db.TokenSnapshots.Count();
            var riskReportCount = db.RiskReports.Count();
            logger.LogInformation("TokenSnapshots table exists with {Count} records", tokenSnapshotCount);
            logger.LogInformation("RiskReports table exists with {Count} records", riskReportCount);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "CRITICAL: Database migration failed! Error: {Message}", ex.Message);
        logger.LogError("Database path: {DbPath}", dbPath);
        logger.LogError("Working directory: {WorkingDir}", Directory.GetCurrentDirectory());
        logger.LogError("ContentRootPath: {ContentRoot}", builder.Environment.ContentRootPath);
        throw; // Re-throw to prevent app from starting with broken database
    }
}

app.Run();
