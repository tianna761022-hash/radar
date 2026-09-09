let chartInstance = null;

function renderStockChart(stockData, historyList, defensivePrice, targetPrice) {
    const chartDom = document.getElementById('modalEChart');
    if (!chartDom) return;

    if (chartInstance) {
        chartInstance.dispose();
    }
    chartInstance = echarts.init(chartDom);

    if (!historyList || historyList.length === 0) {
        return;
    }

    const dates = historyList.map(item => item.date);
    // ECharts Candlestick format: [Open, Close, Lowest, Highest]
    const ohlcData = historyList.map(item => [item.open, item.close, item.low, item.high]);
    const volumes = historyList.map((item, idx) => {
        const isUp = item.close >= item.open;
        return {
            value: item.volumeLots,
            itemStyle: {
                color: isUp ? '#ff3b5c' : '#10b981'
            }
        };
    });

    const ma5 = historyList.map(item => item.mA5);
    const ma10 = historyList.map(item => item.mA10);
    const ma20 = historyList.map(item => item.mA20);
    const ma60 = historyList.map(item => item.mA60);
    const vma5 = historyList.map(item => item.vmA5);
    const vma20 = historyList.map(item => item.vmA20);

    const option = {
        backgroundColor: 'transparent',
        animation: true,
        tooltip: {
            trigger: 'axis',
            axisPointer: {
                type: 'cross',
                lineStyle: {
                    color: '#64748b',
                    width: 1,
                    type: 'dashed'
                }
            },
            backgroundColor: 'rgba(15, 23, 42, 0.95)',
            borderColor: 'rgba(255, 255, 255, 0.1)',
            textStyle: {
                color: '#f8fafc',
                fontSize: 12
            },
            formatter: function (params) {
                const candle = params.find(p => p.seriesType === 'candlestick');
                if (!candle) return '';
                const data = candle.data;
                const idx = candle.dataIndex;
                const date = dates[idx];
                const open = data[1];
                const close = data[2];
                const low = data[3];
                const high = data[4];
                const vol = historyList[idx].volumeLots;
                const change = historyList[idx].change;
                const changePct = historyList[idx].changePercent;
                const isRise = close >= open;
                const color = isRise ? '#ff3b5c' : '#10b981';

                return `
                    <div style="font-weight:bold; margin-bottom:4px; color:#38bdf8;">${date}</div>
                    <div style="color:${color}; font-size:13px; font-weight:bold;">
                        收盤: ${close} (${change >= 0 ? '+' : ''}${change} / ${changePct}%)
                    </div>
                    <div style="font-size:11px; color:#cbd5e1; margin-top:2px;">
                        開: ${open} | 高: ${high} | 低: ${low}
                    </div>
                    <div style="font-size:11px; color:#f59e0b; margin-top:2px;">
                        成交張數: ${vol.toLocaleString()} 張
                    </div>
                `;
            }
        },
        axisPointer: {
            link: [{ xAxisIndex: 'all' }]
        },
        grid: [
            {
                left: '8%',
                right: '4%',
                top: '8%',
                height: '52%'
            },
            {
                left: '8%',
                right: '4%',
                top: '68%',
                height: '24%'
            }
        ],
        xAxis: [
            {
                type: 'category',
                data: dates,
                scale: true,
                boundaryGap: false,
                axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
                axisLabel: { color: '#94a3b8', fontSize: 10 },
                splitLine: { show: false }
            },
            {
                type: 'category',
                gridIndex: 1,
                data: dates,
                scale: true,
                boundaryGap: false,
                axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
                axisLabel: { show: false },
                splitLine: { show: false }
            }
        ],
        yAxis: [
            {
                scale: true,
                splitArea: { show: false },
                axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
                axisLabel: { color: '#94a3b8', fontSize: 10 },
                splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } }
            },
            {
                scale: true,
                gridIndex: 1,
                splitNumber: 2,
                axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
                axisLabel: { color: '#94a3b8', fontSize: 9 },
                splitLine: { show: false }
            }
        ],
        series: [
            {
                name: 'K線',
                type: 'candlestick',
                data: ohlcData,
                itemStyle: {
                    color: '#ff3b5c', // Bullish Red body
                    color0: '#10b981', // Bearish Green body
                    borderColor: '#ff3b5c',
                    borderColor0: '#10b981'
                },
                markPoint: {
                    data: [
                        {
                            name: '爆量起漲點',
                            coord: [dates[dates.length - 1], historyList[historyList.length - 1].close],
                            value: '💥 爆量表態',
                            itemStyle: { color: '#ff3b5c' },
                            label: { fontSize: 10, color: '#fff', fontWeight: 'bold' }
                        }
                    ]
                },
                markLine: {
                    symbol: ['none', 'none'],
                    data: [
                        {
                            yAxis: defensivePrice,
                            name: '防守停損線',
                            lineStyle: { color: '#10b981', type: 'dashed', width: 1.5 },
                            label: { formatter: '🛡️ 防守 ' + defensivePrice, position: 'insideEndTop', color: '#10b981', fontSize: 10 }
                        },
                        {
                            yAxis: targetPrice,
                            name: '目標價',
                            lineStyle: { color: '#ff3b5c', type: 'dashed', width: 1.5 },
                            label: { formatter: '🚀 目標 ' + targetPrice, position: 'insideEndTop', color: '#ff3b5c', fontSize: 10 }
                        }
                    ]
                }
            },
            {
                name: 'MA5',
                type: 'line',
                data: ma5,
                smooth: true,
                showSymbol: false,
                lineStyle: { width: 1.2, color: '#f59e0b' }
            },
            {
                name: 'MA10',
                type: 'line',
                data: ma10,
                smooth: true,
                showSymbol: false,
                lineStyle: { width: 1.2, color: '#3b82f6' }
            },
            {
                name: 'MA20',
                type: 'line',
                data: ma20,
                smooth: true,
                showSymbol: false,
                lineStyle: { width: 1.8, color: '#ec4899' }
            },
            {
                name: 'MA60',
                type: 'line',
                data: ma60,
                smooth: true,
                showSymbol: false,
                lineStyle: { width: 1.2, color: '#10b981' }
            },
            {
                name: '成交量',
                type: 'bar',
                xAxisIndex: 1,
                yAxisIndex: 1,
                data: volumes
            },
            {
                name: 'VMA5',
                type: 'line',
                xAxisIndex: 1,
                yAxisIndex: 1,
                data: vma5,
                smooth: true,
                showSymbol: false,
                lineStyle: { width: 1, color: '#38bdf8' }
            },
            {
                name: 'VMA20',
                type: 'line',
                xAxisIndex: 1,
                yAxisIndex: 1,
                data: vma20,
                smooth: true,
                showSymbol: false,
                lineStyle: { width: 1.2, color: '#f59e0b' }
            }
        ]
    };

    chartInstance.setOption(option);
}

window.addEventListener('resize', () => {
    if (chartInstance) {
        chartInstance.resize();
    }
});
