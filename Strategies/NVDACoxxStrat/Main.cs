using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Data.Market;
namespace QuantConnect.Algorithm.CSharp
{
    public class NVDAAdvancedAIStrategy : QCAlgorithm
    {
        // Strategy Parameters
        private int _macdFast = 12;
        private int _macdSlow = 26;
        // Position tracking
        private Symbol _nvdasymbol;
        private Symbol _soxxsymbol;
        private bool _isPositionOpen = false;
        private decimal _entryPrice = 0;
        private decimal _positionSize = 0;
        private int _greenDaysCount = 0;
        private decimal _recentPeakPrice = 0;
        // Indicators
        private BollingerBands _bb;
        private MovingAverageConvergenceDivergence _macd;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;
        private RollingWindow<decimal> _volumeWindow;
        private RollingWindow<decimal> _priceWindow;
        private IntradayVwap _vwap;
        // Risk management
        private decimal _maxSectorAllocation = 0.25m;
        private bool _suspendedEntries = false;
        private decimal _currentExposure = 1.0m;
        // Event tracking
        private Dictionary<DateTime, string> _aiEvents;
        private DateTime _eventActivationTime;
        public override void Initialize()
        {
            SetStartDate(2025, 1, 1);
            SetEndDate(2025, 2, 28);
            SetCash(100000);
            _nvdasymbol = AddEquity("nvda", Resolution.Minute).Symbol;
            _soxxsymbol = AddEquity("soxx", Resolution.Daily).Symbol;
            // Initialize indicators
            _bb = BB(_nvdasymbol, 20, 2, MovingAverageType.Simple, Resolution.Daily);
            _macd = MACD(_nvdasymbol, _macdFast, _macdSlow, 9, MovingAverageType.Exponential, Resolution.Daily);
            _rsi = RSI(_nvdasymbol, 14, MovingAverageType.Wilders, Resolution.Daily);
            _atr = ATR(_nvdasymbol, 14, MovingAverageType.Simple, Resolution.Daily);
            _vwap = new IntradayVwap(_nvdasymbol);
            _volumeWindow = new RollingWindow<decimal>(21);
            _priceWindow = new RollingWindow<decimal>(50);
            // Set up AI events calendar
            InitializeAIEvents();
            // Warm up period
            SetWarmUp(300, Resolution.Minute);
            // Risk settings
            Settings.FreePortfolioValuePercentage = 0.02m;
        }
        private void InitializeAIEvents()
        {
            _aiEvents = new Dictionary<DateTime, string>
        {
        { new DateTime(2024, 3, 18), "NVIDIA GTC" },
        { new DateTime(2024, 1, 9), "CES" },
        { new DateTime(2024, 6, 4), "COMPUTEX" },
        { new DateTime(2024, 8, 19), "SIGGRAPH" }
        };
        }
        public override void OnData(Slice data)
        {
            if (IsWarmingUp) return;
            if (!data.Bars.ContainsKey(_nvdasymbol) || !data.Bars.ContainsKey(_soxxsymbol)) return;
            var nvdaBar = data.Bars[_nvdasymbol];
            var soxxBar = data.Bars[_soxxsymbol];
            // Update rolling windows
            _volumeWindow.Add(nvdaBar.Volume);
            _priceWindow.Add(nvdaBar.Close);
            _vwap.Update(nvdaBar);
            // Check for emergency stop
            CheckEmergencyStop(nvdaBar);
            // Update risk filters
            UpdateRiskFilters(soxxBar);
            // Check AI event activation
            CheckAIEventActivation();
            if (_isPositionOpen)
            {
                ManageExistingPosition(nvdaBar);
            }
            else if (!_suspendedEntries)
            {
                CheckEntryConditions(nvdaBar, soxxBar);
            }
        }
        private void CheckEmergencyStop(TradeBar nvdaBar)
        {
            if (_isPositionOpen)
            {
                var dailyReturn = (nvdaBar.Close - _entryPrice) / _entryPrice;
                if (dailyReturn <= -0.12m)
                {
                    Liquidate(_nvdasymbol, "Emergency Stop");
                    _isPositionOpen = false;
                    Log("Emergency stop triggered at " + nvdaBar.Close);
                }
            }
        }
        private void UpdateRiskFilters(TradeBar soxxBar)
        {
            // Check SOXX 5-day drop
            var soxxHistory = History(_soxxsymbol, 5, Resolution.Daily);
            if (soxxHistory.Count() >= 5)
            {
                var soxxReturns = soxxHistory.Select(x => (double)x.Close).ToArray();
                var fiveDayReturn = (soxxReturns[4] - soxxReturns[0]) / soxxReturns[0];
                _suspendedEntries = fiveDayReturn <= -0.08;
            }
            // Check VIX spike (simplified)
            var currentVolatility = _atr.Current.Value / _priceWindow[0];
            if (currentVolatility > 0.035m) // Equivalent to VIX > 35
            {
                _currentExposure = 0.3m;
            }
            else
            {
                _currentExposure = 1.0m;
            }
            // Fed meeting day check (simplified)
            if (Time.DayOfWeek == DayOfWeek.Wednesday && Time.Day <= 7)
            {
                _currentExposure = Math.Min(_currentExposure, 0.5m);
            }
        }
        private void CheckAIEventActivation()
        {
            foreach (var aiEvent in _aiEvents)
            {
                if (Time >= aiEvent.Key.AddHours(-72) && Time <= aiEvent.Key.AddHours(24))
                {
                    _eventActivationTime = aiEvent.Key;
                    break;
                }
            }
        }
        private void CheckEntryConditions(TradeBar nvdaBar, TradeBar soxxBar)
        {
            if (!_bb.IsReady || !_macd.IsReady || !_rsi.IsReady) return;
            // Calculate volume surge
            var medianVolume = Median(_volumeWindow);
            var volumeSurge = _volumeWindow.IsReady && medianVolume > 0 ?
                nvdaBar.Volume / medianVolume : 0;
            // Calculate SOXX 5-day return
            var soxxHistory = History(_soxxsymbol, 5, Resolution.Daily);
            var soxxReturn = soxxHistory.Count() >= 5 ?
            (soxxBar.Close - soxxHistory.First().Close) / soxxHistory.First().Close : 0;
            // Breakout Entry Conditions
            if (nvdaBar.Close > _bb.UpperBand &&
            _macd > _macd.Signal &&
            soxxReturn > 0.02m &&
            volumeSurge > 2.0m)
            {
                EnterLongPosition(nvdaBar.Close, "Breakout Entry");
                return;
            }
            // Dip-Buying Entry Conditions (simplified)
            var fiftyTwoWeekHigh = _priceWindow.IsReady ? _priceWindow.Max() : nvdaBar.Close;
            var retracement = (fiftyTwoWeekHigh - nvdaBar.Close) / fiftyTwoWeekHigh;
            if (retracement >= 0.15m &&
            CheckPositiveDivergence() &&
            CheckInstitutionalAccumulation())
            {
                EnterLongPosition(nvdaBar.Close, "Dip-Buying Entry");
            }
        }
        private bool CheckPositiveDivergence()
        {
            if (_priceWindow.Count < 10 || !_rsi.IsReady) return false;
            // Simplified divergence check
            var recentLows = new List<decimal>();
            var recentRSILows = new List<decimal>();
            for (int i = 0; i < 5; i++)
            {
                if (_priceWindow.Count > i + 5)
                {
                    recentLows.Add(_priceWindow[i]);
                    // Note: RSI history would need proper implementation
                }
            }
            return recentLows.Count >= 2 && recentLows[0] < recentLows[1];
        }
        private bool CheckInstitutionalAccumulation()
        {
            if (_volumeWindow.Count < 21) return false;
            // Simplified accumulation check using volume-price relationship
            var volumePriceSlope = CalculateVolumePriceSlope();
            return volumePriceSlope > 0.7m;
        }
        private decimal CalculateVolumePriceSlope()
        {
            // Simplified slope calculation
            if (_priceWindow.Count < 10) return 0;
            var recentPrices = _priceWindow.Take(10).ToArray();
            var recentVolumes = _volumeWindow.Take(10).ToArray();
            decimal sumXY = 0, sumX = 0, sumY = 0, sumX2 = 0;
            for (int i = 0; i < recentPrices.Length; i++)
            {
                sumXY += recentPrices[i] * recentVolumes[i];
                sumX += recentPrices[i];
                sumY += recentVolumes[i];
                sumX2 += recentPrices[i] * recentPrices[i];
            }
            var n = recentPrices.Length;
            var denominator = n * sumX2 - sumX * sumX;
            return denominator != 0 ? (n * sumXY - sumX * sumY) / denominator : 0;
        }
        private void EnterLongPosition(decimal currentPrice, string reason)
        {
            if (_isPositionOpen) return;
            // VAR-based position sizing
            var cvar = 0.05m; // 95% CVaR
            var daysHeld = 10; // Expected holding period
            var positionValue = (cvar * Portfolio.TotalPortfolioValue) /
            (_atr.Current.Value * (decimal)Math.Sqrt(daysHeld));
            positionValue *= _currentExposure;
            positionValue = Math.Min(positionValue, _maxSectorAllocation * Portfolio.TotalPortfolioValue);
            _positionSize = Math.Round(positionValue / currentPrice);
            var ticket = MarketOrder(_nvdasymbol, _positionSize);
            if (ticket.Status == OrderStatus.Filled)
            {
                _isPositionOpen = true;
                _entryPrice = currentPrice;
                _recentPeakPrice = currentPrice;
                _greenDaysCount = 0;
                Log($"Entered long position at {currentPrice}: {reason}");
            }
        }
        private void ManageExistingPosition(TradeBar nvdaBar)
        {
            var currentProfit = (nvdaBar.Close - _entryPrice) / _entryPrice;
            // Update recent peak and green days count
            if (nvdaBar.Close > _recentPeakPrice)
            {
                _recentPeakPrice = nvdaBar.Close;
                _greenDaysCount++;
            }
            // Profit pyramid targets
            if (currentProfit >= 0.15m && Portfolio[_nvdasymbol].Quantity == _positionSize)
            {
                // First target hit - trim 40%
                var trimQuantity = Math.Round(_positionSize * 0.4m);
                MarketOrder(_nvdasymbol, -trimQuantity);
                Log("First profit target hit - trimmed 40%");
            }
            else if (currentProfit >= 0.15m && Portfolio[_nvdasymbol].Quantity > _positionSize * 0.6m)
            {
                // Second target hit - trim 30%
                var trimQuantity = Math.Round(_positionSize * 0.3m);
                MarketOrder(_nvdasymbol, -trimQuantity);
                Log("Second profit target hit - trimmed 30%");
            }
            // Trailing stop logic
            var stopDistance = _greenDaysCount >= 5 ? 0.03m : 0.07m;
            var stopPrice = _recentPeakPrice * (1 - stopDistance);
            if (nvdaBar.Close <= stopPrice)
            {
                Liquidate(_nvdasymbol, "Trailing stop");
                _isPositionOpen = false;
                Log($"Trailing stop triggered at {nvdaBar.Close}");
            }
        }
        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            if (orderEvent.Status == OrderStatus.Filled)
            {
                Log($"Order filled: {orderEvent}");
            }
        }
        // Helper to calculate median for RollingWindow<decimal>
        private decimal Median(RollingWindow<decimal> window)
        {
            var sorted = window.OrderBy(x => x).ToList();
            int count = sorted.Count;
            if (count == 0) return 0;
            if (count % 2 == 1)
                return sorted[count / 2];
            else
                return (sorted[(count / 2) - 1] + sorted[count / 2]) / 2;
        }
    }
}