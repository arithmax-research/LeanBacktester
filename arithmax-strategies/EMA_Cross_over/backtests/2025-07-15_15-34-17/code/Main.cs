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
    public class EMACrossover : QCAlgorithm
    {
        // Dictionary to store EMA indicators for each symbol
        private Dictionary<Symbol, ExponentialMovingAverage> _emaIndicators;
        
        // Dictionary to store Simple Moving Averages for reference
        private Dictionary<Symbol, SimpleMovingAverage> _shortSma;
        private Dictionary<Symbol, SimpleMovingAverage> _longSma;
        
        // Dictionary to store previous trading positions
        private Dictionary<Symbol, int> _previousPositions;
        
        // List of symbols to trade
        private List<Symbol> _symbols;
        
        // EMA period (equivalent to span=20 in Python)
        private int _emaPeriod = 20;
        
        // SMA periods for reference (20 and 100 day windows)
        private int _shortSmaPeriod = 20;
        private int _longSmaPeriod = 100;

        public override void Initialize()
        {
            // Set the algorithm dates - you can modify these as needed
            SetStartDate(2020, 1, 1);
            SetEndDate(2023, 12, 31);
            SetCash(100000);
            
            // Initialize dictionaries
            _emaIndicators = new Dictionary<Symbol, ExponentialMovingAverage>();
            _shortSma = new Dictionary<Symbol, SimpleMovingAverage>();
            _longSma = new Dictionary<Symbol, SimpleMovingAverage>();
            _previousPositions = new Dictionary<Symbol, int>();
            _symbols = new List<Symbol>();
            
            // Add multiple equities (you can modify this list)
            var tickers = new[] { "SPY", "NVDA", "MSFT", "GOOGL" };
            
            foreach (var ticker in tickers)
            {
                var symbol = AddEquity(ticker, Resolution.Daily).Symbol;
                _symbols.Add(symbol);
                
                // Create EMA indicator with 20 periods
                _emaIndicators[symbol] = new ExponentialMovingAverage(_emaPeriod);
                
                // Create SMA indicators for reference
                _shortSma[symbol] = new SimpleMovingAverage(_shortSmaPeriod);
                _longSma[symbol] = new SimpleMovingAverage(_longSmaPeriod);
                
                // Initialize previous positions
                _previousPositions[symbol] = 0;
                
                // Register the indicators to receive data
                RegisterIndicator(symbol, _emaIndicators[symbol], Resolution.Daily);
                RegisterIndicator(symbol, _shortSma[symbol], Resolution.Daily);
                RegisterIndicator(symbol, _longSma[symbol], Resolution.Daily);
            }
            
            // Equal weighting across all symbols
            SetWarmUp(_longSmaPeriod);
        }

        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// Slice object keyed by symbol containing the stock data
        public override void OnData(Slice data)
        {
            // Don't trade during warm-up period
            if (IsWarmingUp) return;
            
            foreach (var symbol in _symbols)
            {
                // Check if we have data for this symbol and it's not null
                if (!data.ContainsKey(symbol) || data[symbol] == null) continue;
                
                var barData = data[symbol];
                if (barData == null) continue;
                
                var price = barData.Close;
                var ema = _emaIndicators[symbol];
                
                // Ensure EMA is ready
                if (!ema.IsReady) continue;
                
                // Calculate trading signal based on price vs EMA
                // Positive signal when price > EMA, negative when price < EMA
                var signal = price > ema.Current.Value ? 1 : (price < ema.Current.Value ? -1 : 0);
                
                // Apply equal weighting (divide by number of symbols)
                var targetWeight = (decimal)signal / _symbols.Count / 3; // Divide by 3 as in original Python code
                
                // Get current holdings - avoid division by zero
                var totalValue = Portfolio.TotalPortfolioValue;
                var currentWeight = totalValue > 0 ? Portfolio[symbol].HoldingsValue / totalValue : 0;
                
                // Only trade if there's a significant change in position
                if (Math.Abs(targetWeight - currentWeight) > 0.01m) // 1% threshold
                {
                    SetHoldings(symbol, targetWeight);
                    
                    // Log the trade
                    Log($"Rebalancing {symbol}: Current Weight: {currentWeight:P2}, Target Weight: {targetWeight:P2}, Price: {price:C2}, EMA: {ema.Current.Value:C2}");
                }
            }
        }
        
        /// OnEndOfAlgorithm event called when the algorithm ends
        public override void OnEndOfAlgorithm()
        {
            // Calculate final statistics
            var startingCash = 100000m;
            var endingValue = Portfolio.TotalPortfolioValue;
            var totalReturn = (endingValue - startingCash) / startingCash;
            
            // Calculate the number of years
            var years = (EndDate - StartDate).TotalDays / 365.25;
            var annualizedReturn = Math.Pow((double)(1 + totalReturn), 1.0 / years) - 1;
            
            Log($"=== FINAL PORTFOLIO STATISTICS ===");
            Log($"Starting Cash: ${startingCash:N2}");
            Log($"Ending Value: ${endingValue:N2}");
            Log($"Total Return: {totalReturn:P2}");
            Log($"Annualized Return: {annualizedReturn:P2}");
            Log($"Years: {years:F2}");
            
            // Log individual symbol performance
            foreach (var symbol in _symbols)
            {
                var holding = Portfolio[symbol];
                if (holding.Invested)
                {
                    Log($"{symbol}: Holdings Value: ${holding.HoldingsValue:N2}, " +
                        $"Quantity: {holding.Quantity}, " +
                        $"Unrealized P&L: ${holding.UnrealizedProfit:N2}");
                }
            }
            
            // Plot final EMA values for each symbol
            foreach (var symbol in _symbols)
            {
                if (_emaIndicators[symbol].IsReady)
                {
                    var price = Securities[symbol].Close;
                    var ema = _emaIndicators[symbol].Current.Value;
                    Plot($"EMA_{symbol}", "Final Price", price);
                    Plot($"EMA_{symbol}", "Final EMA", ema);
                }
            }
        }
    }
}
