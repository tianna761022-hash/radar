using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockAnalyzer.Models;
using StockAnalyzer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<StockDataService>();
builder.Services.AddSingleton<ThematicDataService>();
builder.Services.AddSingleton<CapitalFlowService>();
builder.Services.AddSingleton<UsMarketService>();
builder.Services.AddSingleton<AnalyzerEngine>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// 0. API: Auth Verification & Login
app.MapPost("/api/auth/login", (LoginRequest req) =>
{
    var currentPassword = Environment.GetEnvironmentVariable("ACCESS_PASSWORD") ?? "888888";
    if (!string.IsNullOrEmpty(req?.Password) && string.Equals(req.Password.Trim(), currentPassword.Trim(), StringComparison.Ordinal))
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(currentPassword.Trim()));
        return Results.Ok(new { success = true, token });
    }
    return Results.Json(new { success = false, message = "密碼錯誤！請輸入正確的私人通關密碼。" }, statusCode: 401);
});

bool IsAuthorized(HttpContext ctx)
{
    var currentPassword = Environment.GetEnvironmentVariable("ACCESS_PASSWORD") ?? "888888";
    var expectedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(currentPassword.Trim()));
    
    var authHeader = ctx.Request.Headers["Authorization"].ToString();
    var accessKey = ctx.Request.Headers["X-Access-Key"].ToString();
    
    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var token = authHeader.Substring(7).Trim();
        if (token == expectedToken || token == currentPassword.Trim()) return true;
    }
    if (!string.IsNullOrEmpty(accessKey) && (accessKey == expectedToken || accessKey == currentPassword.Trim()))
    {
        return true;
    }
    return false;
}

// In-Memory Global Scan Cache for Instant Response
ScanResponse? cachedFullScan = null;
DateTime lastFullScanTime = DateTime.MinValue;
var scanCacheLock = new object();

// 1. API: Full Market Scan (High Performance Cached)
app.MapGet("/api/scan", async (
    HttpContext context,
    string? strategy,
    int? ndays,
    decimal? surge,
    decimal? maxprice,
    long? minlots,
    bool? goldencapital,
    bool? cleanchips,
    string? theme,
    string? sector,
    bool? refresh,
    StockDataService stockService,
    ThematicDataService thematicService,
    CapitalFlowService flowService,
    UsMarketService usMarketService,
    AnalyzerEngine engine) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();

    bool forceRefresh = refresh ?? false;
    ScanResponse? baseResponse = null;

    lock (scanCacheLock)
    {
        if (!forceRefresh && cachedFullScan != null && (DateTime.Now - lastFullScanTime).TotalMinutes < 15)
        {
            baseResponse = cachedFullScan;
        }
    }

    if (baseResponse == null)
    {
        var quotes = await stockService.GetAllMarketQuotesAsync(forceRefresh);
        thematicService.UpdateThemeMomentum(quotes, code => null);

        var capitalFlows = flowService.AnalyzeCapitalFlows(quotes, thematicService);
        var regime = flowService.CalculateMarketRegime(quotes);
        var themes = thematicService.GetAllThemes();
        var usMarket = usMarketService.GetUsMarketSummary();

        var baseFilter = new ScanFilterParams
        {
            StrategyTrack = "all",
            NDays = 3,
            MinSurgeRatio = 1.2m,
            MaxPriceChangeInNDays = 8.0m,
            MinDailyLots = 100,
            RequireGoldenCapital = false,
            RequireCleanChips = false,
            ThemeFilter = "all",
            SectorFilter = "all"
        };

        var allCandidates = engine.RunFullMarketScan(quotes, baseFilter, capitalFlows);
        var scanTimeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var tradeDateStr = stockService.LatestTradeDate;
        regime.TradeDate = tradeDateStr;
        regime.ScanTime = scanTimeStr;

        baseResponse = new ScanResponse
        {
            MarketRegime = regime,
            UsMarket = usMarket,
            CapitalFlows = capitalFlows,
            DynamicThemes = themes,
            Results = allCandidates,
            TotalCandidatesCount = allCandidates.Count,
            ScanTime = scanTimeStr,
            TradeDate = tradeDateStr,
            DataSourceNote = "臺灣證券交易所 (TWSE) ＆ 證券櫃檯買賣中心 (TPEx) 官方即時盤後授權數據"
        };

        lock (scanCacheLock)
        {
            cachedFullScan = baseResponse;
            lastFullScanTime = DateTime.Now;
        }
    }

    // Dynamic Filter on Cached Candidates
    var filteredResults = baseResponse.Results.AsEnumerable();

    if (!string.IsNullOrEmpty(strategy) && strategy != "all")
    {
        filteredResults = filteredResults.Where(r => r.StrategyTracks.Contains(strategy));
    }

    if (surge.HasValue)
    {
        filteredResults = filteredResults.Where(r => r.VolumeSurgeRatio >= surge.Value);
    }

    if (maxprice.HasValue)
    {
        filteredResults = filteredResults.Where(r => r.PriceChangeInNDays <= maxprice.Value);
    }

    if (minlots.HasValue)
    {
        filteredResults = filteredResults.Where(r => r.VolumeLots >= minlots.Value);
    }

    if (goldencapital.HasValue && goldencapital.Value)
    {
        filteredResults = filteredResults.Where(r => r.IsGoldenCapital);
    }

    if (cleanchips.HasValue && cleanchips.Value)
    {
        filteredResults = filteredResults.Where(r => !r.OvernightWhaleRisk.Contains("⚠️"));
    }

    if (!string.IsNullOrEmpty(theme) && theme != "all")
    {
        filteredResults = filteredResults.Where(r => r.MatchedThemes.Contains(theme));
    }

    if (!string.IsNullOrEmpty(sector) && sector != "all")
    {
        filteredResults = filteredResults.Where(r => r.Sector.Contains(sector, StringComparison.OrdinalIgnoreCase));
    }

    var finalResultsList = filteredResults.ToList();

    return Results.Ok(new ScanResponse
    {
        MarketRegime = baseResponse.MarketRegime,
        UsMarket = baseResponse.UsMarket,
        CapitalFlows = baseResponse.CapitalFlows,
        DynamicThemes = baseResponse.DynamicThemes,
        Results = finalResultsList,
        TotalCandidatesCount = finalResultsList.Count,
        ScanTime = baseResponse.ScanTime,
        TradeDate = baseResponse.TradeDate,
        DataSourceNote = baseResponse.DataSourceNote
    });
});

// 1.1 API: US Market Linkage Summary
app.MapGet("/api/us-market", (HttpContext context, UsMarketService usService) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();
    return Results.Ok(usService.GetUsMarketSummary());
});

// 2. API: Stock Details & Historical K-Line OHLCV
app.MapGet("/api/stock/{code}", (
    HttpContext context,
    string code,
    StockDataService stockService,
    ThematicDataService thematicService) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();

    var quotes = stockService.GetAllMarketQuotesAsync(false).Result;
    var quote = quotes.FirstOrDefault(q => q.Code == code);
    var history = stockService.GetStockHistory(code, quote);
    var matchedThemes = thematicService.MatchThemes(code, quote?.Name ?? "", quote?.Sector ?? "");
    var roleDesc = thematicService.GetThematicRoleDescription(code, quote?.Name ?? "", matchedThemes);

    return Results.Ok(new
    {
        Code = code,
        Name = quote?.Name ?? code,
        Market = quote?.Market ?? "上市",
        Sector = quote?.Sector ?? "電子科技",
        CurrentPrice = quote?.Close ?? 100,
        Change = quote?.Change ?? 0,
        ChangePercent = quote?.ChangePercent ?? 0,
        VolumeLots = quote?.VolumeLots ?? 0,
        TurnoverValueInMillion = Math.Round((quote?.TurnoverValue ?? 0) / 1_000_000m, 1),
        MatchedThemes = matchedThemes,
        ThematicRole = roleDesc,
        TradeDate = stockService.LatestTradeDate,
        ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        History = history
    });
});

// 3. API: Dynamic Themes
app.MapGet("/api/themes", (HttpContext context, ThematicDataService thematicService) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();
    return Results.Ok(thematicService.GetAllThemes());
});

// 4. API: Update Themes
app.MapPost("/api/themes/update", (HttpContext context, ThemeDefinition newTheme, ThematicDataService thematicService) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();

    var themes = thematicService.GetAllThemes();
    var existing = themes.FirstOrDefault(t => t.Id == newTheme.Id);
    if (existing != null)
    {
        existing.Name = newTheme.Name;
        existing.Icon = newTheme.Icon;
        existing.Description = newTheme.Description;
        existing.Keywords = newTheme.Keywords;
        existing.StockCodes = newTheme.StockCodes;
    }
    else
    {
        if (string.IsNullOrEmpty(newTheme.Id))
        {
            newTheme.Id = "custom_" + DateTime.Now.Ticks;
        }
        themes.Add(newTheme);
    }

    thematicService.SaveThemes(themes);
    return Results.Ok(new { success = true, themes });
});

// 5. API: Copy-paste Broker Codes Format
app.MapGet("/api/broker-codes", (
    HttpContext context,
    string? strategy,
    StockDataService stockService,
    ThematicDataService thematicService,
    CapitalFlowService flowService,
    AnalyzerEngine engine) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();

    var quotes = stockService.GetAllMarketQuotesAsync(false).Result;
    var flows = flowService.AnalyzeCapitalFlows(quotes, thematicService);
    var filter = new ScanFilterParams { StrategyTrack = strategy ?? "golden" };
    var results = engine.RunFullMarketScan(quotes, filter, flows);

    var codeList = string.Join(",", results.Select(r => r.Code));
    return Results.Ok(new { codes = codeList, count = results.Count });
});

// 6. API: Export to CSV (with UTF-8 BOM for Excel)
app.MapGet("/api/export", (
    HttpContext context,
    string? strategy,
    StockDataService stockService,
    ThematicDataService thematicService,
    CapitalFlowService flowService,
    AnalyzerEngine engine) =>
{
    if (!IsAuthorized(context)) return Results.Unauthorized();

    var quotes = stockService.GetAllMarketQuotesAsync(false).Result;
    var flows = flowService.AnalyzeCapitalFlows(quotes, thematicService);
    var filter = new ScanFilterParams { StrategyTrack = strategy ?? "all" };
    var results = engine.RunFullMarketScan(quotes, filter, flows);

    var sb = new StringBuilder();
    sb.AppendLine("股票代號,股票名稱,市場,收盤價,今日漲跌幅(%),成交張數,放量倍數,所屬熱門題材,大師評分,建議防守停損價,預期目標價,風報比,操盤解讀");

    foreach (var r in results)
    {
        var themes = string.Join(" | ", r.MatchedThemes);
        var cleanNarrative = r.Narrative.Replace(",", "，").Replace("\"", "'");
        sb.AppendLine($"{r.Code},{r.Name},{r.Market},{r.CurrentPrice},{r.ChangePercent:+0.00;-0.00},{r.VolumeLots},{r.VolumeSurgeRatio},{themes},{r.MasterScore},{r.DefensivePrice},{r.TargetPrice},{r.RiskRewardRatio}:1,\"{cleanNarrative}\"");
    }

    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    return Results.File(bytes, "text/csv; charset=utf-8", $"台股紅色波段精選_{DateTime.Now:yyyyMMdd_HHmm}.csv");
});

// 7. API: Health Check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }));

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
Console.WriteLine("==================================================");
Console.WriteLine("🚀 台股全自動純多頭紅色波段股票分析系統已啟動！");
Console.WriteLine($"🌐 伺服器監聽連接埠: {port}");
Console.WriteLine("🔒 私人密碼保護已啟用（預設: 888888，可由 ACCESS_PASSWORD 變更）");
Console.WriteLine("==================================================");

// Background warmup on startup so the very first scan responds in 0ms!
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(500);
        using var scope = app.Services.CreateScope();
        var stockService = scope.ServiceProvider.GetRequiredService<StockDataService>();
        var quotes = await stockService.GetAllMarketQuotesAsync(false);
        Console.WriteLine($"[Startup Warmup] Preloaded {quotes.Count} stock quotes into memory.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup Warmup Notice] {ex.Message}");
    }
});

app.Run($"http://0.0.0.0:{port}");
