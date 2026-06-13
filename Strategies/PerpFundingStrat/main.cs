using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuantConnect;
using QuantConnect.Orders;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using QuantConnect.Securities;
using QuantConnect.Securities.Crypto;
using QuantConnect.Brokerages;

namespace QuantConnect.Algorithm.CSharp
{
    public class BinanceFundingRate : BaseData
    {
        public decimal FundingRate { get; set; }
        public decimal MarkPrice { get; set; }

        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            var symbol = config.Symbol.Value.ToLowerInvariant();
            var source = Path.Combine(Globals.DataFolder, "crypto", "binance", "funding", $"{symbol}_funding.csv");
            return new SubscriptionDataSource(source, SubscriptionTransportMedium.LocalFile, FileFormat.Csv);
        }

        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            var csv = line.Split(',');
            if (csv.Length < 3) return null;
            try
            {
                var fundingRate = decimal.Parse(csv[2], NumberStyles.Any, CultureInfo.InvariantCulture);
                var time = DateTime.Parse(csv[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                return new BinanceFundingRate
                {
                    Symbol = config.Symbol,
                    Time = time,
                    EndTime = time + TimeSpan.FromHours(8),
                    FundingRate = fundingRate,
                    MarkPrice = csv.Length > 3 && !string.IsNullOrEmpty(csv[3]) ? decimal.Parse(csv[3], NumberStyles.Any, CultureInfo.InvariantCulture) : 0m,
                    Value = fundingRate
                };
            }
            catch { return null; }
        }
    }

    public class MacroRate : BaseData
    {
        public decimal RateValue { get; set; }
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            var source = Path.Combine(Globals.DataFolder, "crypto", "binance", "rates", $"{config.Symbol.Value.ToLowerInvariant()}.csv");
            return new SubscriptionDataSource(source, SubscriptionTransportMedium.LocalFile, FileFormat.Csv);
        }
        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            var csv = line.Split(',');
            if (csv.Length < 2) return null;
            try
            {
                var rate = decimal.Parse(csv[1], NumberStyles.Any, CultureInfo.InvariantCulture);
                var time = DateTime.Parse(csv[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                return new MacroRate { Symbol = config.Symbol, Time = time, EndTime = time + TimeSpan.FromDays(1), RateValue = rate, Value = rate };
            }
            catch { return null; }
        }
    }

    public class PerpetualFuturesFundingArbitrage : QCAlgorithm
    {
        private readonly decimal _delta = 0.0005m;
        private readonly decimal _spotFee = 0.00009m;        // Binance spot fee: 0.09% (9 bps)
        private readonly decimal _perpFee = 0.0004m;         // Binance perp fee: 0.04% (4 bps)
        private readonly double _fundingIntervalHours = 8.0;        private readonly decimal _stopLossBps = 0.0050m;     // 50 bps stop loss per leg
        private readonly decimal _exitBufferBps = 0.0001m;   // Hysteresis buffer around zero to avoid whipsaw exits
        // IMPROVED: Composite position we need to protect, not individual legs
        private readonly decimal _compositeStopLossPct = 0.0080m; // 80bps max loss on total notional
        // IMPROVED: Partial exit when premium narrows to capture gains
        private readonly decimal _partialExitPct = 0.50m;     // Exit 50% at 50% of target gain
        // IMPROVED: Trailing stop for profit protection — only activates AFTER premium has improved
        private readonly decimal _trailingStopBps = 0.0030m;  // 30bps trailing from best premium seen
        private readonly decimal _positionFraction = 0.10m;   // Reduced from 25% to 10% per asset
        private readonly decimal _maxPortfolioRisk = 0.25m;    // Max 25% total capital in any direction
        private readonly decimal _minVolumeUsd = 5_000_000m;
        // IMPROVED: Pair-specific minimum volumes based on typical liquidity
        private readonly Dictionary<string, decimal> _minVolumeBySymbol = new Dictionary<string, decimal>
        {
            { "BTC", 10_000_000m },
            { "ETH", 5_000_000m },
            { "BNB", 1_000_000m },   // BNB spot volume averages $3-5M, $1M captures most hours
            { "SOL", 800_000m },
            { "ADA", 500_000m },
            { "XRP", 500_000m }
        };
        private readonly decimal _minEntryPremium = 0.0020m;  // Minimum 20 bps premium to enter (lowered from 50bps to get more trades)
        private readonly decimal _maxEntryPremium = 0.0500m; // Max premium to avoid extreme entries (500 bps)
        private readonly double _entryCooldownHours = 12.0;
        private readonly decimal _minFundingEdge = 0.0001m;   // Require a meaningful funding edge to support the trade direction
        private readonly int _entryConfirmationBars = 5;
        // Unused - kept for documentation
        private readonly int _fundingConfirmationBars = 3;
        private readonly Dictionary<string, double> _maxHoldHoursBySymbol = new Dictionary<string, double>
        {
            { "BTC", 48.0 },
            { "BNB", 36.0 },
            { "SOL", 24.0 },
            { "ETH", 48.0 },
            { "ADA", 18.0 },
            { "XRP", 12.0 }
        };
        private readonly Dictionary<string, decimal> _exitBufferBySymbol = new Dictionary<string, decimal>
        {
            { "BTC", 0.00010m },
            { "BNB", 0.00012m },
            { "SOL", 0.00015m },
            { "ETH", 0.00010m },
            { "ADA", 0.00018m },
            { "XRP", 0.00020m }
        };

        /// <summary>
        /// SUGGESTION 3: Pair-specific hold times, cooldowns, and exit buffers.
        /// XRP has tighter cooldown to reduce turnover / fee churn; BTC/ETH get longer cooldown.
        /// </summary>
        private readonly Dictionary<string, double> _cooldownHoursBySymbol = new Dictionary<string, double>
        {
            { "BTC", 24.0 },
            { "ETH", 24.0 },
            { "BNB", 18.0 },
            { "SOL", 16.0 },
            { "ADA", 14.0 },
            { "XRP", 10.0 }   // shorter cooldown because hold time is already short — but exit buffer is wider
        };

        /// <summary>
        /// SUGGESTION 1: Premium persistence — require the premium to be not just above threshold,
        /// but also show relatively low variance across the confirmation window.
        /// This filters out noisy spikes that revert before you can execute.
        /// Using looser values since arb premiums are typically wide during volatile events.
        /// </summary>
        private readonly Dictionary<string, decimal> _maxPremiumStdDevBySymbol = new Dictionary<string, decimal>
        {
            { "BTC", 0.0015m },  // 15 bps max std dev for BTC (was 3 bps — too tight)
            { "ETH", 0.0018m },
            { "BNB", 0.0020m },
            { "SOL", 0.0025m },
            { "ADA", 0.0030m },
            { "XRP", 0.0035m }
        };

        /// <summary>
        /// SUGGESTION 2: Funding regime filter — require funding to be consistently
        /// favorable over a window, not just briefly skewed.
        /// Using 5 bars instead of 8, and requiring majority (not all) bars in same direction.
        /// Also lowered the edge threshold since funding rates are usually small.
        /// </summary>
        private readonly int _fundingRegimeBars = 5;
        private readonly int _fundingRegimeMinConsensus = 4; // At least 4 out of 5 bars must agree

        // IMPROVED: Use MarketOrder for entries (limit orders at 2bps were never filling)
        // Kept as comments for reference if we want to revisit limit orders later

        private readonly List<string> _defaultSymbols = new List<string> { "BTC", "BNB", "SOL" };
        private List<string> _symbols = new List<string>();
        private Dictionary<string, Symbol> _spotSymbols = new Dictionary<string, Symbol>();
        private Dictionary<string, Symbol> _perpSymbols = new Dictionary<string, Symbol>();
        private Dictionary<string, Symbol> _fundingSymbols = new Dictionary<string, Symbol>();
        private Dictionary<string, PerpArbPosition> _positions = new Dictionary<string, PerpArbPosition>();
        private Dictionary<string, decimal> _latestFundingRates = new Dictionary<string, decimal>();
        private Dictionary<string, RollingWindow<decimal>> _fundingWindows = new Dictionary<string, RollingWindow<decimal>>();
        private Dictionary<string, RollingWindow<TradeBar>> _volumeWindows = new Dictionary<string, RollingWindow<TradeBar>>();
        private Dictionary<string, RollingWindow<decimal>> _premiumWindows = new Dictionary<string, RollingWindow<decimal>>();
        private Dictionary<string, DateTime> _lastExitTimes = new Dictionary<string, DateTime>();
        // IMPROVED: Track best premium seen for trailing stops (only after profit seen)
        private Dictionary<string, decimal> _bestPremiums = new Dictionary<string, decimal>();
        // FIX: Track whether premium has ever shown a profit (enables trailing stop)
        private Dictionary<string, bool> _profitSeen = new Dictionary<string, bool>();
        // IMPROVED: Track partial exit status
        private Dictionary<string, bool> _partialExitDone = new Dictionary<string, bool>();

        private Symbol _riskFreeRateSymbol;
        private Symbol _borrowRateSymbol;
        private decimal _riskFreeRate = 0.05m;
        private decimal _borrowRate = 0.08m;

        public override void Initialize()
        {
            _symbols = GetSymbolsFromParameter();

            SetStartDate(2022, 1, 1);
            SetEndDate(2024, 1, 1);
            SetCash(100_000_000);
            SetBrokerageModel(BrokerageName.Binance, AccountType.Margin);

            foreach (var ticker in _symbols)
            {
                _spotSymbols[ticker] = AddCrypto($"{ticker}USDC", Resolution.Hour, Market.Binance).Symbol;
                _perpSymbols[ticker] = AddCrypto($"{ticker}USDT", Resolution.Hour, Market.Binance).Symbol;
                _fundingWindows[ticker] = new RollingWindow<decimal>(_fundingRegimeBars);
                _volumeWindows[ticker] = new RollingWindow<TradeBar>(24);
                _premiumWindows[ticker] = new RollingWindow<decimal>(_entryConfirmationBars);
                Consolidate(_perpSymbols[ticker], Resolution.Hour, (TradeBar bar) => _volumeWindows[ticker].Add(bar));
                _fundingSymbols[ticker] = AddData<BinanceFundingRate>($"{ticker}USDT", Resolution.Hour).Symbol;
            }

            _riskFreeRateSymbol = AddData<MacroRate>("RISKFREE", Resolution.Daily).Symbol;
            _borrowRateSymbol = AddData<MacroRate>("BORROW", Resolution.Daily).Symbol;

            SetWarmUp(TimeSpan.FromHours(24));
            Schedule.On(DateRules.EveryDay(), TimeRules.Every(TimeSpan.FromHours(1)), Rebalance);
        }

        private List<string> GetSymbolsFromParameter()
        {
            var parameter = GetParameter("symbols");
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return new List<string>(_defaultSymbols);
            }

            return parameter
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(symbol => symbol.Trim().ToUpperInvariant())
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct()
                .ToList();
        }

        public override void OnData(Slice slice)
        {
            if (IsWarmingUp) return;

            foreach (var ticker in _symbols)
            {
                if (slice.ContainsKey(_fundingSymbols[ticker]))
                {
                    var fundingRate = ((BinanceFundingRate)slice[_fundingSymbols[ticker]]).FundingRate;
                    _latestFundingRates[ticker] = fundingRate;
                    _fundingWindows[ticker].Add(fundingRate);
                }
            }
            if (slice.ContainsKey(_riskFreeRateSymbol)) _riskFreeRate = ((MacroRate)slice[_riskFreeRateSymbol]).RateValue;
            if (slice.ContainsKey(_borrowRateSymbol)) _borrowRate = ((MacroRate)slice[_borrowRateSymbol]).RateValue;
        }

        private (decimal upper, decimal lower) CalculateBounds(string ticker)
        {
            decimal tc = _spotFee + _perpFee;  // 13 bps total transaction costs
            decimal fundingAbs = _latestFundingRates.ContainsKey(ticker) ? Math.Abs(_latestFundingRates[ticker]) : _delta;
            decimal safetyMargin = 0.0001m;  // 1 bp safety margin on top
            return (fundingAbs + tc + safetyMargin, -fundingAbs - tc - safetyMargin);
        }

        private void Rebalance()
        {
            if (IsWarmingUp) return;
            foreach (var ticker in _symbols) ProcessAsset(ticker);
        }

        private void ProcessAsset(string ticker)
        {
            if (!Securities[_spotSymbols[ticker]].HasData || !Securities[_perpSymbols[ticker]].HasData)
            {
                return;
            }

            if (!_volumeWindows[ticker].IsReady)
            {
                return;
            }

            // IMPROVED: Use pair-specific volume threshold
            var averageDollarVolume = _volumeWindows[ticker].Average(bar => bar.Close * bar.Volume);
            var minVol = _minVolumeBySymbol.ContainsKey(ticker) ? _minVolumeBySymbol[ticker] : _minVolumeUsd;
            if (averageDollarVolume < minVol)
            {
                return;
            }

            // SUGGESTION 3: Use pair-specific cooldown
            var cooldownHours = _cooldownHoursBySymbol.ContainsKey(ticker) ? _cooldownHoursBySymbol[ticker] : _entryCooldownHours;
            if (_lastExitTimes.TryGetValue(ticker, out var lastExitTime) &&
                (UtcTime - lastExitTime).TotalHours < cooldownHours)
            {
                return;
            }
            
            decimal premium = (Securities[_perpSymbols[ticker]].Price / Securities[_spotSymbols[ticker]].Price) - 1m;
            _premiumWindows[ticker].Add(premium);
            var (upperBound, lowerBound) = CalculateBounds(ticker);
            decimal fundingRate = _latestFundingRates.ContainsKey(ticker) ? _latestFundingRates[ticker] : 0m;

            if (premium > _maxEntryPremium || premium < -_maxEntryPremium) return;

            if (!_positions.ContainsKey(ticker))
            {
                if (IsShortPerpLongSpotEntryConfirmed(ticker, premium, upperBound, fundingRate))
                    EnterShortPerpLongSpot(ticker, premium, upperBound);
                else if (IsLongPerpShortSpotEntryConfirmed(ticker, premium, lowerBound, fundingRate))
                    EnterLongPerpShortSpot(ticker, premium, lowerBound);
            }
            else ManagePosition(ticker, premium, upperBound, lowerBound);
        }

        /// <summary>
        /// SUGGESTION 1: Premium persistence check.
        /// In addition to level checks, we verify that premium values across the confirmation
        /// window are tightly clustered (low standard deviation). This filters out volatile spikes.
        /// </summary>
        private bool IsPremiumPersistent(string ticker, decimal threshold, bool isAboveThreshold)
        {
            if (!_premiumWindows[ticker].IsReady) return false;

            var values = _premiumWindows[ticker].ToList();
            if (values.Count < _entryConfirmationBars) return false;

            // All values must be beyond the threshold
            if (isAboveThreshold)
            {
                if (!values.All(v => v >= threshold)) return false;
            }
            else
            {
                if (!values.All(v => v <= threshold)) return false;
            }

            // Check variance: compute standard deviation of premium values
            decimal mean = values.Average();
            decimal variance = values.Select(v => (v - mean) * (v - mean)).Sum() / values.Count;
            decimal stdDev = (decimal)Math.Sqrt((double)variance);

            // Max allowed std dev for this symbol
            var maxStdDev = _maxPremiumStdDevBySymbol.ContainsKey(ticker) ? _maxPremiumStdDevBySymbol[ticker] : 0.0005m;

            return stdDev <= maxStdDev;
        }

        /// <summary>
        /// SUGGESTION 2: Funding regime filter using consensus approach.
        /// Uses a window of bars and requires a super-majority (not unanimity) to agree.
        /// This avoids the problem of a single neutral funding bar blocking entry.
        /// </summary>
        private bool IsFundingRegimeConfirmed(string ticker, bool favorShortPerp)
        {
            if (!_fundingWindows[ticker].IsReady) return false;

            var allValues = _fundingWindows[ticker].ToList();
            if (allValues.Count < _fundingRegimeBars) return false;

            int agreeing = 0;
            if (favorShortPerp)
            {
                // Count how many bars have positive funding
                agreeing = allValues.Count(v => v > 0);
                // Require super-majority AND strong average
                if (agreeing < _fundingRegimeMinConsensus) return false;
                return allValues.Average() >= _minFundingEdge * 2;
            }

            // Favor long perp — count negative bars
            agreeing = allValues.Count(v => v < 0);
            if (agreeing < _fundingRegimeMinConsensus) return false;
            return allValues.Average() <= -_minFundingEdge * 2;
        }

        private bool IsShortPerpLongSpotEntryConfirmed(string ticker, decimal premium, decimal upperBound, decimal fundingRate)
        {
            if (premium < Math.Max(upperBound, _minEntryPremium)) return false;
            if (!IsFundingRegimeConfirmed(ticker, true)) return false;
            if (!_premiumWindows[ticker].IsReady) return false;

            // IMPROVED: Premium persistence check is sufficient; removed redundant 5% above bound
            if (!IsPremiumPersistent(ticker, upperBound, true)) return false;

            Log($"ENTRY SIGNAL: {ticker} SHORT PERP / LONG SPOT | Premium: {premium:P4} | UpperBound: {upperBound:P4} | Funding: {fundingRate:P4}");

            return true;
        }

        private bool IsLongPerpShortSpotEntryConfirmed(string ticker, decimal premium, decimal lowerBound, decimal fundingRate)
        {
            if (premium > Math.Min(lowerBound, -_minEntryPremium)) return false;
            if (!IsFundingRegimeConfirmed(ticker, false)) return false;
            if (!_premiumWindows[ticker].IsReady) return false;

            // IMPROVED: Premium persistence check is sufficient; removed redundant 5% below bound
            if (!IsPremiumPersistent(ticker, lowerBound, false)) return false;

            Log($"ENTRY SIGNAL: {ticker} LONG PERP / SHORT SPOT | Premium: {premium:P4} | LowerBound: {lowerBound:P4} | Funding: {fundingRate:P4}");

            return true;
        }

        /// <summary>
        /// IMPROVED: Use MarketOrder instead of LimitOrder for entries.
        /// Limit orders at tight offsets were never filling in backtest — the arb
        /// premium moves faster than the 2-5bps window. Market orders ensure execution,
        /// which is the priority for a backtest that needs to validate the strategy logic.
        /// </summary>
        private void EnterShortPerpLongSpot(string ticker, decimal premium, decimal upperBound)
        {
            decimal targetValue = Portfolio.TotalPortfolioValue * _positionFraction;
            var spot = Securities[_spotSymbols[ticker]];
            var perp = Securities[_perpSymbols[ticker]];

            var spotQuantity = Math.Floor((targetValue / spot.Price) / spot.SymbolProperties.LotSize) * spot.SymbolProperties.LotSize;
            var perpQuantity = -Math.Floor((targetValue / perp.Price) / perp.SymbolProperties.LotSize) * perp.SymbolProperties.LotSize;

            Log($"ENTER {ticker} SHORT PERP / LONG SPOT: Spot={spotQuantity}@{spot.Price:F2}, Perp={perpQuantity}@{perp.Price:F2}, Premium={premium:P4}");

            MarketOrder(_spotSymbols[ticker], spotQuantity);
            MarketOrder(_perpSymbols[ticker], perpQuantity);

            _positions[ticker] = new PerpArbPosition
            {
                Direction = PerpArbDirection.ShortPerpLongSpot,
                EntryPremium = premium,
                EntryBound = upperBound,
                EntryTime = UtcTime,
                Notional = targetValue
            };
        }

        /// <summary>
        /// IMPROVED: Market orders for reverse direction.
        /// </summary>
        private void EnterLongPerpShortSpot(string ticker, decimal premium, decimal lowerBound)
        {
            decimal targetValue = Portfolio.TotalPortfolioValue * _positionFraction;
            var spot = Securities[_spotSymbols[ticker]];
            var perp = Securities[_perpSymbols[ticker]];

            var spotQuantity = -Math.Floor((targetValue / spot.Price) / spot.SymbolProperties.LotSize) * spot.SymbolProperties.LotSize;
            var perpQuantity = Math.Floor((targetValue / perp.Price) / perp.SymbolProperties.LotSize) * perp.SymbolProperties.LotSize;

            Log($"ENTER {ticker} LONG PERP / SHORT SPOT: Spot={spotQuantity}@{spot.Price:F2}, Perp={perpQuantity}@{perp.Price:F2}, Premium={premium:P4}");

            MarketOrder(_spotSymbols[ticker], spotQuantity);
            MarketOrder(_perpSymbols[ticker], perpQuantity);

            _positions[ticker] = new PerpArbPosition
            {
                Direction = PerpArbDirection.LongPerpShortSpot,
                EntryPremium = premium,
                EntryBound = lowerBound,
                EntryTime = UtcTime,
                Notional = targetValue
            };
        }

        /// <summary>
        /// FIXED: Position management with trailing stop that only activates AFTER profit is seen.
        /// Previously, the trailing stop fired immediately when premium moved against the position
        /// because _bestPremiums was initialized at entry price and any adverse move looked like
        /// "retracement from best" — causing premature exits on every losing trade.
        /// 
        /// Now: trailing stop is ONLY active AFTER premiumChange has turned positive (profit shown).
        /// Until then, only the composite stop loss protects the position.
        /// Also fixed: stop loss is checked BEFORE trailing stop for priority ordering.
        /// </summary>
        private void ManagePosition(string ticker, decimal currentPremium, decimal upperBound, decimal lowerBound)
        {
            var pos = _positions[ticker];
            var maxHoldHours = _maxHoldHoursBySymbol.ContainsKey(ticker) ? _maxHoldHoursBySymbol[ticker] : 48.0;
            var exitBuffer = _exitBufferBySymbol.ContainsKey(ticker) ? _exitBufferBySymbol[ticker] : _exitBufferBps;
            bool shouldExit = false;

            // Calculate premium movement since entry
            decimal premiumChange;
            if (pos.Direction == PerpArbDirection.ShortPerpLongSpot)
            {
                // For short perp/long spot: we profit when premium DECREASES (goes toward zero or negative)
                premiumChange = pos.EntryPremium - currentPremium; // Positive = good
            }
            else
            {
                // For long perp/short spot: we profit when premium INCREASES (goes toward zero or positive)
                premiumChange = currentPremium - pos.EntryPremium; // Positive = good
            }

            // FIX: Track profit-seen and best-premium separately.
            // _bestPremiums should only be tracked AFTER profit has been shown.
            // Until then, keep _bestPremiums = currentPremium so the trailing stop
            // doesn't fire on adverse movement.
            if (!_bestPremiums.ContainsKey(ticker))
                _bestPremiums[ticker] = currentPremium; // Start trailing from current, not entry

            if (!_profitSeen.ContainsKey(ticker))
                _profitSeen[ticker] = false;

            if (premiumChange > 0)
            {
                _profitSeen[ticker] = true;
                // Update best premium — only after profit has been seen
                _bestPremiums[ticker] = pos.Direction == PerpArbDirection.ShortPerpLongSpot
                    ? Math.Min(_bestPremiums[ticker], currentPremium)
                    : Math.Max(_bestPremiums[ticker], currentPremium);
            }
            else if (!_profitSeen[ticker])
            {
                // FIX: While no profit has been seen, keep best premium = current
                // so trailing stop doesn't trigger on adverse movement from entry
                _bestPremiums[ticker] = currentPremium;
            }

            // IMPROVED: Partial exit at 60% of target gain (lock in profits)
            decimal targetGain = Math.Abs(pos.EntryPremium - (pos.Direction == PerpArbDirection.ShortPerpLongSpot ? exitBuffer : -exitBuffer));
            if (!_partialExitDone.ContainsKey(ticker) || !_partialExitDone[ticker])
            {
                if (premiumChange >= targetGain * 0.60m)
                {
                    Log($"PARTIAL EXIT {ticker}: Premium improved {premiumChange:P4} (target {targetGain:P4}), locking 50%");
                    // Partially close: sell half of perp, cover half of spot
                    var spot = Securities[_spotSymbols[ticker]];
                    var perp = Securities[_perpSymbols[ticker]];
                    if (pos.Direction == PerpArbDirection.ShortPerpLongSpot)
                    {
                        // Short perp (negative qty): buy back half. Long spot (positive qty): sell half.
                        var perpToClose = Math.Abs(perp.Holdings.Quantity) * _partialExitPct;
                        var spotToClose = Math.Abs(spot.Holdings.Quantity) * _partialExitPct;
                        MarketOrder(_perpSymbols[ticker], perpToClose); // buy back
                        MarketOrder(_spotSymbols[ticker], -spotToClose); // sell
                    }
                    else
                    {
                        // Long perp (positive qty): sell half. Short spot (negative qty): buy back half.
                        var perpToClose = Math.Abs(perp.Holdings.Quantity) * _partialExitPct;
                        var spotToClose = Math.Abs(spot.Holdings.Quantity) * _partialExitPct;
                        MarketOrder(_perpSymbols[ticker], -perpToClose); // sell
                        MarketOrder(_spotSymbols[ticker], spotToClose); // buy back
                    }
                    _partialExitDone[ticker] = true;
                }
            }

            // Check exit conditions (ordered by priority)

            // 1. Composite stop loss (protects total position) — checked FIRST
            if (premiumChange < -_compositeStopLossPct)
            {
                shouldExit = true;
                Log($"STOP LOSS EXIT {ticker}: Premium change {premiumChange:P4} exceeded -{_compositeStopLossPct:P4}");
            }

            // 2. Premium converged to zero (successful exit)
            if (!shouldExit)
            {
                if (pos.Direction == PerpArbDirection.ShortPerpLongSpot)
                {
                    if (currentPremium <= exitBuffer) shouldExit = true;
                }
                else if (pos.Direction == PerpArbDirection.LongPerpShortSpot)
                {
                    if (currentPremium >= -exitBuffer) shouldExit = true;
                }
            }

            // 3. Trailing stop — ONLY active after profit has been seen
            if (!shouldExit && _profitSeen[ticker])
            {
                decimal bestPremium = _bestPremiums[ticker];
                decimal trailingDistance;
                if (pos.Direction == PerpArbDirection.ShortPerpLongSpot)
                {
                    trailingDistance = currentPremium - bestPremium;
                    if (trailingDistance > _trailingStopBps)
                    {
                        shouldExit = true;
                        Log($"TRAILING STOP EXIT {ticker}: Retraced {trailingDistance:P4} from best premium {bestPremium:P4}");
                    }
                }
                else
                {
                    trailingDistance = bestPremium - currentPremium;
                    if (trailingDistance > _trailingStopBps)
                    {
                        shouldExit = true;
                        Log($"TRAILING STOP EXIT {ticker}: Retraced {trailingDistance:P4} from best premium {bestPremium:P4}");
                    }
                }
            }

            // 4. Max hold time
            if (!shouldExit && (UtcTime - pos.EntryTime).TotalHours > maxHoldHours)
            {
                shouldExit = true;
                Log($"MAX HOLD EXIT {ticker}: Held {(UtcTime - pos.EntryTime).TotalHours:F1}h, exiting");
            }

            if (shouldExit)
            {
                Log($"EXIT {ticker}: Premium={currentPremium:P4}, Entry={pos.EntryPremium:P4}, Change={premiumChange:P4}");
                Liquidate(_spotSymbols[ticker]);
                Liquidate(_perpSymbols[ticker]);
                _positions.Remove(ticker);
                _lastExitTimes[ticker] = UtcTime;
                // Clean up tracking
                _bestPremiums.Remove(ticker);
                _profitSeen.Remove(ticker);
                _partialExitDone.Remove(ticker);
            }
        }
    }

    internal class PerpArbPosition
    {
        public PerpArbDirection Direction { get; set; }
        public decimal EntryPremium { get; set; }
        public decimal EntryBound { get; set; }
        public DateTime EntryTime { get; set; }
        public decimal Notional { get; set; }
    }

    internal enum PerpArbDirection { ShortPerpLongSpot, LongPerpShortSpot }
}