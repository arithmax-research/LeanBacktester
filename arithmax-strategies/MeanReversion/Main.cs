#region imports
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Globalization;
    using System.Drawing;
    using QuantConnect;
    using QuantConnect.Algorithm.Framework;
    using QuantConnect.Algorithm.Framework.Selection;
    using QuantConnect.Algorithm.Framework.Alphas;
    using QuantConnect.Algorithm.Framework.Portfolio;
    using QuantConnect.Algorithm.Framework.Portfolio.SignalExports;
    using QuantConnect.Algorithm.Framework.Execution;
    using QuantConnect.Algorithm.Framework.Risk;
    using QuantConnect.Algorithm.Selection;
    using QuantConnect.Api;
    using QuantConnect.Parameters;
    using QuantConnect.Benchmarks;
    using QuantConnect.Brokerages;
    using QuantConnect.Commands;
    using QuantConnect.Configuration;
    using QuantConnect.Util;
    using QuantConnect.Interfaces;
    using QuantConnect.Algorithm;
    using QuantConnect.Indicators;
    using QuantConnect.Data;
    using QuantConnect.Data.Auxiliary;
    using QuantConnect.Data.Consolidators;
    using QuantConnect.Data.Custom;
    using QuantConnect.Data.Custom.IconicTypes;
    using QuantConnect.DataSource;
    using QuantConnect.Data.Fundamental;
    using QuantConnect.Data.Market;
    using QuantConnect.Data.Shortable;
    using QuantConnect.Data.UniverseSelection;
    using QuantConnect.Notifications;
    using QuantConnect.Orders;
    using QuantConnect.Orders.Fees;
    using QuantConnect.Orders.Fills;
    using QuantConnect.Orders.OptionExercise;
    using QuantConnect.Orders.Slippage;
    using QuantConnect.Orders.TimeInForces;
    using QuantConnect.Python;
    using QuantConnect.Scheduling;
    using QuantConnect.Securities;
    using QuantConnect.Securities.Equity;
    using QuantConnect.Securities.Future;
    using QuantConnect.Securities.Option;
    using QuantConnect.Securities.Positions;
    using QuantConnect.Securities.Forex;
    using QuantConnect.Securities.Crypto;
    using QuantConnect.Securities.CryptoFuture;
    using QuantConnect.Securities.IndexOption;
    using QuantConnect.Securities.Interfaces;
    using QuantConnect.Securities.Volatility;
    using QuantConnect.Storage;
    using QuantConnect.Statistics;
    using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
    using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
    using Calendar = QuantConnect.Data.Consolidators.Calendar;
#endregion
namespace QuantConnect.Algorithm.CSharp
{
    public class MeanReversion : QCAlgorithm
    {
        // ETF symbols with intraday patterns - all available in daily data
        private readonly List<string> _symbols = new List<string> { "TQQQ", "UDOW", "DIG", "UGL", "UPRO", "TMF", "SPY" };
        
        // Strategy parameters - Multi-day hold strategy
        private decimal _positionSize = 1.0m / 7m; // Equal weight allocation (1/7 for each ETF)
        private int _lookbackPeriod = 20; // Minutes for moving average calculation
        private int _rebalanceFrequency = 4; // Rebalance every 4 days
        private int _daysSinceLastRebalance = 0; // Track days since last rebalance
        
        // Trading times and state tracking
        private TimeSpan _buyTime = new TimeSpan(9, 30, 0);  // Buy at market open
        private TimeSpan _sellTime = new TimeSpan(15, 0, 0); // Sell at 3:00 PM
        private bool _hasBoughtToday = false; // Track if we've bought today
        private bool _hasSoldToday = false;   // Track if we've sold today
        private DateTime _lastTradingDate = DateTime.MinValue; // Track last trading date
        
        // Rolling windows to store price data for each symbol
        private Dictionary<string, RollingWindow<decimal>> _priceWindows;
        private Dictionary<string, SimpleMovingAverage> _movingAverages;
        
        // Timing parameters for daily trading
        private TimeSpan _marketCloseBuffer = new TimeSpan(15, 45, 0); // 15 minutes before close

        public override void Initialize()
        {
            // Set strategy parameters for the available data period
            SetStartDate(2024, 1, 1);
            SetEndDate(2024, 12, 31);
            SetCash(100000);
            
            // Initialize data structures
            _priceWindows = new Dictionary<string, RollingWindow<decimal>>();
            _movingAverages = new Dictionary<string, SimpleMovingAverage>();
            
            // Add equity data for each symbol - using minute resolution for precise timing
            foreach (var symbol in _symbols)
            {
                var equity = AddEquity(symbol, Resolution.Minute);
                
                // Initialize rolling window and moving average for each symbol
                _priceWindows[symbol] = new RollingWindow<decimal>(_lookbackPeriod);
                _movingAverages[symbol] = new SimpleMovingAverage(_lookbackPeriod);
            }
            
            // Set SPY as benchmark
            SetBenchmark("SPY");
            
            // Schedule daily cleanup before market close (keep as backup)
            Schedule.On(DateRules.EveryDay(), TimeRules.At(15, 45), CloseAllPositions);
            
            Debug("Multi-Day Hold Strategy Initialized with ETFs: " + string.Join(", ", _symbols));
            Debug("Strategy: Buy and hold for multiple days - Rebalance every 4 days");
            Debug($"Equal weight allocation: {_positionSize:P} per ETF");
        }

        public override void OnData(Slice data)
        {
            var currentTime = Time.TimeOfDay;
            
            // Update price data for moving averages (for potential future use)
            foreach (var symbol in _symbols)
            {
                if (data.ContainsKey(symbol) && data[symbol] != null)
                {
                    var tradeBar = data[symbol];
                    _priceWindows[symbol].Add(tradeBar.Close);
                    _movingAverages[symbol].Update(Time, tradeBar.Close);
                }
            }
            
            // Reset daily trading flags at the start of new trading day
            if (currentTime >= new TimeSpan(9, 30, 0) && currentTime <= new TimeSpan(9, 31, 0))
            {
                // Check if this is a new trading day by comparing dates
                if (Time.Date != _lastTradingDate)
                {
                    _hasBoughtToday = false;
                    _hasSoldToday = false;
                    _lastTradingDate = Time.Date;
                    Debug($"New trading day: {Time.Date:yyyy-MM-dd} - Flags reset");
                }
            }
            
            // Execute buy orders at market open (9:30 AM window) - Only on rebalance days
            if (currentTime >= _buyTime && currentTime <= new TimeSpan(9, 35, 0) && !_hasBoughtToday)
            {
                // Check if it's time to rebalance or if we have no positions
                bool needsRebalancing = _daysSinceLastRebalance >= _rebalanceFrequency || !HasAnyPositions();
                
                if (needsRebalancing)
                {
                    ExecuteRebalance();
                    _hasBoughtToday = true;
                    _daysSinceLastRebalance = 0;
                    Debug($"Rebalanced portfolio on day {Time.Date:yyyy-MM-dd}");
                }
                else
                {
                    _hasBoughtToday = true; // Mark as processed for the day
                }
            }
            
            // Remove the daily sell logic - we want to hold positions
            // Only sell during rebalancing or emergency liquidation
        }
        
        private bool HasAnyPositions()
        {
            return _symbols.Any(symbol => Portfolio[symbol].Invested);
        }
        
        private void ExecuteRebalance()
        {
            Debug($"Executing portfolio rebalance at {Time:HH:mm}");
            
            // First, liquidate all current positions
            foreach (var symbol in _symbols)
            {
                if (Portfolio[symbol].Invested)
                {
                    var quantity = Portfolio[symbol].Quantity;
                    Debug($"Liquidating {quantity} shares of {symbol}");
                    MarketOrder(symbol, -quantity);
                }
            }
            
            // Then, buy new positions with equal allocation
            foreach (var symbol in _symbols)
            {
                var targetValue = Portfolio.TotalPortfolioValue * _positionSize;
                var currentPrice = Securities[symbol].Price;
                
                if (currentPrice > 0)
                {
                    var quantity = (int)(targetValue / currentPrice);
                    if (quantity > 0)
                    {
                        Debug($"Buying {quantity} shares of {symbol} at ${currentPrice:F2} (Target: ${targetValue:F2})");
                        MarketOrder(symbol, quantity);
                    }
                }
            }
        }
        
        private void ExecuteDailyBuy()
        {
            Debug($"Executing daily buy orders at {Time:HH:mm}");
            
            foreach (var symbol in _symbols)
            {
                if (!Portfolio[symbol].Invested)
                {
                    var targetValue = Portfolio.TotalPortfolioValue * _positionSize;
                    var currentPrice = Securities[symbol].Price;
                    
                    if (currentPrice > 0)
                    {
                        var quantity = (int)(targetValue / currentPrice);
                        if (quantity > 0)
                        {
                            Debug($"Buying {quantity} shares of {symbol} at ${currentPrice:F2} (Target: ${targetValue:F2})");
                            MarketOrder(symbol, quantity);
                        }
                    }
                }
            }
        }
        
        private void ExecuteDailySell()
        {
            Debug($"Executing daily sell orders at {Time:HH:mm}");
            
            foreach (var symbol in _symbols)
            {
                if (Portfolio[symbol].Invested)
                {
                    var quantity = Portfolio[symbol].Quantity;
                    var currentPrice = Securities[symbol].Price;
                    Debug($"Selling {quantity} shares of {symbol} at ${currentPrice:F2}");
                    MarketOrder(symbol, -quantity);
                }
            }
        }
        
        private void CloseAllPositions()
        {
            Debug("Final cleanup - closing any remaining positions before market close");
            
            foreach (var symbol in _symbols)
            {
                if (Portfolio[symbol].Invested)
                {
                    var quantity = Portfolio[symbol].Quantity;
                    Debug($"Final liquidation: {quantity} shares of {symbol}");
                    MarketOrder(symbol, -quantity);
                }
            }
        }
        
        private bool IsMarketOpen()
        {
            // Check if current time is within market hours (9:30 AM - 4:00 PM ET)
            var currentTime = Time.TimeOfDay;
            var marketOpen = new TimeSpan(9, 30, 0);
            var marketClose = new TimeSpan(16, 0, 0);
            
            return currentTime >= marketOpen && currentTime <= marketClose;
        }
        
        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            if (orderEvent.Status == OrderStatus.Filled)
            {
                Debug($"Order filled: {orderEvent.Symbol} - {orderEvent.Direction} {orderEvent.FillQuantity} shares at ${orderEvent.FillPrice:F2}");
            }
        }
        
        public override void OnEndOfDay(Symbol symbol)
        {
            // Only log portfolio performance once per day for SPY (as a proxy for end of day)
            if (symbol.Value == "SPY")
            {
                _daysSinceLastRebalance++; // Increment days counter
                
                var totalValue = Portfolio.TotalPortfolioValue;
                Debug($"End of day portfolio value: ${totalValue:F2} (Days since rebalance: {_daysSinceLastRebalance})");
                
                foreach (var sym in _symbols)
                {
                    if (Portfolio[sym].Invested)
                    {
                        var holdings = Portfolio[sym];
                        Debug($"{sym}: {holdings.Quantity} shares, Value: ${holdings.HoldingsValue:F2}, P&L: {holdings.UnrealizedProfitPercent:P2}");
                    }
                }
            }
        }
    }
}