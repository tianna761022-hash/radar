using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StockAnalyzer.Models;

namespace StockAnalyzer.Services
{
    public class UsMarketService
    {
        private readonly HttpClient _httpClient;
        private UsMarketSummary _cachedSummary = new();
        private DateTime _lastFetch = DateTime.MinValue;

        public UsMarketService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            _cachedSummary = FetchRealUsMarketData();
        }

        public UsMarketSummary GetUsMarketSummary()
        {
            if ((DateTime.Now - _lastFetch).TotalMinutes < 30 && _cachedSummary.TechTitans.Count > 0)
            {
                return _cachedSummary;
            }

            _cachedSummary = FetchRealUsMarketData();
            _lastFetch = DateTime.Now;
            return _cachedSummary;
        }

        public (string TitanName, decimal TitanChange, string ImpactDescription) GetUsLinkageForStock(
            string code, 
            string name, 
            List<string> themes)
        {
            var summary = GetUsMarketSummary();
            var nvda = summary.TechTitans.FirstOrDefault(t => t.Symbol == "NVDA");
            var tsm = summary.TechTitans.FirstOrDefault(t => t.Symbol == "TSM");
            var aapl = summary.TechTitans.FirstOrDefault(t => t.Symbol == "AAPL");
            var tsla = summary.TechTitans.FirstOrDefault(t => t.Symbol == "TSLA");
            var amd = summary.TechTitans.FirstOrDefault(t => t.Symbol == "AMD");

            // 1. AI, Cooling, CPO, CoWoS -> NVDA
            if (themes.Any(t => t.Contains("AI") || t.Contains("散熱") || t.Contains("矽光子") || t.Contains("CoWoS") || t.Contains("伺服器")) || 
                code is "3017" or "3324" or "3450" or "3081" or "3131" or "3583" or "2382" or "6669")
            {
                var change = nvda?.ChangePercent ?? 0m;
                var sentiment = change >= 0 ? "大漲帶動" : "拉回整理";
                return ("🇺🇸 輝達 (NVDA) 供應鏈", change, $"美股輝達 (NVDA) 最新報價 ${nvda?.Price:0.00} ({change:+0.00;-0.00}%)，{sentiment}台股 AI 伺服器與散熱供應鏈！");
            }

            // 2. Semiconductor / Foundry -> TSMC ADR / SOX
            if (themes.Any(t => t.Contains("半導體") || t.Contains("晶圓") || t.Contains("IC設計")) || code is "2330" or "2454" or "3661" or "5274")
            {
                var change = tsm?.ChangePercent ?? 0m;
                var sentiment = change >= 0 ? "溢價大漲" : "震盪整理";
                return ("🇺🇸 台積電 ADR (TSM)", change, $"美股台積電 ADR (TSM) 最新報價 ${tsm?.Price:0.00} ({change:+0.00;-0.00}%)，{sentiment}，奠定台股權值多頭基調！");
            }

            // 3. Apple -> AAPL
            if (themes.Any(t => t.Contains("蘋果") || t.Contains("消費電子")) || code is "3008" or "3406" or "4958" or "2317")
            {
                var change = aapl?.ChangePercent ?? 0m;
                var sentiment = change >= 0 ? "走揚" : "整理";
                return ("🇺🇸 蘋果 (AAPL) 供應鏈", change, $"美股蘋果 (AAPL) 最新報價 ${aapl?.Price:0.00} ({change:+0.00;-0.00}%)，{sentiment}，Apple 旗艦供應鏈連動。");
            }

            // 4. EV -> TSLA
            if (themes.Any(t => t.Contains("電動車") || t.Contains("車載")) || code is "3019" or "3552" or "6279")
            {
                var change = tsla?.ChangePercent ?? 0m;
                var sentiment = change >= 0 ? "勁揚" : "回檔";
                return ("🇺🇸 特斯拉 (TSLA) 供應鏈", change, $"美股特斯拉 (TSLA) 最新報價 ${tsla?.Price:0.00} ({change:+0.00;-0.00}%)，{sentiment}，帶動車載與功率元件拉貨動能。");
            }

            // 5. AMD / High Performance -> AMD
            if (themes.Any(t => t.Contains("顯卡") || t.Contains("板卡") || t.Contains("晶片")) || code is "6150" or "2465" or "5386" or "3515")
            {
                var change = amd?.ChangePercent ?? 0m;
                var sentiment = change >= 0 ? "強勁飆漲" : "整理";
                return ("🇺🇸 超微 (AMD) 供應鏈", change, $"美股超微 (AMD) 最新報價 ${amd?.Price:0.00} ({change:+0.00;-0.00}%)，{sentiment}，帶動台股板卡與運算族群。");
            }

            // General market fallback
            var generalChange = tsm?.ChangePercent ?? nvda?.ChangePercent ?? 0m;
            return ("🇺🇸 美股科技巨頭", generalChange, $"美股巨頭指標表現 ({generalChange:+0.00;-0.00}%)，提供台股產業鏈外溢支撐。");
        }

        private UsMarketSummary FetchRealUsMarketData()
        {
            var titans = new List<UsMarketQuote>();
            var targetSymbols = new (string Symbol, string Name, string Icon, string LinkedSector)[]
            {
                ("NVDA", "輝達 (NVIDIA)", "🤖", "台積電、奇鋐、雙鴻、鴻海、廣達、緯穎"),
                ("TSM", "台積電 ADR", "🇹🇼", "台積電 (2330)、台股加權指數"),
                ("AAPL", "蘋果 (Apple)", "📱", "大立光、玉晶光、鴻海、臻鼎-KY"),
                ("TSLA", "特斯拉 (Tesla)", "🚗", "亞光、貿聯-KY、胡連、充電樁"),
                ("AMD", "超微 (AMD)", "⚡", "撼訊、祥碩、健策")
            };

            var startDate = DateTime.Today.AddDays(-15).ToString("yyyy-MM-dd");

            foreach (var item in targetSymbols)
            {
                try
                {
                    var url = $"https://api.finmindtrade.com/api/v4/data?dataset=USStockPrice&data_id={item.Symbol}&start_date={startDate}";
                    var resp = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Array)
                    {
                        var list = dataElem.EnumerateArray().ToList();
                        if (list.Count >= 2)
                        {
                            var latest = list[^1];
                            var prev = list[^2];

                            var close = latest.GetProperty("Close").GetDecimal();
                            var prevClose = prev.GetProperty("Close").GetDecimal();
                            var change = Math.Round(close - prevClose, 2);
                            var changePct = prevClose > 0 ? Math.Round((change / prevClose) * 100, 2) : 0m;

                            var sentiment = changePct >= 3.0m ? "🔥 狂飆大漲" : (changePct > 0 ? "⭐ 偏多走揚" : (changePct < -3.0m ? "⚠️ 重挫拉回" : "📊 震盪整理"));

                            titans.Add(new UsMarketQuote
                            {
                                Symbol = item.Symbol,
                                Name = item.Name,
                                Price = Math.Round(close, 2),
                                Change = change,
                                ChangePercent = changePct,
                                Icon = item.Icon,
                                LinkedTwSector = item.LinkedSector,
                                SentimentText = sentiment
                            });
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[US Market Fetch Error {item.Symbol}] {ex.Message}");
                }
            }

            var nvdaChange = titans.FirstOrDefault(t => t.Symbol == "NVDA")?.ChangePercent ?? 0m;
            var tsmChange = titans.FirstOrDefault(t => t.Symbol == "TSM")?.ChangePercent ?? 0m;

            var impactDesc = nvdaChange > 0 || tsmChange > 0
                ? $"🔥 美股輝達 ({nvdaChange:+0.00;-0.00}%) 與台積電 ADR ({tsmChange:+0.00;-0.00}%) 表現強勁，直接激勵台股 AI 與半導體供應鏈多頭動能！"
                : $"📊 美股輝達 ({nvdaChange:+0.00;-0.00}%) 與台積電 ADR ({tsmChange:+0.00;-0.00}%) 處於震盪整理，台股個股走獨立基本面行情。";

            return new UsMarketSummary
            {
                Indices = new List<UsMarketQuote>(),
                TechTitans = titans,
                SoxChangePercent = tsmChange,
                TsmAdrChangePercent = tsmChange,
                NvdaChangePercent = nvdaChange,
                UsImpactOnTaiwan = impactDesc,
                UsMoodColor = (nvdaChange >= 0 || tsmChange >= 0) ? "text-red" : "text-emerald"
            };
        }
    }
}
