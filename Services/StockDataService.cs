using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StockAnalyzer.Models;

namespace StockAnalyzer.Services
{
    public class StockDataService
    {
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, StockRawQuote> _quoteCache = new();
        private readonly ConcurrentDictionary<string, List<StockDailyData>> _historyCache = new();
        private readonly ConcurrentDictionary<string, decimal> _capitalCache = new(); // Code -> Capital (Billion TWD)
        private readonly ConcurrentDictionary<string, (long Foreign, long Trust, long Dealer)> _institutionalCache = new();
        
        private DateTime _lastFetchTime = DateTime.MinValue;
        private string _latestTradeDate = "";
        private readonly object _lock = new();

        public string LatestTradeDate => !string.IsNullOrEmpty(_latestTradeDate) ? _latestTradeDate : DateTime.Today.ToString("yyyy-MM-dd");
        public DateTime LastScanTime => _lastFetchTime != DateTime.MinValue ? _lastFetchTime : DateTime.Now;

        public StockDataService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(12);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            // Ensure Data directory
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            var histDir = Path.Combine(dataDir, "history");
            if (!Directory.Exists(histDir)) Directory.CreateDirectory(histDir);
        }

        public async Task<List<StockRawQuote>> GetAllMarketQuotesAsync(bool forceRefresh = false)
        {
            if (_quoteCache.Count > 500 && !forceRefresh && (DateTime.Now - _lastFetchTime).TotalMinutes < 30)
            {
                return _quoteCache.Values.ToList();
            }

            // 1. Initial startup load from local market_snapshot.json
            if (_quoteCache.Count < 300)
            {
                var snapshotQuotes = LoadFromSnapshotFile();
                if (snapshotQuotes.Count > 0)
                {
                    foreach (var q in snapshotQuotes)
                    {
                        _quoteCache[q.Code] = q;
                    }
                    _lastFetchTime = DateTime.Now;
                    Console.WriteLine($"[Snapshot Load] Loaded {snapshotQuotes.Count} quotes from snapshot file. TradeDate: {_latestTradeDate}");
                }
            }

            // 2. Return cached if valid and not force refresh
            if (_quoteCache.Count > 300 && !forceRefresh)
            {
                return _quoteCache.Values.ToList();
            }

            // 3. Online fetch real market data
            var quotes = await FetchAllOnlineQuotesAsync();
            if (quotes.Count > 300)
            {
                _quoteCache.Clear();
                foreach (var q in quotes)
                {
                    _quoteCache[q.Code] = q;
                }
                _lastFetchTime = DateTime.Now;
                return quotes;
            }

            return _quoteCache.Values.ToList();
        }

        private async Task<List<StockRawQuote>> FetchAllOnlineQuotesAsync()
        {
            var quotes = new List<StockRawQuote>();
            var twseList = new List<StockRawQuote>();
            var tpexList = new List<StockRawQuote>();

            // 1. Fetch Real Paid-in Capital (TWSE OpenData)
            try
            {
                await LoadRealCapitalsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Capital Fetch Notice] {ex.Message}");
            }

            // 2. Fetch Real Institutional Daily Trading (TWSE T86)
            try
            {
                await LoadRealInstitutionalTradingAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Institutional Fetch Notice] {ex.Message}");
            }

            // 3. Fetch TWSE Quotes
            try
            {
                twseList = await FetchTwseQuotesAsync();
                quotes.AddRange(twseList);
                Console.WriteLine($"[TWSE Online Fetch] Successfully fetched {twseList.Count} real quotes.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TWSE Fetch Error] {ex.Message}");
            }

            // 4. Fetch TPEx Quotes
            try
            {
                tpexList = await FetchTpexQuotesAsync();
                quotes.AddRange(tpexList);
                Console.WriteLine($"[TPEx Online Fetch] Successfully fetched {tpexList.Count} real quotes.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TPEx Fetch Error] {ex.Message}");
            }

            if (quotes.Count > 300)
            {
                SaveSnapshotAsync(twseList, tpexList);
            }

            return quotes;
        }

        private async Task LoadRealCapitalsAsync()
        {
            var url = "https://openapi.twse.com.tw/v1/opendata/t187ap03_L";
            var resp = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(resp);
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var code = GetStringProp(elem, "公司代號", "Code", "code");
                var capStr = GetStringProp(elem, "實收資本額", "PaidInCapital", "capitals");
                if (!string.IsNullOrEmpty(code) && decimal.TryParse(capStr, out var capVal) && capVal > 0)
                {
                    _capitalCache[code] = Math.Round(capVal / 100_000_000m, 2); // Convert to 億元
                }
            }
            Console.WriteLine($"[Capitals Loaded] Cached {_capitalCache.Count} company paid-in capitals.");
        }

        private async Task LoadRealInstitutionalTradingAsync()
        {
            var url = "https://www.twse.com.tw/rwd/zh/fund/T86?response=json";
            var resp = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(resp);
            if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in dataElem.EnumerateArray())
                {
                    if (row.GetArrayLength() >= 19)
                    {
                        var code = row[0].GetString()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(code) || code.Length > 6) continue;

                        var foreignNet = ParseLong(row[4].GetString()) / 1000; // 外資買賣超張數
                        var trustNet = ParseLong(row[10].GetString()) / 1000;   // 投信買賣超張數
                        var dealerNet = ParseLong(row[11].GetString()) / 1000;  // 自營商買賣超張數

                        _institutionalCache[code] = (foreignNet, trustNet, dealerNet);
                    }
                }
                Console.WriteLine($"[Institutional Loaded] Cached {_institutionalCache.Count} stock institutional records from TWSE T86.");
            }
        }

        private async Task<List<StockRawQuote>> FetchTwseQuotesAsync()
        {
            var list = new List<StockRawQuote>();
            var url = "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL";
            var resp = await _httpClient.GetStringAsync(url);
            
            using var doc = JsonDocument.Parse(resp);
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var code = elem.GetProperty("Code").GetString() ?? "";
                var name = elem.GetProperty("Name").GetString() ?? "";
                
                if (code.Length > 6 || string.IsNullOrWhiteSpace(name)) continue;

                var dateStr = elem.TryGetProperty("Date", out var dProp) ? dProp.GetString() ?? "" : "";
                var formattedDate = FormatRocDate(dateStr);
                if (!string.IsNullOrEmpty(formattedDate) && string.IsNullOrEmpty(_latestTradeDate))
                {
                    _latestTradeDate = formattedDate;
                }

                var open = ParseDecimal(elem.GetProperty("OpeningPrice").GetString());
                var high = ParseDecimal(elem.GetProperty("HighestPrice").GetString());
                var low = ParseDecimal(elem.GetProperty("LowestPrice").GetString());
                var close = ParseDecimal(elem.GetProperty("ClosingPrice").GetString());
                var change = ParseDecimal(elem.GetProperty("Change").GetString());
                var volume = ParseLong(elem.GetProperty("TradeVolume").GetString());
                var val = ParseDecimal(elem.GetProperty("TradeValue").GetString());
                var trans = ParseInt(elem.GetProperty("Transaction").GetString());

                if (close <= 0) continue;

                var prevClose = close - change;
                var changePercent = prevClose > 0 ? (change / prevClose) * 100 : 0;

                _capitalCache.TryGetValue(code, out var capInBillion);
                _institutionalCache.TryGetValue(code, out var inst);

                list.Add(new StockRawQuote
                {
                    Code = code,
                    Name = name,
                    Open = open > 0 ? open : close,
                    High = high > 0 ? high : close,
                    Low = low > 0 ? low : close,
                    Close = close,
                    PrevClose = prevClose > 0 ? prevClose : close,
                    Change = change,
                    ChangePercent = Math.Round(changePercent, 2),
                    VolumeShares = volume,
                    TurnoverValue = val,
                    Transactions = trans,
                    Market = "上市",
                    Sector = DetermineSector(code, name),
                    Date = formattedDate,
                    CapitalInBillion = capInBillion,
                    ForeignNetBuyLots = inst.Foreign,
                    TrustNetBuyLots = inst.Trust,
                    DealerNetBuyLots = inst.Dealer
                });
            }

            return list;
        }

        private async Task<List<StockRawQuote>> FetchTpexQuotesAsync()
        {
            var list = new List<StockRawQuote>();
            var url = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes";
            var resp = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(resp);
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var code = elem.GetProperty("SecuritiesCompanyCode").GetString() ?? "";
                var name = elem.GetProperty("CompanyName").GetString() ?? "";

                if (code.Length > 6 || string.IsNullOrWhiteSpace(name)) continue;

                var dateStr = elem.TryGetProperty("Date", out var dProp) ? dProp.GetString() ?? "" : "";
                var formattedDate = FormatRocDate(dateStr);
                if (!string.IsNullOrEmpty(formattedDate) && string.IsNullOrEmpty(_latestTradeDate))
                {
                    _latestTradeDate = formattedDate;
                }

                var open = ParseDecimal(elem.GetProperty("Open").GetString());
                var high = ParseDecimal(elem.GetProperty("High").GetString());
                var low = ParseDecimal(elem.GetProperty("Low").GetString());
                var close = ParseDecimal(elem.GetProperty("Close").GetString());
                var change = ParseDecimal(elem.GetProperty("Change").GetString());
                var volume = ParseLong(elem.GetProperty("TradingShares").GetString());
                var val = ParseDecimal(elem.GetProperty("TransactionAmount").GetString());
                var trans = ParseInt(elem.GetProperty("TransactionNumber").GetString());
                var capStr = elem.TryGetProperty("Capitals", out var capProp) ? capProp.GetString() : "0";
                var capVal = ParseDecimal(capStr);
                var capInBillion = capVal > 0 ? Math.Round(capVal / 100_000_000m, 2) : 0m;
                if (capInBillion > 0) _capitalCache[code] = capInBillion;

                if (close <= 0) continue;

                var prevClose = close - change;
                var changePercent = prevClose > 0 ? (change / prevClose) * 100 : 0;

                _institutionalCache.TryGetValue(code, out var inst);

                list.Add(new StockRawQuote
                {
                    Code = code,
                    Name = name,
                    Open = open > 0 ? open : close,
                    High = high > 0 ? high : close,
                    Low = low > 0 ? low : close,
                    Close = close,
                    PrevClose = prevClose > 0 ? prevClose : close,
                    Change = change,
                    ChangePercent = Math.Round(changePercent, 2),
                    VolumeShares = volume,
                    TurnoverValue = val,
                    Transactions = trans,
                    Market = "上櫃",
                    Sector = DetermineSector(code, name),
                    Date = formattedDate,
                    CapitalInBillion = capInBillion,
                    ForeignNetBuyLots = inst.Foreign,
                    TrustNetBuyLots = inst.Trust,
                    DealerNetBuyLots = inst.Dealer
                });
            }

            return list;
        }

        public List<StockDailyData> GetStockHistory(string code, StockRawQuote? currentQuote = null)
        {
            // 1. Check in-memory cache
            if (_historyCache.TryGetValue(code, out var cached) && cached.Count >= 20)
            {
                return cached;
            }

            // 2. Check local file cache
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "history", $"{code}.json");
            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var list = JsonSerializer.Deserialize<List<StockDailyData>>(json);
                    if (list != null && list.Count >= 15)
                    {
                        // Check if latest date is up to date with today's quote
                        if (currentQuote != null && list.Last().Date != LatestTradeDate && currentQuote.Close > 0)
                        {
                            AppendTodayQuote(list, currentQuote);
                        }
                        _historyCache[code] = list;
                        return list;
                    }
                }
                catch { }
            }

            // 3. Fetch Real History from FinMind API
            try
            {
                var realHistory = FetchRealFinMindHistory(code, currentQuote);
                if (realHistory.Count > 0)
                {
                    _historyCache[code] = realHistory;
                    try
                    {
                        var json = JsonSerializer.Serialize(realHistory, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(filePath, json);
                    }
                    catch { }
                    return realHistory;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FinMind History Fetch Error {code}] {ex.Message}");
            }

            // 4. If network unavailable, build single real data point for today
            var fallback = new List<StockDailyData>();
            if (currentQuote != null)
            {
                fallback.Add(new StockDailyData
                {
                    Date = LatestTradeDate,
                    Open = currentQuote.Open,
                    High = currentQuote.High,
                    Low = currentQuote.Low,
                    Close = currentQuote.Close,
                    VolumeLots = currentQuote.VolumeLots,
                    TurnoverValue = currentQuote.TurnoverValue,
                    Change = currentQuote.Change,
                    ChangePercent = currentQuote.ChangePercent,
                    MA5 = currentQuote.Close,
                    MA10 = currentQuote.Close,
                    MA20 = currentQuote.Close,
                    MA60 = currentQuote.Close,
                    VMA5 = currentQuote.VolumeLots,
                    VMA20 = currentQuote.VolumeLots
                });
            }
            return fallback;
        }

        private List<StockDailyData> FetchRealFinMindHistory(string code, StockRawQuote? currentQuote)
        {
            var startDate = DateTime.Today.AddDays(-120).ToString("yyyy-MM-dd");
            var url = $"https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockPrice&data_id={code}&start_date={startDate}";
            
            var resp = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(resp);
            if (!doc.RootElement.TryGetProperty("data", out var dataElem) || dataElem.ValueKind != JsonValueKind.Array)
            {
                return new List<StockDailyData>();
            }

            var rawPoints = new List<(string Date, decimal Open, decimal High, decimal Low, decimal Close, long VolumeLots, decimal Val, decimal Spread)>();

            foreach (var item in dataElem.EnumerateArray())
            {
                var date = item.GetProperty("date").GetString() ?? "";
                var open = item.GetProperty("open").GetDecimal();
                var high = item.GetProperty("max").GetDecimal();
                var low = item.GetProperty("min").GetDecimal();
                var close = item.GetProperty("close").GetDecimal();
                var volShares = item.GetProperty("Trading_Volume").GetInt64();
                var val = item.GetProperty("Trading_money").GetDecimal();
                var spread = item.GetProperty("spread").GetDecimal();

                if (close <= 0) continue;

                rawPoints.Add((date, open, high, low, close, volShares / 1000, val, spread));
            }

            if (rawPoints.Count == 0) return new List<StockDailyData>();

            // If current quote date is newer than FinMind last date, append current quote
            if (currentQuote != null && currentQuote.Close > 0 && (rawPoints.Count == 0 || rawPoints.Last().Date != LatestTradeDate))
            {
                rawPoints.Add((LatestTradeDate, currentQuote.Open, currentQuote.High, currentQuote.Low, currentQuote.Close, currentQuote.VolumeLots, currentQuote.TurnoverValue, currentQuote.Change));
            }

            // Compute Real Moving Averages and Technical Indicators
            var result = new List<StockDailyData>();
            for (int i = 0; i < rawPoints.Count; i++)
            {
                var item = rawPoints[i];
                var ma5 = rawPoints.Skip(Math.Max(0, i - 4)).Take(Math.Min(i + 1, 5)).Average(x => x.Close);
                var ma10 = rawPoints.Skip(Math.Max(0, i - 9)).Take(Math.Min(i + 1, 10)).Average(x => x.Close);
                var ma20 = rawPoints.Skip(Math.Max(0, i - 19)).Take(Math.Min(i + 1, 20)).Average(x => x.Close);
                var ma60 = rawPoints.Skip(Math.Max(0, i - 59)).Take(Math.Min(i + 1, 60)).Average(x => x.Close);
                var vma5 = (decimal)rawPoints.Skip(Math.Max(0, i - 4)).Take(Math.Min(i + 1, 5)).Average(x => x.VolumeLots);
                var vma20 = (decimal)rawPoints.Skip(Math.Max(0, i - 19)).Take(Math.Min(i + 1, 20)).Average(x => x.VolumeLots);

                var prevClose = i > 0 ? rawPoints[i - 1].Close : item.Open;
                var change = item.Close - prevClose;
                var changePct = prevClose > 0 ? (change / prevClose) * 100 : 0;

                var amplitude = item.High - item.Low;
                var clv = amplitude > 0 ? (item.Close - item.Low) / amplitude : 0.5m;
                var upperShadow = amplitude > 0 ? (item.High - Math.Max(item.Open, item.Close)) / amplitude : 0m;

                result.Add(new StockDailyData
                {
                    Date = item.Date,
                    Open = item.Open,
                    High = item.High,
                    Low = item.Low,
                    Close = item.Close,
                    VolumeLots = item.VolumeLots,
                    TurnoverValue = item.Val,
                    Change = Math.Round(change, 2),
                    ChangePercent = Math.Round(changePct, 2),
                    CLV = Math.Round(clv, 4),
                    UpperShadowRatio = Math.Round(upperShadow, 4),
                    MA5 = Math.Round(ma5, 2),
                    MA10 = Math.Round(ma10, 2),
                    MA20 = Math.Round(ma20, 2),
                    MA60 = Math.Round(ma60, 2),
                    VMA5 = Math.Round(vma5, 0),
                    VMA20 = Math.Round(vma20, 0)
                });
            }

            return result;
        }

        private void AppendTodayQuote(List<StockDailyData> list, StockRawQuote currentQuote)
        {
            var amplitude = currentQuote.High - currentQuote.Low;
            var clv = amplitude > 0 ? (currentQuote.Close - currentQuote.Low) / amplitude : 0.5m;
            var upperShadow = amplitude > 0 ? (currentQuote.High - Math.Max(currentQuote.Open, currentQuote.Close)) / amplitude : 0m;

            var closeList = list.Select(x => x.Close).ToList();
            closeList.Add(currentQuote.Close);
            var volList = list.Select(x => (decimal)x.VolumeLots).ToList();
            volList.Add(currentQuote.VolumeLots);

            var ma5 = closeList.TakeLast(5).Average();
            var ma10 = closeList.TakeLast(10).Average();
            var ma20 = closeList.TakeLast(20).Average();
            var ma60 = closeList.TakeLast(60).Average();
            var vma5 = volList.TakeLast(5).Average();
            var vma20 = volList.TakeLast(20).Average();

            list.Add(new StockDailyData
            {
                Date = LatestTradeDate,
                Open = currentQuote.Open,
                High = currentQuote.High,
                Low = currentQuote.Low,
                Close = currentQuote.Close,
                VolumeLots = currentQuote.VolumeLots,
                TurnoverValue = currentQuote.TurnoverValue,
                Change = currentQuote.Change,
                ChangePercent = currentQuote.ChangePercent,
                CLV = Math.Round(clv, 4),
                UpperShadowRatio = Math.Round(upperShadow, 4),
                MA5 = Math.Round(ma5, 2),
                MA10 = Math.Round(ma10, 2),
                MA20 = Math.Round(ma20, 2),
                MA60 = Math.Round(ma60, 2),
                VMA5 = Math.Round(vma5, 0),
                VMA20 = Math.Round(vma20, 0)
            });
        }

        private void SaveSnapshotAsync(List<StockRawQuote> twse, List<StockRawQuote> tpex)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var dir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var filePath = Path.Combine(dir, "market_snapshot.json");
                    var obj = new
                    {
                        tradeDate = LatestTradeDate,
                        updateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        twse = twse,
                        tpex = tpex
                    };
                    var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
                    Console.WriteLine($"[Snapshot Auto-Saved] Cached {twse.Count + tpex.Count} quotes to {filePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Snapshot Save Notice] {ex.Message}");
                }
            });
        }

        private List<StockRawQuote> LoadFromSnapshotFile()
        {
            var list = new List<StockRawQuote>();
            try
            {
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "Data", "market_snapshot.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Data", "market_snapshot.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "market_snapshot.json"),
                    Path.Combine("/app", "Data", "market_snapshot.json"),
                    "Data/market_snapshot.json",
                    "market_snapshot.json"
                };

                var filePath = candidates.FirstOrDefault(File.Exists);
                if (filePath != null)
                {
                    var json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("tradeDate", out var tdProp))
                    {
                        var td = tdProp.GetString();
                        if (!string.IsNullOrEmpty(td)) _latestTradeDate = td;
                    }

                    if (doc.RootElement.TryGetProperty("twse", out var twseElem))
                    {
                        var arr = ExtractArrayElement(twseElem);
                        if (arr.ValueKind == JsonValueKind.Array)
                        {
                            list.AddRange(ParseSnapshotElements(arr, "上市"));
                        }
                    }

                    if (doc.RootElement.TryGetProperty("tpex", out var tpexElem))
                    {
                        var arr = ExtractArrayElement(tpexElem);
                        if (arr.ValueKind == JsonValueKind.Array)
                        {
                            list.AddRange(ParseSnapshotElements(arr, "上櫃"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Snapshot Load Error] {ex.Message}");
            }

            return list;
        }

        private List<StockRawQuote> ParseSnapshotElements(JsonElement elem, string market)
        {
            var list = new List<StockRawQuote>();
            foreach (var item in elem.EnumerateArray())
            {
                var code = GetStringProp(item, "Code", "code", "SecuritiesCompanyCode");
                var name = GetStringProp(item, "Name", "name", "CompanyName");
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) continue;

                var open = GetDecimalProp(item, "Open", "OpeningPrice");
                var high = GetDecimalProp(item, "High", "HighestPrice");
                var low = GetDecimalProp(item, "Low", "LowestPrice");
                var close = GetDecimalProp(item, "Close", "ClosingPrice");
                var change = GetDecimalProp(item, "Change");
                var changePct = GetDecimalProp(item, "ChangePercent");
                var volShares = GetLongProp(item, "VolumeShares", "TradingShares", "TradeVolume");
                var val = GetDecimalProp(item, "TurnoverValue", "TransactionAmount", "TradeValue");
                var trans = (int)GetLongProp(item, "Transactions", "TransactionNumber", "Transaction");
                var cap = GetDecimalProp(item, "CapitalInBillion", "Capitals");
                var foreign = GetLongProp(item, "ForeignNetBuyLots");
                var trust = GetLongProp(item, "TrustNetBuyLots");
                var dealer = GetLongProp(item, "DealerNetBuyLots");
                var date = GetStringProp(item, "Date", "date");

                list.Add(new StockRawQuote
                {
                    Code = code,
                    Name = name,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Change = change,
                    ChangePercent = changePct,
                    VolumeShares = volShares,
                    TurnoverValue = val,
                    Transactions = trans,
                    Market = market,
                    Sector = DetermineSector(code, name),
                    Date = date,
                    CapitalInBillion = cap,
                    ForeignNetBuyLots = foreign,
                    TrustNetBuyLots = trust,
                    DealerNetBuyLots = dealer
                });
            }
            return list;
        }

        private static JsonElement ExtractArrayElement(JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Array) return elem;
            if (elem.ValueKind == JsonValueKind.Object)
            {
                if (elem.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array) return v;
                if (elem.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array) return it;
                if (elem.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array) return d;
            }
            return default;
        }

        private static string GetStringProp(JsonElement elem, params string[] propNames)
        {
            foreach (var p in propNames)
            {
                if (elem.TryGetProperty(p, out var val))
                {
                    var str = val.GetString();
                    if (!string.IsNullOrEmpty(str)) return str.Trim();
                }
            }
            return "";
        }

        private static decimal GetDecimalProp(JsonElement elem, params string[] propNames)
        {
            foreach (var p in propNames)
            {
                if (elem.TryGetProperty(p, out var val))
                {
                    if (val.ValueKind == JsonValueKind.Number) return val.GetDecimal();
                    if (val.ValueKind == JsonValueKind.String)
                    {
                        var d = ParseDecimal(val.GetString());
                        if (d != 0m) return d;
                    }
                }
            }
            return 0m;
        }

        private static long GetLongProp(JsonElement elem, params string[] propNames)
        {
            foreach (var p in propNames)
            {
                if (elem.TryGetProperty(p, out var val))
                {
                    if (val.ValueKind == JsonValueKind.Number) return val.GetInt64();
                    if (val.ValueKind == JsonValueKind.String)
                    {
                        var l = ParseLong(val.GetString());
                        if (l != 0L) return l;
                    }
                }
            }
            return 0L;
        }

        private static decimal ParseDecimal(string? str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0m;
            var clean = str.Replace(",", "").Replace("+", "").Replace("X", "").Replace("- -", "0").Trim();
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
            return 0m;
        }

        private static long ParseLong(string? str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0L;
            var clean = str.Replace(",", "").Replace("+", "").Trim();
            if (long.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
            return 0L;
        }

        private static int ParseInt(string? str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0;
            var clean = str.Replace(",", "").Replace("+", "").Trim();
            if (int.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
            return 0;
        }

        private static string FormatRocDate(string? rocDate)
        {
            if (string.IsNullOrWhiteSpace(rocDate)) return DateTime.Today.ToString("yyyy-MM-dd");
            var clean = rocDate.Replace("/", "").Replace("-", "").Trim();
            if (clean.Length == 10 && rocDate.Contains("-")) return rocDate;
            if (clean.Length == 8 && clean.StartsWith("20"))
            {
                return $"{clean[..4]}-{clean.Substring(4, 2)}-{clean.Substring(6, 2)}";
            }
            if (clean.Length == 7 && int.TryParse(clean[..3], out var rocYear))
            {
                var year = rocYear + 1911;
                var month = clean.Substring(3, 2);
                var day = clean.Substring(5, 2);
                return $"{year}-{month}-{day}";
            }
            if (clean.Length == 6 && int.TryParse(clean[..2], out var rocYear2))
            {
                var year = rocYear2 + 1911;
                var month = clean.Substring(2, 2);
                var day = clean.Substring(4, 2);
                return $"{year}-{month}-{day}";
            }
            return rocDate;
        }

        private string DetermineSector(string code, string name)
        {
            if (code.StartsWith("23") || code.StartsWith("24") || code.StartsWith("30") || code.StartsWith("32") || code.StartsWith("35") || code.StartsWith("36") || code.StartsWith("52") || code.StartsWith("54") || code.StartsWith("62") || code.StartsWith("64") || code.StartsWith("66") || code.StartsWith("80") || code.StartsWith("82"))
            {
                if (name.Contains("光") || name.Contains("晶") || name.Contains("科") || name.Contains("電") || name.Contains("積") || name.Contains("訊") || name.Contains("碩") || name.Contains("通") || name.Contains("技") || name.Contains("達"))
                {
                    return "半導體/電子零組件";
                }
                return "電子科技";
            }
            if (code.StartsWith("15") || code.StartsWith("16") || name.Contains("電") || name.Contains("重") || name.Contains("機")) return "電機機械/綠能重電";
            if (code.StartsWith("26") || name.Contains("航") || name.Contains("運")) return "航運/物流";
            if (code.StartsWith("41") || code.StartsWith("47") || code.StartsWith("65") || code.StartsWith("67") || name.Contains("生") || name.Contains("醫") || name.Contains("藥")) return "生技醫療";
            if (code.StartsWith("28") || name.Contains("金") || name.Contains("銀") || name.Contains("保")) return "金融保險";
            if (code.StartsWith("25") || name.Contains("建") || name.Contains("地") || name.Contains("皇")) return "建材營造/資產";
            if (code.StartsWith("20") || name.Contains("鋼") || name.Contains("鐵")) return "鋼鐵工業";
            if (code.StartsWith("13") || name.Contains("塑") || name.Contains("化")) return "塑膠化學";
            if (code.StartsWith("84") || code.StartsWith("83") || code.StartsWith("99") || name.Contains("環") || name.Contains("龍") || name.Contains("綠")) return "綠色循環/貴金屬精煉";
            
            return "其他產業";
        }
    }
}
