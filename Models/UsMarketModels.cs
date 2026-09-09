using System;
using System.Collections.Generic;

namespace StockAnalyzer.Models
{
    public class UsMarketQuote
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string LinkedTwSector { get; set; } = string.Empty; // 連動台股板塊
        public string SentimentText { get; set; } = string.Empty; // 多空評價
    }

    public class UsMarketSummary
    {
        public List<UsMarketQuote> Indices { get; set; } = new();
        public List<UsMarketQuote> TechTitans { get; set; } = new();
        public decimal SoxChangePercent { get; set; } // 費半漲跌 %
        public decimal TsmAdrChangePercent { get; set; } // 台積電 ADR 漲跌 %
        public decimal NvdaChangePercent { get; set; } // 輝達漲跌 %
        public string UsImpactOnTaiwan { get; set; } = "🔥 美股科技股大漲，強力帶動台股 AI 與半導體開高走揚！";
        public string UsMoodColor { get; set; } = "text-red";
    }
}
