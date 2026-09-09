using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
            _cachedSummary = GenerateUsMarketData();
        }

        public UsMarketSummary GetUsMarketSummary()
        {
            if ((DateTime.Now - _lastFetch).TotalMinutes < 30 && _cachedSummary.Indices.Count > 0)
            {
                return _cachedSummary;
            }

            _cachedSummary = GenerateUsMarketData();
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
            var gold = summary.TechTitans.FirstOrDefault(t => t.Symbol == "GOLD");
            var btc = summary.TechTitans.FirstOrDefault(t => t.Symbol == "BTC");

            // 1. AI, Cooling, CPO, CoWoS -> NVDA
            if (themes.Any(t => t.Contains("AI") || t.Contains("散熱") || t.Contains("矽光子") || t.Contains("CoWoS") || t.Contains("伺服器")) || 
                code is "3017" or "3324" or "3450" or "3081" or "3131" or "3583" or "2382" or "6669")
            {
                var change = nvda?.ChangePercent ?? 3.42m;
                var sentiment = change >= 0 ? "大漲帶動" : "拉回整理";
                return ("🇺🇸 輝達 (NVDA) 供應鏈", change, $"昨夜美股輝達 (NVDA) {sentiment} ({change:+0.00;-0.00}%)，直接激勵台股 AI 伺服器與散熱供應鏈！");
            }

            // 2. Semiconductor / Foundry -> TSMC ADR / SOX
            if (themes.Any(t => t.Contains("半導體") || t.Contains("晶圓") || t.Contains("IC設計")) || code is "2330" or "2454" or "3661" or "5274")
            {
                var change = tsm?.ChangePercent ?? 2.85m;
                return ("🇺🇸 台積電 ADR (TSM)", change, $"美股台積電 ADR (TSM) 溢價大漲 ({change:+0.00;-0.00}%)，費半指數同步強勢，奠定半導體多頭基調！");
            }

            // 3. Crypto / Boards -> BTC / NVDA
            if (themes.Any(t => t.Contains("虛擬") || t.Contains("顯卡") || t.Contains("比特幣")) || code is "6150" or "2465" or "5386" or "3515")
            {
                var change = btc?.ChangePercent ?? 4.80m;
                return ("🪙 比特幣 (BTC)", change, $"國際比特幣價格暴漲 ({change:+0.00;-0.00}%) 創波段新高，帶動顯卡與板卡廠買盤強勁進駐！");
            }

            // 4. Gold / Recycling -> GOLD
            if (themes.Any(t => t.Contains("黃金") || t.Contains("貴金屬")) || code is "9955" or "1785" or "8390")
            {
                var change = gold?.ChangePercent ?? 2.15m;
                return ("👑 國際黃金 (GOLD)", change, $"國際金價強勢創高 ({change:+0.00;-0.00}%)，直接推升貴金屬精煉與回收業者毛利率與存貨價值！");
            }

            // 5. Apple -> AAPL
            if (themes.Any(t => t.Contains("蘋果") || t.Contains("消費電子")) || code is "3008" or "3406" or "4958" or "2317")
            {
                var change = aapl?.ChangePercent ?? 1.65m;
                return ("🇺🇸 蘋果 (AAPL) 供應鏈", change, $"美股蘋果 (AAPL) 走揚 ({change:+0.00;-0.00}%)，Apple Intelligence 旗艦換機題材發酵。");
            }

            // 6. EV -> TSLA
            if (themes.Any(t => t.Contains("電動車") || t.Contains("車載")) || code is "3019" or "3552" or "6279")
            {
                var change = tsla?.ChangePercent ?? 3.20m;
                return ("🇺🇸 特斯拉 (TSLA) 供應鏈", change, $"美股特斯拉 (TSLA) 勁揚 ({change:+0.00;-0.00}%)，帶動車載鏡頭與功率元件拉貨動能。");
            }

            // General market fallback -> SOX / Nasdaq
            var soxChange = summary.SoxChangePercent;
            return ("🇺🇸 費城半導體 (SOX)", soxChange, $"美股費城半導體指數強勢上漲 ({soxChange:+0.00;-0.00}%)，為台股多頭提供穩定外溢支撐。");
        }

        private UsMarketSummary GenerateUsMarketData()
        {
            var indices = new List<UsMarketQuote>
            {
                new() { Symbol = "^SOX", Name = "費城半導體指數", Price = 5280.5m, Change = 125.4m, ChangePercent = 2.43m, Icon = "🔬", LinkedTwSector = "台積電、聯發科、半導體供應鏈", SentimentText = "🔥 強力多頭" },
                new() { Symbol = "^IXIC", Name = "那斯達克 100", Price = 18640.2m, Change = 248.6m, ChangePercent = 1.35m, Icon = "💻", LinkedTwSector = "AI 伺服器、電子硬體、高科技", SentimentText = "🔥 科技領漲" },
                new() { Symbol = "^GSPC", Name = "標普 500 指數", Price = 5620.8m, Change = 42.5m, ChangePercent = 0.76m, Icon = "📈", LinkedTwSector = "大型權值股、整體市場流動性", SentimentText = "⭐ 溫和偏多" },
                new() { Symbol = "^DJI", Name = "道瓊工業指數", Price = 41250.0m, Change = 180.2m, ChangePercent = 0.44m, Icon = "🏛️", LinkedTwSector = "傳統產業、金融與重電綠能", SentimentText = "⭐ 穩健整理" }
            };

            var titans = new List<UsMarketQuote>
            {
                new() { Symbol = "NVDA", Name = "輝達 (NVIDIA)", Price = 128.5m, Change = 4.6m, ChangePercent = 3.71m, Icon = "🤖", LinkedTwSector = "台積電、奇鋐、雙鴻、鴻海、廣達、緯穎", SentimentText = "🔥 AI 王者大暴漲" },
                new() { Symbol = "TSM", Name = "台積電 ADR", Price = 178.2m, Change = 4.8m, ChangePercent = 2.77m, Icon = "🇹🇼", LinkedTwSector = "台積電 (2330)、台股加權指數", SentimentText = "🔥 溢價擴大強勢" },
                new() { Symbol = "AAPL", Name = "蘋果 (Apple)", Price = 224.8m, Change = 3.2m, ChangePercent = 1.44m, Icon = "📱", LinkedTwSector = "大立光、玉晶光、鴻海、臻鼎-KY", SentimentText = "⭐ 換機潮加持" },
                new() { Symbol = "TSLA", Name = "特斯拉 (Tesla)", Price = 238.4m, Change = 7.5m, ChangePercent = 3.25m, Icon = "🚗", LinkedTwSector = "亞光、貿聯-KY、胡連、充電樁", SentimentText = "🔥 自動駕駛與車用" },
                new() { Symbol = "AMD", Name = "超微 (AMD)", Price = 152.0m, Change = 3.8m, ChangePercent = 2.56m, Icon = "⚡", LinkedTwSector = "撼訊、祥碩、健策", SentimentText = "🔥 晶片算力看好" },
                new() { Symbol = "GOLD", Name = "國際現貨黃金", Price = 2528.0m, Change = 42.0m, ChangePercent = 1.69m, Icon = "👑", LinkedTwSector = "佳龍、光洋科、金益鼎、衛司特", SentimentText = "🔥 歷史新高噴出" },
                new() { Symbol = "BTC", Name = "比特幣 (Bitcoin)", Price = 64800.0m, Change = 2850.0m, ChangePercent = 4.60m, Icon = "🪙", LinkedTwSector = "撼訊、麗臺、青雲、華擎、微星", SentimentText = "🔥 狂飆噴出" }
            };

            return new UsMarketSummary
            {
                Indices = indices,
                TechTitans = titans,
                SoxChangePercent = 2.43m,
                TsmAdrChangePercent = 2.77m,
                NvdaChangePercent = 3.71m,
                UsImpactOnTaiwan = "🔥 昨夜美股費半 (+2.4%) 與輝達 (+3.7%) 強勢大漲，台股 AI、散熱、矽光子與半導體供應鏈今日具備極強上攻動能！",
                UsMoodColor = "text-red"
            };
        }
    }
}
