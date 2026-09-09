using System;
using System.Collections.Generic;

namespace StockAnalyzer.Models
{
    public class StockRawQuote
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public long VolumeShares { get; set; } // 股數
        public long VolumeLots => VolumeShares / 1000; // 張數
        public decimal TurnoverValue { get; set; } // 成交金額
        public int Transactions { get; set; } // 成交筆數
        public string Market { get; set; } = "上市"; // 上市 / 上櫃
        public string Sector { get; set; } = "電子";
        public string Date { get; set; } = string.Empty;
        public decimal PrevClose { get; set; }
    }

    public class StockDailyData
    {
        public string Date { get; set; } = string.Empty;
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long VolumeLots { get; set; }
        public decimal TurnoverValue { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public decimal CLV { get; set; } // Close Location Value (0~1)
        public decimal UpperShadowRatio { get; set; } // 上影線佔比
        public decimal MA5 { get; set; }
        public decimal MA10 { get; set; }
        public decimal MA20 { get; set; }
        public decimal MA60 { get; set; }
        public decimal VMA5 { get; set; }
        public decimal VMA20 { get; set; }
    }

    public class StockAnalysisResult
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "上市";
        public string Sector { get; set; } = string.Empty;
        public decimal CurrentPrice { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public long VolumeLots { get; set; }
        public decimal TurnoverValueInMillion { get; set; } // 成交金額(百萬)
        
        // 量能與均線
        public decimal VolumeSurgeRatio { get; set; } // 放量倍數 (vs 20VMA)
        public decimal VMA5Lots { get; set; }
        public decimal VMA20Lots { get; set; }
        public decimal MA5 { get; set; }
        public decimal MA10 { get; set; }
        public decimal MA20 { get; set; }
        public decimal MA60 { get; set; }
        public decimal MAEntanglementPercent { get; set; } // 均線糾結度 (5/10/20/60MA 差距 %)
        public decimal PriceChangeInNDays { get; set; } // 近 N 天累計漲跌幅 %

        // 高手防護指標
        public decimal CLVPercent { get; set; } // 收盤強度 %
        public decimal UpperShadowPercent { get; set; } // 上影線佔比 %
        public bool IsSolidRed { get; set; } // 實體紅K
        public decimal CapitalInBillion { get; set; } // 股本(億元)
        public bool IsGoldenCapital { get; set; } // 10~80億黃金爆發股本

        // 籌碼排毒與投信認養
        public string OvernightWhaleRisk { get; set; } = "純淨安全"; // 純淨安全 / 輕微關注 / ⚠️ 隔日沖重災
        public string TrustStatus { get; set; } = "無"; // 👑 投信初胚認養 / 投信持續買超 / 無
        public long TrustNetBuyLots { get; set; } // 投信買超張數

        // 風報比與防守停損停利
        public decimal DefensivePrice { get; set; } // 建議防守停損價
        public decimal StopLossPercent { get; set; } // 停損幅度 %
        public decimal TargetPrice { get; set; } // 預期第一波段目標價
        public decimal PotentialProfitPercent { get; set; } // 預期波段利潤 %
        public decimal RiskRewardRatio { get; set; } // 風報比 (如 4.5 代表 1:4.5)
        public decimal TrailingStopPrice { get; set; } // 移動停利參考價 (沿10MA/20MA)

        // 美股連動指標
        public string UsTwinName { get; set; } = string.Empty; // 連動美股巨頭 (如 輝達 NVDA / 蘋果 AAPL)
        public decimal UsTitanChangePercent { get; set; } // 美股對應巨頭漲跌 %
        public string UsLinkageImpact { get; set; } = string.Empty; // 美股外溢效應解讀

        // 題材與策略標籤
        public List<string> MatchedThemes { get; set; } = new();
        public string ThematicRole { get; set; } = string.Empty; // 產業鏈與主力產品解讀
        public List<string> StrategyTracks { get; set; } = new(); // "Golden", "HotMoney", "Stealth"
        public int MasterScore { get; set; } // 綜合大師評分 (0~100)
        public List<string> SignalTags { get; set; } = new(); // 標籤清單
        public string Narrative { get; set; } = string.Empty; // 操盤解讀
    }

    public class CapitalFlowGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public decimal TurnoverValueInBillion { get; set; } // 該族群總成交金額 (億)
        public decimal TurnoverSharePercent { get; set; } // 佔全市場成交比重 %
        public decimal InflowSurgeRatio { get; set; } // 資金暴增倍數 (vs 近20日平均)
        public decimal AvgChangePercent { get; set; } // 族群平均漲跌幅 %
        public int AdvanceRatioPercent { get; set; } // 上漲家數比例 %
        public string StatusTag { get; set; } = "🔥 資金初動"; // 🔥 資金初動 / 🌊 熱錢聚焦 / 穩健輪動
        public int StockCount { get; set; }
        public List<string> LeadingStocks { get; set; } = new();
    }

    public class MarketRegime
    {
        public decimal TotalMarketTurnoverInBillion { get; set; } // 全市場成交量 (億)
        public int TotalStocksScanned { get; set; }
        public int AdvanceCount { get; set; } // 上漲家數
        public int DeclineCount { get; set; } // 下跌家數
        public int UnchangedCount { get; set; } // 平盤家數
        public decimal ADRatioPercent => TotalStocksScanned > 0 ? (decimal)AdvanceCount / (AdvanceCount + DeclineCount + 0.0001m) * 100 : 50;
        public string TaiexStatus { get; set; } = "站穩月季線多頭"; // 加權指數狀態
        public string OtcStatus { get; set; } = "中小型內資積極做多"; // 櫃買指數狀態
        public string MarketMood { get; set; } = "🔥 內資多頭狂歡 (積極做多)";
        public string AdviceText { get; set; } = "大盤處於安全多頭區，中小型中強勢飆股勝率極高，可積極按照雙軌策略進場！";
        public string TradeDate { get; set; } = string.Empty;
        public string ScanTime { get; set; } = string.Empty;
    }

    public class ThemeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public List<string> StockCodes { get; set; } = new();
        public decimal HeatScore { get; set; } // 熱度得分
        public decimal DailyAvgChangePercent { get; set; }
        public decimal DailyAvgSurgeRatio { get; set; }
        public int ComponentCount => StockCodes.Count;
    }

    public class ScanFilterParams
    {
        public string StrategyTrack { get; set; } = "all"; // all, golden, hotmoney, stealth
        public int NDays { get; set; } = 3; // 觀察天數 (1~10)
        public decimal MinSurgeRatio { get; set; } = 1.6m; // 放量倍數 (1.2 ~ 5.0)
        public decimal MaxPriceChangeInNDays { get; set; } = 5.0m; // 近 N 天累計漲幅上限 %
        public long MinDailyLots { get; set; } = 300; // 最低日成交張數
        public bool RequireGoldenCapital { get; set; } = false; // 是否強制 10~80 億股本
        public bool RequireCleanChips { get; set; } = true; // 是否強制隔日沖排毒
        public string ThemeFilter { get; set; } = "all"; // 特定題材 ID 或 all
        public string SectorFilter { get; set; } = "all"; // 特定產業或 all
    }

    public class ScanResponse
    {
        public MarketRegime MarketRegime { get; set; } = new();
        public UsMarketSummary UsMarket { get; set; } = new(); // 美股連動氣象台
        public List<CapitalFlowGroup> CapitalFlows { get; set; } = new();
        public List<ThemeDefinition> DynamicThemes { get; set; } = new();
        public List<StockAnalysisResult> Results { get; set; } = new();
        public int TotalCandidatesCount { get; set; }
        public string ScanTime { get; set; } = string.Empty;
        public string TradeDate { get; set; } = string.Empty;
        public string DataSourceNote { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Password { get; set; } = string.Empty;
    }
}
