using System;
using System.Collections.Generic;
using System.Linq;
using StockAnalyzer.Models;

namespace StockAnalyzer.Services
{
    public class AnalyzerEngine
    {
        private readonly StockDataService _stockDataService;
        private readonly ThematicDataService _thematicService;
        private readonly UsMarketService _usMarketService;

        public AnalyzerEngine(StockDataService stockDataService, ThematicDataService thematicService, UsMarketService usMarketService)
        {
            _stockDataService = stockDataService;
            _thematicService = thematicService;
            _usMarketService = usMarketService;
        }

        public List<StockAnalysisResult> RunFullMarketScan(
            List<StockRawQuote> quotes, 
            ScanFilterParams filterParams,
            List<CapitalFlowGroup> capitalFlows)
        {
            var results = new List<StockAnalysisResult>();
            var hotThemeNames = capitalFlows.Take(5).Select(c => c.GroupName.Replace("🔥", "").Replace("⭐", "").Replace("🌊", "").Trim()).ToList();

            foreach (var q in quotes)
            {
                // Basic liquidity filter
                if (q.VolumeLots < filterParams.MinDailyLots || q.Close <= 5) continue;

                var history = _stockDataService.GetStockHistory(q.Code, q);
                if (history.Count < 2) continue;

                var today = history.Last();
                var prevDay = history.Count >= 2 ? history[^2] : today;

                // Calculate Real Moving Averages & Volume Averages
                var ma5 = today.MA5 > 0 ? today.MA5 : q.Close;
                var ma10 = today.MA10 > 0 ? today.MA10 : q.Close;
                var ma20 = today.MA20 > 0 ? today.MA20 : q.Close;
                var ma60 = today.MA60 > 0 ? today.MA60 : q.Close;
                var vma5 = today.VMA5 > 0 ? today.VMA5 : q.VolumeLots;
                var vma20 = Math.Max(50, today.VMA20 > 0 ? today.VMA20 : q.VolumeLots);

                // Real volume surge ratio
                var volumeSurgeRatio = Math.Round((decimal)q.VolumeLots / vma20, 2);

                // N-day cumulative price change
                var n = Math.Clamp(filterParams.NDays, 1, 10);
                var nDaysAgoIndex = Math.Max(0, history.Count - 1 - n);
                var baseClosePrice = history[nDaysAgoIndex].Close;
                var nDayChangePercent = baseClosePrice > 0 ? Math.Round(((q.Close - baseClosePrice) / baseClosePrice) * 100, 2) : 0;

                // Real MA Entanglement (5MA, 10MA, 20MA, 60MA range %)
                var mas = new[] { ma5, ma10, ma20, ma60 }.Where(x => x > 0).ToList();
                var maMin = mas.Min();
                var maMax = mas.Max();
                var maEntanglement = maMin > 0 ? Math.Round(((maMax - maMin) / maMin) * 100, 2) : 10m;

                // Master K-Line Shield Filters
                var clv = today.CLV; // 0~1
                var clvPercent = Math.Round(clv * 100, 1);
                var upperShadow = today.UpperShadowRatio;
                var upperShadowPercent = Math.Round(upperShadow * 100, 1);
                var isSolidRed = today.Close >= today.Open && q.ChangePercent >= 0;

                // Real Paid-in Capital (in Billion TWD)
                var capitalInBillion = q.CapitalInBillion > 0 ? q.CapitalInBillion : 0m;
                var isGoldenCapital = capitalInBillion >= 10m && capitalInBillion <= 80m;

                // Real Institutional & Overnight Whale Analysis
                var trustLots = q.TrustNetBuyLots;
                var foreignLots = q.ForeignNetBuyLots;
                var dealerLots = q.DealerNetBuyLots;

                string trustStatus = "無";
                if (trustLots >= 500) trustStatus = $"👑 投信大額認養 (+{trustLots}張)";
                else if (trustLots >= 100) trustStatus = $"📈 投信持續買超 (+{trustLots}張)";
                else if (trustLots <= -200) trustStatus = $"⚠️ 投信減碼 ({trustLots}張)";

                // Real Turnover Rate (周轉率 %)
                var totalSharesLots = capitalInBillion > 0 ? (capitalInBillion * 100_000_000m / 10m) / 1000m : 500_000m;
                var turnoverRatePercent = totalSharesLots > 0 ? Math.Round(((decimal)q.VolumeLots / totalSharesLots) * 100, 2) : 0m;

                string overnightRisk = "🛡️ 純淨無污染";
                if (turnoverRatePercent >= 25.0m && upperShadowPercent >= 35.0m)
                {
                    overnightRisk = "⚠️ 隔日沖出貨重災";
                }
                else if (turnoverRatePercent >= 20.0m || (dealerLots < -300 && upperShadowPercent >= 30.0m))
                {
                    overnightRisk = "⚠️ 短線當沖過熱";
                }
                else if (turnoverRatePercent >= 10.0m)
                {
                    overnightRisk = "輕微關注";
                }

                // Anti-Chasing-High Rule
                if (nDayChangePercent > filterParams.MaxPriceChangeInNDays + 4.0m) continue;
                if (q.ChangePercent > 9.8m) continue; // Exclude locked limit-up

                // Match Themes & Thematic Role
                var matchedThemes = _thematicService.MatchThemes(q.Code, q.Name, q.Sector);
                var thematicRole = _thematicService.GetThematicRoleDescription(q.Code, q.Name, matchedThemes);

                // US Stock Linkage Analysis
                var (usTwinName, usTitanChange, usImpactDesc) = _usMarketService.GetUsLinkageForStock(q.Code, q.Name, matchedThemes);

                // Strategy Track Evaluation
                var strategyTracks = new List<string>();
                var signalTags = new List<string>();

                // Track A: 熱錢風口・資金初動型
                var isInHotTheme = matchedThemes.Any(t => hotThemeNames.Any(h => h.Contains(t) || t.Contains(h)));
                if (isInHotTheme && q.ChangePercent >= -0.5m && q.ChangePercent <= 5.0m && volumeSurgeRatio >= 1.3m)
                {
                    strategyTracks.Add("hotmoney");
                    signalTags.Add("🔥 資金初動接棒");
                }

                // Track B: 主力潛伏・默默吃貨型 (Volume surge + price flat + low base)
                if (volumeSurgeRatio >= filterParams.MinSurgeRatio && Math.Abs(nDayChangePercent) <= 5.0m && clv >= 0.50m)
                {
                    strategyTracks.Add("stealth");
                    signalTags.Add("💎 主力低檔吸籌");
                }

                // Track C: 均線糾結・壓縮首根突破型
                if (maEntanglement <= 4.5m && q.Close >= ma5 && q.Close >= ma20 && q.ChangePercent >= 0.5m && volumeSurgeRatio >= 1.3m)
                {
                    strategyTracks.Add("breakout");
                    signalTags.Add("🚀 均線糾結突破");
                }

                // Track D: 洗盤窒息・拉回守穩上車型
                var isPullbackRetest = prevDay.VolumeLots < vma20 * 0.8m && q.Close >= ma20 && q.Close >= ma10 && q.ChangePercent >= 0.0m;
                if (isPullbackRetest)
                {
                    strategyTracks.Add("pullback");
                    signalTags.Add("🎯 拉回量縮守穩");
                }

                if (strategyTracks.Count == 0) continue;

                // Golden Pick: Multiple track intersection
                if (strategyTracks.Count >= 2 || (strategyTracks.Contains("stealth") && strategyTracks.Contains("hotmoney")))
                {
                    strategyTracks.Add("golden");
                    signalTags.Insert(0, "⭐ 雙料即將起漲");
                }

                if (isGoldenCapital) signalTags.Add($"🎯 股本{capitalInBillion}億");
                if (trustLots > 100) signalTags.Add($"👑 投信買超{trustLots}張");
                if (foreignLots > 500) signalTags.Add($"🌐 外資買超{foreignLots}張");
                if (!string.IsNullOrEmpty(usTwinName) && usTitanChange > 1.0m) signalTags.Add($"{usTwinName} {usTitanChange:+0.0;-0.0}%");

                // Filter by Strategy Track if specified
                if (filterParams.StrategyTrack != "all")
                {
                    if (filterParams.StrategyTrack == "golden" && !strategyTracks.Contains("golden")) continue;
                    if (filterParams.StrategyTrack != "golden" && !strategyTracks.Contains(filterParams.StrategyTrack)) continue;
                }

                if (filterParams.RequireGoldenCapital && !isGoldenCapital) continue;
                if (filterParams.RequireCleanChips && overnightRisk.Contains("⚠️")) continue;

                // Defense Stop Loss & Take Profit Calculations
                var defensivePrice = Math.Round(Math.Min(ma20 > 0 ? ma20 : q.Close * 0.95m, q.Low * 0.985m), 2);
                var stopLossPercent = Math.Round(((q.Close - defensivePrice) / q.Close) * 100, 2);
                if (stopLossPercent <= 0) stopLossPercent = 3.5m;

                var targetPrice = Math.Round(q.Close * (1.0m + Math.Max(0.08m, stopLossPercent * 0.03m)), 2);
                var potentialProfitPercent = Math.Round(((targetPrice - q.Close) / q.Close) * 100, 2);
                var riskReward = stopLossPercent > 0 ? Math.Round(potentialProfitPercent / stopLossPercent, 1) : 3.5m;
                var trailingStopPrice = Math.Round(Math.Max(ma10, ma20), 2);

                // Master Score (0~100)
                var score = 65;
                if (strategyTracks.Contains("golden")) score += 15;
                if (isGoldenCapital) score += 5;
                if (trustLots > 100) score += 5;
                if (foreignLots > 200) score += 3;
                if (isSolidRed) score += 4;
                if (clv >= 0.7m) score += 4;
                if (volumeSurgeRatio >= 1.8m) score += 4;
                score = Math.Min(99, score);

                var narrative = BuildActionNarrative(q, strategyTracks, matchedThemes, thematicRole, usTwinName, usTitanChange, volumeSurgeRatio, capitalInBillion, trustLots, foreignLots);

                results.Add(new StockAnalysisResult
                {
                    Code = q.Code,
                    Name = q.Name,
                    Market = q.Market,
                    Sector = q.Sector,
                    CurrentPrice = q.Close,
                    Change = q.Change,
                    ChangePercent = q.ChangePercent,
                    VolumeLots = q.VolumeLots,
                    TurnoverValueInMillion = Math.Round(q.TurnoverValue / 1_000_000m, 1),
                    
                    VolumeSurgeRatio = volumeSurgeRatio,
                    VMA5Lots = vma5,
                    VMA20Lots = vma20,
                    MA5 = ma5,
                    MA10 = ma10,
                    MA20 = ma20,
                    MA60 = ma60,
                    MAEntanglementPercent = maEntanglement,
                    PriceChangeInNDays = nDayChangePercent,

                    CLVPercent = clvPercent,
                    UpperShadowPercent = upperShadowPercent,
                    IsSolidRed = isSolidRed,
                    CapitalInBillion = capitalInBillion,
                    IsGoldenCapital = isGoldenCapital,

                    OvernightWhaleRisk = overnightRisk,
                    TrustStatus = trustStatus,
                    TrustNetBuyLots = trustLots,
                    ForeignNetBuyLots = foreignLots,
                    DealerNetBuyLots = dealerLots,

                    DefensivePrice = defensivePrice,
                    StopLossPercent = stopLossPercent,
                    TargetPrice = targetPrice,
                    PotentialProfitPercent = potentialProfitPercent,
                    RiskRewardRatio = riskReward,
                    TrailingStopPrice = trailingStopPrice,

                    UsTwinName = usTwinName,
                    UsTitanChangePercent = usTitanChange,
                    UsLinkageImpact = usImpactDesc,

                    MatchedThemes = matchedThemes,
                    ThematicRole = thematicRole,
                    StrategyTracks = strategyTracks,
                    MasterScore = score,
                    SignalTags = signalTags.Distinct().ToList(),
                    Narrative = narrative
                });
            }

            return results.OrderByDescending(r => r.MasterScore).ThenByDescending(r => r.VolumeSurgeRatio).ToList();
        }

        private string BuildActionNarrative(
            StockRawQuote q, 
            List<string> tracks, 
            List<string> themes, 
            string thematicRole,
            string usTwinName,
            decimal usTitanChange,
            decimal surgeRatio,
            decimal capitalInBillion,
            long trustLots,
            long foreignLots)
        {
            var themeStr = themes.Count > 0 ? string.Join("、", themes.Take(2)) : q.Sector;
            var usStr = !string.IsNullOrEmpty(usTwinName) ? $"連動美股 {usTwinName} ({usTitanChange:+0.0;-0.0}%)，" : "";
            var capStr = capitalInBillion > 0 ? $"實收股本 {capitalInBillion:0.0} 億" : "";
            var instStr = trustLots > 0 ? $"投信買超 {trustLots} 張" : (foreignLots > 0 ? $"外資買超 {foreignLots} 張" : "");

            if (tracks.Contains("golden"))
            {
                return $"【雙料起漲極品】{q.Name} 兼具資金風口與主力吸籌特徵，今日成交量放大 {surgeRatio} 倍，{usStr}{capStr}籌碼集中度高，建議嚴守停損點逢低分批佈局。";
            }
            if (tracks.Contains("stealth"))
            {
                return $"【主力低檔吸籌】{q.Name} 股價於底部區間整理，今日單日放量 {surgeRatio} 倍但漲幅溫和未被市場散戶發覺，{instStr}主力吃貨跡象明確。";
            }
            if (tracks.Contains("hotmoney"))
            {
                return $"【熱錢初動接棒】{q.Name} 隸屬於熱門題材「{themeStr}」，{usStr}獲主力買盤第一時間點火進駐，具備強烈續攻潛力。";
            }
            if (tracks.Contains("breakout"))
            {
                return $"【均線糾結突破】{q.Name} 經多日壓縮整理，均線糾結後今日首根帶量突破，技術面翻多確立，適合順勢操作。";
            }
            return $"【拉回守穩上車】{q.Name} 回測月季線支撐守穩，量縮洗盤乾淨，風險報酬比極佳。";
        }
    }
}
