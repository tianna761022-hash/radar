using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StockAnalyzer.Models;

namespace StockAnalyzer.Services
{
    public class ThematicDataService
    {
        private readonly string _themesFilePath;
        private List<ThemeDefinition> _themes = new();
        private readonly object _fileLock = new();

        public ThematicDataService()
        {
            var baseDir = AppContext.BaseDirectory;
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _themesFilePath = Path.Combine(dataDir, "themes_data.json");
            LoadThemes();
        }

        public void LoadThemes()
        {
            lock (_fileLock)
            {
                if (File.Exists(_themesFilePath))
                {
                    var json = File.ReadAllText(_themesFilePath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _themes = JsonSerializer.Deserialize<List<ThemeDefinition>>(json, opts) ?? new();
                }
                else
                {
                    _themes = new();
                }
            }
        }

        public List<ThemeDefinition> GetAllThemes()
        {
            return _themes.ToList();
        }

        public void SaveThemes(List<ThemeDefinition> updatedThemes)
        {
            lock (_fileLock)
            {
                _themes = updatedThemes;
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_themes, opts);
                File.WriteAllText(_themesFilePath, json);
            }
        }

        public List<string> MatchThemes(string code, string name, string sector)
        {
            var matched = new List<string>();
            foreach (var t in _themes)
            {
                if (t.StockCodes.Contains(code))
                {
                    matched.Add(t.Name);
                    continue;
                }

                // Match keywords
                if (t.Keywords.Any(k => name.Contains(k) || sector.Contains(k)))
                {
                    matched.Add(t.Name);
                }
            }

            if (matched.Count == 0)
            {
                matched.Add(sector);
            }

            return matched.Distinct().ToList();
        }

        public string GetThematicRoleDescription(string code, string name, List<string> matchedThemes)
        {
            // Curated industry supply chain roles
            var dictionary = new Dictionary<string, string>
            {
                { "3017", "全球伺服器散熱龍頭，全面切入 NVIDIA GB200 水冷板 (Cold Plate) 與 CDU 液冷機櫃，訂單能見度直達明年。" },
                { "3324", "高階散熱模組大廠，水冷板與散熱導管主力供應商，受惠 AI 伺服器散熱全面升級需求。" },
                { "3653", "高階均熱片與半導體導線架龍頭，獨家供應頂級 AI 晶片散熱蓋與水冷機構件。" },
                { "3450", "矽光子 CPO 與光通訊封測大廠，切入 800G 光收發模組雷射晶片封裝，主力法人持續佈局。" },
                { "3081", "全球磷化銦 (InP) 磊晶片龍頭，矽光子與光收發核心材料供應商，營運步入強勁復甦。" },
                { "6442", "高階光纖主動與被動元件大廠，北美四大雲端 CSP 廠光通訊傳輸主要供應鏈。" },
                { "3131", "台積電 CoWoS 濕製程單晶圓清洗機台獨家龍頭，先進封裝大擴產最直接受惠者。" },
                { "3583", "半導體再生晶圓與 CoWoS 濕製程設備大廠，受惠台積電海內外晶圓廠大擴充訂單。" },
                { "6187", "CoWoS 先進點膠機與貼合設備龍頭，訂單滿載交期排至下半年。" },
                { "6150", "超微 (AMD) 顯卡主力板卡廠，受惠虛擬幣價格狂飆與電競/挖礦算力需求大增。" },
                { "2465", "輝達 (NVIDIA) 亞太區長期核心合作夥伴，AI 工作站、顯卡與伺服器解決方案直接受益。" },
                { "5386", "高階板卡與記憶體模組廠，虛擬幣行情暴漲帶動高階顯卡急單湧入。" },
                { "9955", "台股最純黃金概念股！專精電子廢棄物提煉黃金白銀，國際金價創天價帶動存貨與毛利暴衝。" },
                { "1785", "半導體與面板貴金屬靶材龍頭，貴金屬精煉回收量全球領先，受惠金銀價格全面上揚。" },
                { "8390", "綜合電子廢料與工業貴金屬回收處理龍頭，毛利率隨貴金屬價格飆漲而大幅提升。" },
                { "6894", "PCB 與半導體高階含銅/貴金屬廢液電解回收設備廠，綠色循環經濟大廠。" },
                { "8033", "台灣無人機國家隊核心廠商，軍工無人機與國防防務標案主力得標者。" },
                { "3491", "SpaceX Starlink 低軌衛星高頻微波與天線元件主要供應鏈，低軌衛星發射加速直接受惠。" },
                { "1519", "台電強韌電網計畫 500kV 超高壓變壓器最大受惠廠，外銷美國電力變壓器訂單大暴增。" },
                { "1513", "GIS 氣體絕緣開關與電網配電盤龍頭，台電重電標案與綠能儲能工程主力統包商。" },
                { "2317", "全球電子代工與 AI 伺服器組裝龍頭，NVIDIA GB200 NVL72 整機櫃主力代工夥伴。" },
                { "2382", "全球雲端 AI 伺服器代工龍頭，北美各大 CSP 巨頭 AI 運算中心核心伺服器供應商。" },
                { "6669", "純度最高白牌 AI 伺服器大廠，微軟與 Meta 核心 AI 伺服器機櫃主力出貨商。" },
                { "2330", "全球晶圓代工與先進製程絕對龍頭，AI 晶片、N3/N2 製程與 CoWoS 產能全球無可取代。" },
                { "2454", "全球手機晶片與 ASIC 邊緣 AI 運算晶片龍頭，攜手輝達共同開發車用與 AI 運算平台。" },
                { "3661", "客製化 ASIC 晶片設計龍頭，專攻北美 CSP 巨頭雲端 AI 加速晶片與 3nm 先進設計。" },
                { "5274", "全球伺服器遠端管理晶片 (BMC) 霸主，全球市佔率超過 70%，AI 伺服器出貨直接推升業績。" }
            };

            if (dictionary.TryGetValue(code, out var desc))
            {
                return desc;
            }

            var themesStr = string.Join("、", matchedThemes);
            return $"深耕於【{themesStr}】產業鏈，具備優質產品技術與主力資金關注潛力。";
        }

        public void UpdateThemeMomentum(List<StockRawQuote> quotes, Func<string, StockDailyData?> getLatestDaily)
        {
            var quoteMap = quotes.ToDictionary(q => q.Code, q => q);

            foreach (var theme in _themes)
            {
                var matchedQuotes = theme.StockCodes
                    .Where(c => quoteMap.ContainsKey(c))
                    .Select(c => quoteMap[c])
                    .ToList();

                if (matchedQuotes.Count == 0)
                {
                    theme.HeatScore = 30;
                    theme.DailyAvgChangePercent = 0;
                    theme.DailyAvgSurgeRatio = 1.0m;
                    continue;
                }

                var avgChange = matchedQuotes.Average(q => q.ChangePercent);
                var totalTurnoverInMillion = matchedQuotes.Sum(q => q.TurnoverValue) / 1_000_000m;
                var advanceRatio = (decimal)matchedQuotes.Count(q => q.ChangePercent > 0) / matchedQuotes.Count;

                // Heat score: turnover weight + avg change + advance ratio
                var heat = (avgChange * 8m) + (advanceRatio * 40m) + Math.Min(30m, totalTurnoverInMillion / 100m);
                theme.HeatScore = Math.Round(Math.Clamp(heat, 10m, 99m), 1);
                theme.DailyAvgChangePercent = Math.Round(avgChange, 2);
                theme.DailyAvgSurgeRatio = Math.Round(1.0m + (avgChange > 0 ? avgChange / 3m : 0), 2);
            }

            // Sort themes by HeatScore descending
            _themes = _themes.OrderByDescending(t => t.HeatScore).ToList();
        }
    }
}
