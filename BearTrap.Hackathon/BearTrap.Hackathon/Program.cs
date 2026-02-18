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
    options.UseSqlite($"Data Source={dbPath}"));

// ===== Application Services =====
// Risk analyzer orchestrates token analysis and scoring
builder.Services.AddScoped<RiskAnalyzer>();

// ===== Chain Data Provider Selection =====
// Reads ChainDataProvider config (default: Offchain)
// Supports: Offchain (mock), Rpc, or Bitquery
var provider = (builder.Configuration["ChainDataProvider"] ?? "Offchain").Trim();

if (string.IsNullOrWhiteSpace(provider))
{
    provider = "Offchain";
}

if (provider.Equals("Offchain", StringComparison.OrdinalIgnoreCase))
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

app.Run();
