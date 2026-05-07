using System;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;

public class PLTRAlgorithm : QCAlgorithm
{
    private Symbol _pltr;
    private RelativeStrengthIndex _rsi;
    private ExponentialMovingAverage _ema;
    private AverageTrueRange _atr;
    private IntradayVwap _vwap;
    private decimal _resistanceLevel = 30.0m;
    private SimpleMovingAverage _volume30DayAvg;

    public override void Initialize()
    {
        SetStartDate(2025, 1, 1);
        SetEndDate(2025, 9, 21);
        AddEquity("PLTR", Resolution.Minute);
        _pltr = AddEquity("PLTR").Symbol;
        _rsi = RSI(_pltr, 14);
        _ema = EMA(_pltr, 20);
        _atr = ATR(_pltr, 14);
        _vwap = VWAP(_pltr);
        _volume30DayAvg = SMA(_pltr, 30);
    }

    public override void OnData(Slice data)
    {
        if (!_ema.IsReady || !_rsi.IsReady || !_atr.IsReady || !_vwap.IsReady || !_volume30DayAvg.IsReady) return;
        // Enter long position when close price crosses resistance level, volume is above average and RSI is below 70
        if (data[_pltr].Close > _resistanceLevel && data[_pltr].Close > _volume30DayAvg.Current.Value && _rsi.Current.Value < 70) {
            SetHoldings(_pltr, 1);
        }
        // Exit position when profit targets are reached or stop level is hit
        if (Portfolio[_pltr].UnrealizedProfitPercent > 0.06m || data[_pltr].Close < _vwap.Current.Value - _atr.Current.Value)
        {
            Liquidate(_pltr);
        }
    }
}
