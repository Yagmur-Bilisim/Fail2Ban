using Fail2Ban.API.Configuration;
using Fail2Ban.API.Data;
using Fail2Ban.API.Interfaces;
using Fail2Ban.API.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Cihazlar arası erişim veya Electron GUI erişimi için CORS.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

// Ayarlar
builder.Services.Configure<AppSettings>(builder.Configuration);

// Veritabanı (SQLite)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=fail2ban_v2.db";
builder.Services.AddDbContext<AppDbContext>(options => {
    options.UseSqlite(connectionString);
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted));
});

// Redis Kurulumu
var redisConnection = builder.Configuration.GetConnectionString("RedisConnection") ?? "localhost:6379";
try
{
    var muxer = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(muxer);
    builder.Services.AddSingleton<IRedisService, RedisService>();
}
catch (Exception ex)
{
    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogWarning(ex, "Redis sunucusuna bağlanılamadı. Redis kurana kadar ilgili servis devre dışı.");
    // Dummy redis servisi veya hata yönetimi eklenebilir. Prototip olarak çalışmasını engellemeyecek bir yöntem kullanılabilir.
}

// HttpClient & AbuseIPDB
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAbuseIPDBService, AbuseIPDBService>();
builder.Services.AddScoped<IOTXService, OTXService>();

// Servisler
builder.Services.AddScoped<IDatabaseService, DatabaseService>();
builder.Services.AddScoped<IFirewallManager, FirewallManager>();

// Background Worker'lar (EventLogWatcher, IIS Pointers, SMTP Pointers, BanCleanup)
if (OperatingSystem.IsWindows())
{
    builder.Services.AddHostedService<EventLogMonitorService>();
    builder.Services.AddHostedService<IisLogMonitorService>();
    builder.Services.AddHostedService<SmtpLogMonitorService>();
    builder.Services.AddHostedService<BanCleanupService>();
}

var memoryLogProvider = new MemoryLoggerProvider();
builder.Logging.AddProvider(memoryLogProvider);

var app = builder.Build();

// Veritabanını Başlat ve İlk Whitelist kayıtlarını at (Migration)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
    await db.InitializeDatabaseAsync();
    
    var fw = scope.ServiceProvider.GetRequiredService<IFirewallManager>();
    await fw.InitializeAsync();
}

app.UseCors("AllowAll");

// Minimal API Endpoints for Electron UI
app.MapGet("/api/bans", async (IDatabaseService db) => 
{
    var bans = await db.GetActiveBansAsync();
    return Results.Ok(bans);
});

app.MapPost("/api/bans", async (IDatabaseService db, IFirewallManager fw, string ip, string reason, int duration) => 
{
    var result = await db.BanIpAsync(ip, reason, 1, duration);
    var bans = await db.GetActiveBansAsync();
    await fw.SyncFirewallRulesAsync(bans.Select(b => b.IpAddress).ToList());
    return Results.Ok(result);
});

app.MapDelete("/api/bans/{ip}", async (IDatabaseService db, IFirewallManager fw, string ip) => 
{
    await db.UnbanIpAsync(ip);
    var bans = await db.GetActiveBansAsync();
    await fw.SyncFirewallRulesAsync(bans.Select(b => b.IpAddress).ToList());
    return Results.Ok();
});

// Logs Endpoint
app.MapGet("/api/logs", () => 
{
    return Results.Ok(memoryLogProvider.GetLogs());
});

// Whitelist Endpoints
app.MapGet("/api/whitelist", async (IDatabaseService db) => 
{
    return Results.Ok(await db.GetWhitelistedIpsAsync());
});

app.MapPost("/api/whitelist", async (IDatabaseService db, IFirewallManager fw, string ip, string desc) => 
{
    var res = await db.AddWhitelistIpAsync(ip, desc);
    if(res != null)
    {
        // Eğer beyaz listeye eklenen kişinin aktif banı varsa DatabaseService'de düşürüldü,
        // Biz de burada kalan aktif banları Firewall'a tekrar senkronize ederek yasaklılar listesinden bu IP'yi çıkarıyoruz.
        var bans = await db.GetActiveBansAsync();
        await fw.SyncFirewallRulesAsync(bans.Select(b => b.IpAddress).ToList());
        return Results.Ok(res);
    }
    return Results.BadRequest("IP already exist.");
});

app.MapDelete("/api/whitelist/{ip}", async (IDatabaseService db, IFirewallManager fw, string ip) => 
{
    await db.RemoveWhitelistIpAsync(ip);
    
    // Beyaz Listeden çıktığı için belki önceden atılmış ve süresi geçmemiş bir aktif banı olabilir. (Opsiyonel Güvenlik)
    var bans = await db.GetActiveBansAsync();
    await fw.SyncFirewallRulesAsync(bans.Select(b => b.IpAddress).ToList());
    
    return Results.Ok();
});


app.MapGet("/api/stats", async (AppDbContext ctx) => 
{
    var totalBans = await ctx.BanRecords.CountAsync();
    var activeBans = await ctx.BanRecords.CountAsync(x => x.IsActive);
    var reportedBans = await ctx.BanRecords.CountAsync(x => x.IsAbuseReported);
    var todayBans = await ctx.BanRecords.CountAsync(x => x.BannedAt.Date == DateTime.Today);
    
    return Results.Ok(new {
        TotalBans = totalBans,
        ActiveBans = activeBans,
        ReportedBans = reportedBans,
        TodayBans = todayBans
    });
});

app.Run("http://0.0.0.0:5009");
