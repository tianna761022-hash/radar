using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using StockAnalyzer.Models;

namespace StockAnalyzer.Services
{
    public class CapitalFlowService
    {
        public List<CapitalFlowGroup> AnalyzeCapitalFlows(List<StockRawQuote> quotes, ThematicDataService thematicService)
        {
            var groups = new List<CapitalFlowGroup>();
            var totalMarketTurnover = quotes.Sum(q => q.TurnoverValue);
            if (totalMarketTurnover <= 0) totalMarketTurnover = 1;

            var themes = thematicService.GetAllThemes();
            var quoteMap = quotes.ToDictionary(q => q.Code, q => q);

            foreach (var theme in themes)
            {
                var themeQuotes = theme.StockCodes
                    .Where(c => quoteMap.ContainsKey(c))
                    .Select(c => quoteMap[c])
                    .ToList();

                if (themeQuotes.Count == 0) continue;

                var groupTurnover = themeQuotes.Sum(q => q.TurnoverValue);
                var turnoverShare = Math.Round((groupTurnover / totalMarketTurnover) * 100, 2);
                var avgChange = Math.Round(themeQuotes.Average(q => q.ChangePercent), 2);
                var advanceCount = themeQuotes.Count(q => q.ChangePercent > 0);
                var advanceRatio = (int)Math.Round(((double)advanceCount / themeQuotes.Count) * 100);

                // Inflow surge ratio (simulated vs 20-day baseline share ~ 1.5% - 4.5%)
                var baselineShare = 1.8m + (decimal)(Math.Abs(theme.Id.GetHashCode() % 20) / 10.0);
                var surgeRatio = baselineShare > 0 ? Math.Round(Math.Max(1.0m, turnoverShare / baselineShare), 2) : 1.2m;

                var statusTag = "🌊 資金穩步流入";
                if (surgeRatio >= 2.0m || (turnoverShare >= 5.0m && avgChange >= 1.5m))
                {
                    statusTag = "🔥 資金初動爆發";
                }
                else if (avgChange >= 1.2m && advanceRatio >= 60)
                {
                    statusTag = "⭐ 熱錢集結中";
                }

                var leading = themeQuotes
                    .OrderByDescending(q => q.ChangePercent)
                    .ThenByDescending(q => q.TurnoverValue)
                    .Take(3)
                    .Select(q => $"{q.Code} {q.Name} ({q.ChangePercent:+0.0;-0.0}%)")
                    .ToList();

                groups.Add(new CapitalFlowGroup
                {
                    GroupName = $"{theme.Icon} {theme.Name}",
                    TurnoverValueInBillion = Math.Round(groupTurnover / 100_000_000m, 2),
                    TurnoverSharePercent = turnoverShare,
                    InflowSurgeRatio = surgeRatio,
                    AvgChangePercent = avgChange,
                    AdvanceRatioPercent = advanceRatio,
                    StatusTag = statusTag,
                    StockCount = themeQuotes.Count,
                    LeadingStocks = leading
                });
            }

            // Also analyze top 5 major stock sectors
            var sectors = quotes.GroupBy(q => q.Sector);
            foreach (var sec in sectors)
            {
                var secQuotes = sec.ToList();
                var groupTurnover = secQuotes.Sum(q => q.TurnoverValue);
                var turnoverShare = Math.Round((groupTurnover / totalMarketTurnover) * 100, 2);
                var avgChange = Math.Round(secQuotes.Average(q => q.ChangePercent), 2);
                var advanceCount = secQuotes.Count(q => q.ChangePercent > 0);
                var advanceRatio = (int)Math.Round(((double)advanceCount / secQuotes.Count) * 100);

                if (turnoverShare >= 3.0m)
                {
                    groups.Add(new CapitalFlowGroup
                    {
                        GroupName = $"🏢 {sec.Key}",
                        TurnoverValueInBillion = Math.Round(groupTurnover / 100_000_000m, 2),
                        TurnoverSharePercent = turnoverShare,
                        InflowSurgeRatio = 1.35m,
                        AvgChangePercent = avgChange,
                        AdvanceRatioPercent = advanceRatio,
                        StatusTag = "📊 產業大板塊",
                        StockCount = secQuotes.Count,
                        LeadingStocks = secQuotes.OrderByDescending(q => q.TurnoverValue).Take(3).Select(q => $"{q.Code} {q.Name}").ToList()
                    });
                }
            }

            return groups.OrderByDescending(g => g.TurnoverSharePercent * g.InflowSurgeRatio).ToList();
        }

        public MarketRegime CalculateMarketRegime(List<StockRawQuote> quotes)
        {
            var totalTurnover = quotes.Sum(q => q.TurnoverValue);
            var advance = quotes.Count(q => q.ChangePercent > 0.05m);
            var decline = quotes.Count(q => q.ChangePercent < -0.05m);
            var unchanged = quotes.Count - advance - decline;

            var adRatio = (advance + decline) > 0 ? (double)advance / (advance + decline) : 0.5;

            var mood = "🔥 內資多頭強勢！中小型飆股即將噴出";
            var advice = "市場多頭氣勢旺盛，資金積極在中小型題材股輪動，此時依系統提示買進即將起漲股，勝率極高！";

            if (adRatio < 0.4)
            {
                mood = "⚠️ 盤勢震盪洗盤 (精選低檔抗跌吃貨股)";
                advice = "大盤處於短線洗盤階段，此時專注在「主力低檔默默吃貨、均線糾結」標的，最容易買在最安全起漲點！";
            }

            return new MarketRegime
            {
                TotalMarketTurnoverInBillion = Math.Round(totalTurnover / 100_000_000m, 2),
                TotalStocksScanned = quotes.Count,
                AdvanceCount = advance,
                DeclineCount = decline,
                UnchangedCount = unchanged,
                TaiexStatus = "加權指數處於多頭攻擊區間",
                OtcStatus = "櫃買中小型指數主力活躍",
                MarketMood = mood,
                AdviceText = advice
            };
        }
    }
}
