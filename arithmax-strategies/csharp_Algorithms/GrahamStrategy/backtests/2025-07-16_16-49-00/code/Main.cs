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
    /// <summary>
    /// Benjamin Graham Value Investing Strategy
    /// Implements Graham's defensive investor criteria with modern enhancements
    /// </summary>
    public class GrahamStrategy : QCAlgorithm
    {
        // Strategy configuration
        private readonly decimal _initialCapital = 100000m;
        private readonly int _maxPortfolioSize = 15;
        private readonly int _minPortfolioSize = 8;
        private readonly decimal _rebalanceThreshold = 0.05m;

        // Portfolio management
        private Dictionary<Symbol, decimal> _targetWeights;
        private Dictionary<Symbol, SecurityData> _securityData;
        private List<Symbol> _universe;
        private DateTime _lastRebalanceDate;
        private int _rebalanceFrequency = 90; // Rebalance every 90 days

        // Risk management
        private readonly decimal _maxSectorConcentration = 0.30m;
        private readonly decimal _maxSinglePosition = 0.12m;
        private readonly decimal _minCash = 0.05m;

        // Performance tracking
        private RollingWindow<decimal> _portfolioValue;
        private decimal _maxDrawdown;
        private decimal _highWaterMark;

        public override void Initialize()
        {
            SetStartDate(2019, 1, 1);
            SetEndDate(2025, 6, 1);
            SetCash(_initialCapital);

            // Set benchmark
            SetBenchmark("SPY");

            // Initialize data structures
            _targetWeights = new Dictionary<Symbol, decimal>();
            _securityData = new Dictionary<Symbol, SecurityData>();
            _universe = new List<Symbol>();
            _portfolioValue = new RollingWindow<decimal>(252);
            _lastRebalanceDate = DateTime.MinValue;
            _highWaterMark = (decimal)_initialCapital;

            // Add universe selection
            AddUniverse(CoarseSelectionFilter, FineSelectionFilter);

            // Set up rebalancing schedule
            Schedule.On(DateRules.MonthStart(), TimeRules.At(10, 0), RebalancePortfolio);

            // Set up risk management
            Schedule.On(DateRules.EveryDay(), TimeRules.At(15, 30), ApplyRiskManagement);

            // Set up performance tracking
            Schedule.On(DateRules.EveryDay(), TimeRules.At(16, 0), UpdatePerformanceMetrics);

            // Set brokerage settings
            SetBrokerageModel(BrokerageName.InteractiveBrokersBrokerage, AccountType.Margin);

            // Set warm-up period
            SetWarmUp(TimeSpan.FromDays(30));

            Log("Graham Strategy initialized successfully");
        }

        /// <summary>
        /// Coarse universe selection - filter by price and volume
        /// </summary>
        private IEnumerable<Symbol> CoarseSelectionFilter(IEnumerable<CoarseFundamental> coarse)
        {
            var coarseCount = coarse.Count();
            Log($"Coarse universe selection: Processing {coarseCount} securities");

            var filtered = coarse
                .Where(x => x.HasFundamentalData && x.Price > 5m && x.DollarVolume > 1000000)
                .OrderByDescending(x => x.DollarVolume)
                .Take(1000)
                .Select(x => x.Symbol);

            var filteredCount = filtered.Count();
            Log($"After coarse filtering: {filteredCount} securities passed (price > $5, volume > $1M, has fundamental data)");

            return filtered;
        }

        /// <summary>
        /// Fine universe selection - apply Graham's criteria
        /// </summary>
        private IEnumerable<Symbol> FineSelectionFilter(IEnumerable<FineFundamental> fine)
        {
            var filtered = new List<Symbol>();
            var fineCount = fine.Count();
            
            Log($"Fine universe selection: Processing {fineCount} securities");

            foreach (var stock in fine)
            {
                if (ApplyGrahamCriteria(stock))
                {
                    filtered.Add(stock.Symbol);

                    // Store security data for later use
                    _securityData[stock.Symbol] = new SecurityData
                    {
                        Symbol = stock.Symbol,
                        PE = (decimal)stock.ValuationRatios.PERatio,
                        PB = (decimal)stock.ValuationRatios.PBRatio,
                        CurrentRatio = (decimal)stock.OperationRatios.CurrentRatio.Value,
                        DebtToEquity = (decimal)stock.OperationRatios.TotalDebtEquityRatio.Value,
                        ROE = (decimal)stock.OperationRatios.ROE.Value,
                        ProfitMargin = (decimal)stock.OperationRatios.NetMargin.Value,
                        MarketCap = stock.MarketCap,
                        Sector = (int)stock.AssetClassification.MorningstarSectorCode,
                        Industry = (int)stock.AssetClassification.MorningstarIndustryCode,
                        Beta = 1.0m, // Default beta value since it's not available
                        Revenue = (decimal)stock.FinancialStatements.IncomeStatement.TotalRevenue.Value,
                        DividendYield = (decimal)stock.ValuationRatios.ForwardDividendYield
                    };
                }
            }

            Log($"After Graham criteria: {filtered.Count} securities passed");

            // Apply diversification and quality scoring
            var selectedSymbols = SelectDiversifiedPortfolio(filtered);
            _universe = selectedSymbols.ToList();

            Log($"Selected {selectedSymbols.Count()} stocks for universe");
            
            // Log selected symbols
            if (selectedSymbols.Any())
            {
                Log($"Selected symbols: {string.Join(", ", selectedSymbols.Take(10))}");
            }
            
            return selectedSymbols;
        }

        /// <summary>
        /// Apply Benjamin Graham's defensive investor criteria
        /// </summary>
        private bool ApplyGrahamCriteria(FineFundamental stock)
        {
            try
            {
                // Size filter - minimum market cap (reduced from 1B to 100M)
                if (stock.MarketCap < 100000000) return false;

                // Earnings stability - positive earnings in recent years
                if (stock.EarningReports.BasicEPS.Value <= 0) return false;

                // Dividend record - relaxed dividend requirement
                // Allow stocks with any dividend yield (including 0)
                // if (stock.ValuationRatios.ForwardDividendYield <= 0) return false;

                // PE ratio - reasonable valuation (relaxed from 20 to 25)
                var pe = stock.ValuationRatios.PERatio;
                if (pe <= 0 || pe > 25) return false;

                // PB ratio - conservative book value (relaxed from 2.5 to 3.5)
                var pb = stock.ValuationRatios.PBRatio;
                if (pb <= 0 || pb > 3.5) return false;

                // Current ratio - strong liquidity (relaxed from 1.5 to 1.2)
                var currentRatio = stock.OperationRatios.CurrentRatio.Value;
                if (currentRatio < 1.2) return false;

                // Debt to equity - conservative leverage (relaxed from 0.5 to 0.8)
                var debtToEquity = stock.OperationRatios.TotalDebtEquityRatio.Value;
                if (debtToEquity > 0.8) return false;

                // Profitability - positive profit margins (relaxed from 5% to 3%)
                var netMargin = stock.OperationRatios.NetMargin.Value;
                if (netMargin < 0.03) return false;

                // ROE - reasonable return on equity (relaxed from 10% to 8%)
                var roe = stock.OperationRatios.ROE.Value;
                if (roe < 0.08) return false;

                // Revenue growth - stable or growing revenue
                var revenue = stock.FinancialStatements.IncomeStatement.TotalRevenue.Value;
                if (revenue <= 0) return false;

                Log($"Stock {stock.Symbol} passed Graham criteria: PE={pe:F2}, PB={pb:F2}, CR={currentRatio:F2}, DE={debtToEquity:F2}, NM={netMargin:F2}, ROE={roe:F2}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error applying Graham criteria for {stock.Symbol}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Select diversified portfolio with quality scoring
        /// </summary>
        private IEnumerable<Symbol> SelectDiversifiedPortfolio(List<Symbol> eligibleSymbols)
        {
            if (eligibleSymbols.Count <= _maxPortfolioSize)
                return eligibleSymbols;

            var scoredSymbols = new List<ScoredSymbol>();

            // Calculate quality scores
            foreach (var symbol in eligibleSymbols)
            {
                if (_securityData.ContainsKey(symbol))
                {
                    var data = _securityData[symbol];
                    var score = CalculateQualityScore(data);
                    scoredSymbols.Add(new ScoredSymbol { Symbol = symbol, Score = score, Data = data });
                }
            }

            // Sort by score and apply diversification
            scoredSymbols = scoredSymbols.OrderByDescending(x => x.Score).ToList();

            return ApplyDiversification(scoredSymbols);
        }

        /// <summary>
        /// Calculate quality score for stock selection
        /// </summary>
        private decimal CalculateQualityScore(SecurityData data)
        {
            decimal score = 0;

            // PE ratio score (lower is better)
            if (data.PE > 0 && data.PE < 15)
                score += (15 - data.PE) / 15 * 0.2m;

            // PB ratio score (lower is better)
            if (data.PB > 0 && data.PB < 2)
                score += (2 - data.PB) / 2 * 0.15m;

            // Current ratio score (higher is better, up to 3)
            if (data.CurrentRatio > 1.5m)
                score += Math.Min(data.CurrentRatio / 3, 1) * 0.15m;

            // ROE score (higher is better)
            if (data.ROE > 0.10m)
                score += Math.Min(data.ROE * 5, 1) * 0.15m;

            // Profit margin score (higher is better)
            if (data.ProfitMargin > 0.05m)
                score += Math.Min(data.ProfitMargin * 10, 1) * 0.15m;

            // Low debt score (lower debt is better)
            if (data.DebtToEquity < 0.3m)
                score += (0.3m - data.DebtToEquity) / 0.3m * 0.1m;

            // Dividend yield score (moderate dividend is good)
            if (data.DividendYield > 0.01m && data.DividendYield < 0.08m)
                score += 0.1m;

            return score;
        }

        /// <summary>
        /// Apply sector diversification rules
        /// </summary>
        private IEnumerable<Symbol> ApplyDiversification(List<ScoredSymbol> scoredSymbols)
        {
            var selected = new List<Symbol>();
            var sectorCounts = new Dictionary<int, int>();

            foreach (var item in scoredSymbols)
            {
                if (selected.Count >= _maxPortfolioSize) break;

                var sector = item.Data.Sector;
                var currentSectorCount = sectorCounts.ContainsKey(sector) ? sectorCounts[sector] : 0;
                var maxPerSector = Math.Max(1, _maxPortfolioSize / 8); // Max 8 sectors

                if (currentSectorCount < maxPerSector)
                {
                    selected.Add(item.Symbol);
                    sectorCounts[sector] = currentSectorCount + 1;
                }
            }

            // Fill remaining slots if needed
            if (selected.Count < _minPortfolioSize)
            {
                foreach (var item in scoredSymbols)
                {
                    if (selected.Count >= _maxPortfolioSize) break;
                    if (!selected.Contains(item.Symbol))
                    {
                        selected.Add(item.Symbol);
                    }
                }
            }

            return selected;
        }

        /// <summary>
        /// Rebalance portfolio based on target weights
        /// </summary>
        private void RebalancePortfolio()
        {
            if (IsWarmingUp) return;

            try
            {
                // Check if enough time has passed since last rebalance
                if ((Time - _lastRebalanceDate).TotalDays < _rebalanceFrequency) return;

                _lastRebalanceDate = Time;

                // Calculate target weights using risk parity approach
                CalculateTargetWeights();

                // Execute rebalancing trades
                ExecuteRebalancingTrades();

                Log($"Portfolio rebalanced on {Time:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                Log($"Error during rebalancing: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate target weights using risk parity approach
        /// </summary>
        private void CalculateTargetWeights()
        {
            _targetWeights.Clear();

            if (_universe.Count == 0) return;

            // Simple equal weight approach (can be enhanced with risk parity)
            var weight = 1.0m / _universe.Count;

            foreach (var symbol in _universe)
            {
                if (Securities.ContainsKey(symbol))
                {
                    _targetWeights[symbol] = Math.Min(weight, _maxSinglePosition);
                }
            }

            // Normalize weights to sum to 1
            var totalWeight = _targetWeights.Values.Sum();
            if (totalWeight > 0)
            {
                var scaleFactor = (1 - _minCash) / totalWeight;
                var keys = _targetWeights.Keys.ToList();
                foreach (var key in keys)
                {
                    _targetWeights[key] *= scaleFactor;
                }
            }
        }

        /// <summary>
        /// Execute rebalancing trades
        /// </summary>
        private void ExecuteRebalancingTrades()
        {
            var totalValue = Portfolio.TotalPortfolioValue;

            foreach (var kvp in _targetWeights)
            {
                var symbol = kvp.Key;
                var targetWeight = kvp.Value;
                var targetValue = totalValue * targetWeight;

                if (Securities.ContainsKey(symbol))
                {
                    var currentValue = Portfolio[symbol].HoldingsValue;
                    var difference = targetValue - currentValue;

                    if (Math.Abs(difference) > totalValue * _rebalanceThreshold)
                    {
                        var currentPrice = Securities[symbol].Price;
                        if (currentPrice > 0)
                        {
                            var targetQuantity = (int)(targetValue / currentPrice);
                            var currentQuantity = Portfolio[symbol].Quantity;
                            var orderQuantity = targetQuantity - currentQuantity;

                            if (Math.Abs(orderQuantity) > 0)
                            {
                                MarketOrder(symbol, orderQuantity);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Risk management checks
        /// </summary>
        private void ApplyRiskManagement()
        {
            if (IsWarmingUp) return;

            // Check portfolio concentration
            CheckPortfolioConcentration();

            // Check drawdown limits
            CheckDrawdownLimits();

            // Check sector concentration
            CheckSectorConcentration();
        }

        /// <summary>
        /// Check portfolio concentration limits
        /// </summary>
        private void CheckPortfolioConcentration()
        {
            var totalValue = Portfolio.TotalPortfolioValue;

            foreach (var holding in Portfolio.Values)
            {
                if (holding.Invested)
                {
                    var weight = holding.HoldingsValue / totalValue;
                    if (weight > _maxSinglePosition)
                    {
                        var targetValue = totalValue * _maxSinglePosition;
                        var excessValue = holding.HoldingsValue - targetValue;
                        var reduceQuantity = (int)(excessValue / holding.Price);

                        if (reduceQuantity > 0)
                        {
                            MarketOrder(holding.Symbol, -reduceQuantity);
                            Log($"Reduced position in {holding.Symbol} due to concentration limits");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check drawdown limits
        /// </summary>
        private void CheckDrawdownLimits()
        {
            var currentValue = Portfolio.TotalPortfolioValue;

            if (currentValue > _highWaterMark)
            {
                _highWaterMark = currentValue;
            }

            var drawdown = (_highWaterMark - currentValue) / _highWaterMark;

            if (drawdown > _maxDrawdown)
            {
                _maxDrawdown = drawdown;
            }

            // Emergency sell if drawdown exceeds 20%
            if (drawdown > 0.20m)
            {
                Liquidate();
                Log($"Emergency liquidation triggered due to {drawdown:P2} drawdown");
            }
        }

        /// <summary>
        /// Check sector concentration
        /// </summary>
        private void CheckSectorConcentration()
        {
            var sectorExposure = new Dictionary<int, decimal>();
            var totalValue = Portfolio.TotalPortfolioValue;

            foreach (var holding in Portfolio.Values)
            {
                if (holding.Invested && _securityData.ContainsKey(holding.Symbol))
                {
                    var sector = _securityData[holding.Symbol].Sector;
                    var exposure = holding.HoldingsValue / totalValue;

                    if (!sectorExposure.ContainsKey(sector))
                        sectorExposure[sector] = 0;

                    sectorExposure[sector] += exposure;
                }
            }

            // Check for sector concentration violations
            foreach (var kvp in sectorExposure)
            {
                if (kvp.Value > _maxSectorConcentration)
                {
                    Log($"Sector concentration warning: Sector {kvp.Key} exposure is {kvp.Value:P2}");
                }
            }
        }

        /// <summary>
        /// Update performance metrics
        /// </summary>
        private void UpdatePerformanceMetrics()
        {
            if (IsWarmingUp) return;

            var currentValue = Portfolio.TotalPortfolioValue;
            _portfolioValue.Add(currentValue);

            // Log performance periodically
            if (Time.Day == 1) // Monthly logging
            {
                var totalReturn = (currentValue - _initialCapital) / _initialCapital;
                Log($"Monthly Performance Update - Total Return: {totalReturn:P2}, Portfolio Value: ${currentValue:N0}");
            }
        }

        /// <summary>
        /// Handle data events
        /// </summary>
        public override void OnData(Slice data)
        {
            // Data handling is primarily done through scheduled events
            // This method can be used for additional data processing if needed
        }

        /// <summary>
        /// Handle order events
        /// </summary>
        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            if (orderEvent.Status == OrderStatus.Filled)
            {
                Log($"Order filled: {orderEvent.Symbol} - {orderEvent.Direction} {orderEvent.FillQuantity} @ ${orderEvent.FillPrice}");
            }
            else if (orderEvent.Status == OrderStatus.Canceled)
            {
                Log($"Order canceled: {orderEvent.Symbol} - {orderEvent.Message}");
            }
        }

        /// <summary>
        /// Handle end of algorithm
        /// </summary>
        public override void OnEndOfAlgorithm()
        {
            var finalValue = Portfolio.TotalPortfolioValue;
            var totalReturn = (finalValue - _initialCapital) / _initialCapital;

            Log($"=== FINAL PERFORMANCE SUMMARY ===");
            Log($"Initial Capital: ${_initialCapital:N0}");
            Log($"Final Portfolio Value: ${finalValue:N0}");
            Log($"Total Return: {totalReturn:P2}");
            Log($"Max Drawdown: {_maxDrawdown:P2}");
            Log($"Number of Holdings: {Portfolio.Values.Count(h => h.Invested)}");

            // Log top holdings
            var topHoldings = Portfolio.Values
                .Where(h => h.Invested)
                .OrderByDescending(h => h.HoldingsValue)
                .Take(10);

            Log("Top 10 Holdings:");
            foreach (var holding in topHoldings)
            {
                var weight = holding.HoldingsValue / finalValue;
                Log($"  {holding.Symbol}: ${holding.HoldingsValue:N0} ({weight:P2})");
            }
        }
    }

    /// <summary>
    /// Helper class to store security fundamental data
    /// </summary>
    public class SecurityData
    {
        public Symbol Symbol { get; set; }
        public decimal PE { get; set; }
        public decimal PB { get; set; }
        public decimal CurrentRatio { get; set; }
        public decimal DebtToEquity { get; set; }
        public decimal ROE { get; set; }
        public decimal ProfitMargin { get; set; }
        public decimal MarketCap { get; set; }
        public int Sector { get; set; }
        public int Industry { get; set; }
        public decimal Beta { get; set; }
        public decimal Revenue { get; set; }
        public decimal DividendYield { get; set; }
    }

    /// <summary>
    /// Helper class for scoring symbols
    /// </summary>
    public class ScoredSymbol
    {
        public Symbol Symbol { get; set; }
        public decimal Score { get; set; }
        public SecurityData Data { get; set; }
    }
}

