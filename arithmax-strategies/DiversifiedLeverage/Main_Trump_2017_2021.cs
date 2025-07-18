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
    public class DiversifiedLeverageTrump2017 : QCAlgorithm
    {
        // Portfolio configuration
        private Dictionary<string, decimal> _targetWeights;
        private List<string> _symbols;
        private int _rebalancePeriod = 4; // Rebalance every 4 days
        private Dictionary<string, OrderTicket> _orderDict;
        private DateTime _lastRebalanceTime;
        private Dictionary<string, decimal> _individualReturns;
        private Dictionary<string, decimal> _initialPrices;

        public override void Initialize()
        {
            // Trump's First Term: January 20, 2017 - January 20, 2021
            SetStartDate(2017, 01, 20); // Trump inauguration date
            SetEndDate(2021, 01, 20); // End of Trump's first term
            SetCash(100000);             //Set Strategy Cash

            // Initialize tracking dictionaries
            _individualReturns = new Dictionary<string, decimal>();
            _initialPrices = new Dictionary<string, decimal>();

            // Initialize portfolio weights and symbols
            _targetWeights = new Dictionary<string, decimal>
            {
                {"TQQQ", 0.20m},  // 3x Leveraged Nasdaq
                {"UPRO", 0.20m},  // 3x Leveraged S&P 500
                {"UDOW", 0.10m},  // 3x Leveraged Dow Jones
                {"TMF", 0.25m},   // 3x Leveraged Treasury Bonds
                {"UGL", 0.10m},   // 3x Leveraged Gold
                {"DIG", 0.15m},   // 2x Leveraged Oil and Gas Companies
            };

            _symbols = new List<string>(_targetWeights.Keys);
            _orderDict = new Dictionary<string, OrderTicket>();

            // Add securities to the algorithm
            foreach (var symbol in _symbols)
            {
                AddEquity(symbol, Resolution.Daily);
                _individualReturns[symbol] = 0m;
            }

            // Schedule rebalancing
            Schedule.On(DateRules.Every(DayOfWeek.Monday), TimeRules.At(9, 31), RebalancePortfolio);
            
            // Log initial portfolio value
            Log($"=== TRUMP 2017-2021 PERIOD ANALYSIS ===");
            Log($"Initial Portfolio Value: ${Portfolio.TotalPortfolioValue:F2}");
            Log($"Target Portfolio Weights: {string.Join(", ", _targetWeights.Select(kvp => $"{kvp.Key}: {kvp.Value:P2}"))}");

            _lastRebalanceTime = DateTime.MinValue;
        }

        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// Slice object keyed by symbol containing the stock data
        public override void OnData(Slice data)
        {
            // Store initial prices for individual ETF tracking
            foreach (var symbol in _symbols)
            {
                if (!_initialPrices.ContainsKey(symbol) && data.ContainsKey(symbol) && data[symbol] != null)
                {
                    _initialPrices[symbol] = data[symbol].Price;
                    Log($"Initial price for {symbol}: ${_initialPrices[symbol]:F2}");
                }
            }

            // Check if it's time to rebalance (every 4 days)
            if (Time.Subtract(_lastRebalanceTime).TotalDays >= _rebalancePeriod)
            {
                RebalancePortfolio();
            }
        }

        private void RebalancePortfolio()
        {
            // Skip if we have pending orders
            if (_orderDict.Values.Any(order => order.Status == OrderStatus.Submitted || order.Status == OrderStatus.PartiallyFilled))
            {
                Log("Skipping rebalancing - pending orders exist");
                return;
            }

            Log("Rebalancing portfolio...");
            
            // Get current portfolio value
            var portfolioValue = Portfolio.TotalPortfolioValue;
            Log($"Current Portfolio Value: ${portfolioValue:F2}");

            // Clear completed orders
            _orderDict.Clear();

            // Calculate desired position values and create orders
            foreach (var kvp in _targetWeights)
            {
                var symbol = kvp.Key;
                var weight = kvp.Value;

                if (!Securities.ContainsKey(symbol))
                {
                    Log($"Warning: {symbol} not in securities, skipping");
                    continue;
                }

                var security = Securities[symbol];
                if (!security.HasData)
                {
                    Log($"Warning: {symbol} has no data, skipping");
                    continue;
                }

                var currentPrice = security.Price;
                if (currentPrice <= 0)
                {
                    Log($"Warning: {symbol} has invalid price {currentPrice}, skipping");
                    continue;
                }

                // Calculate target position value and shares
                var targetValue = portfolioValue * weight;
                var targetShares = (int)(targetValue / currentPrice);

                // Get current position
                var currentShares = Portfolio[symbol].Quantity;

                // Calculate difference
                var sharesDifference = targetShares - currentShares;

                // Skip if the difference is very small
                if (Math.Abs(sharesDifference) < 1)
                {
                    continue;
                }

                // Create the order
                OrderTicket order = null;
                if (sharesDifference > 0)
                {
                    Log($"Buying {sharesDifference} shares of {symbol} at ${currentPrice:F2}");
                    order = MarketOrder(symbol, sharesDifference);
                }
                else if (sharesDifference < 0)
                {
                    Log($"Selling {Math.Abs(sharesDifference)} shares of {symbol} at ${currentPrice:F2}");
                    order = MarketOrder(symbol, sharesDifference);
                }

                if (order != null)
                {
                    _orderDict[symbol] = order;
                }
            }

            _lastRebalanceTime = Time;
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            var order = Transactions.GetOrderById(orderEvent.OrderId);
            
            if (orderEvent.Status == OrderStatus.Filled)
            {
                if (order.Direction == OrderDirection.Buy)
                {
                    Log($"BUY EXECUTED - {orderEvent.Symbol}, Price: ${orderEvent.FillPrice:F2}, Quantity: {orderEvent.FillQuantity}");
                }
                else
                {
                    Log($"SELL EXECUTED - {orderEvent.Symbol}, Price: ${orderEvent.FillPrice:F2}, Quantity: {orderEvent.FillQuantity}");
                }
            }
            else if (orderEvent.Status == OrderStatus.Canceled || orderEvent.Status == OrderStatus.Invalid)
            {
                Log($"Order {orderEvent.Symbol} Failed with Status: {orderEvent.Status}");
            }
        }

        public override void OnEndOfAlgorithm()
        {
            Log($"=== TRUMP 2017-2021 PERIOD FINAL RESULTS ===");
            Log($"Final Portfolio Value: ${Portfolio.TotalPortfolioValue:F2}");
            var totalReturn = (Portfolio.TotalPortfolioValue - 100000) / 100000 * 100;
            Log($"Total Portfolio Return: {totalReturn:F2}%");
            
            // Calculate individual ETF returns
            Log($"=== INDIVIDUAL ETF PERFORMANCE (Trump 2017-2021) ===");
            foreach (var symbol in _symbols)
            {
                if (_initialPrices.ContainsKey(symbol) && Securities[symbol].HasData)
                {
                    var initialPrice = _initialPrices[symbol];
                    var finalPrice = Securities[symbol].Price;
                    var etfReturn = (finalPrice - initialPrice) / initialPrice * 100;
                    _individualReturns[symbol] = etfReturn;
                    Log($"{symbol}: {initialPrice:F2} -> {finalPrice:F2} = {etfReturn:F2}% return");
                }
            }

            // Log final holdings
            Log($"=== FINAL PORTFOLIO COMPOSITION ===");
            foreach (var kvp in Portfolio)
            {
                if (kvp.Value.Invested)
                {
                    var holding = kvp.Value;
                    Log($"{kvp.Key}: {holding.Quantity} shares, Value: ${holding.HoldingsValue:F2}, Weight: {holding.HoldingsValue / Portfolio.TotalPortfolioValue:P2}");
                }
            }

            // Find best and worst performers
            var bestPerformer = _individualReturns.OrderByDescending(x => x.Value).FirstOrDefault();
            var worstPerformer = _individualReturns.OrderBy(x => x.Value).FirstOrDefault();
            
            Log($"=== TRUMP 2017-2021 PERIOD SUMMARY ===");
            Log($"Best Performer: {bestPerformer.Key} with {bestPerformer.Value:F2}% return");
            Log($"Worst Performer: {worstPerformer.Key} with {worstPerformer.Value:F2}% return");
        }
    }
}
