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
    public class BTCETHPairsTrading : QCAlgorithm
    {
        // Strategy parameters
        private Symbol _btcSymbol;
        private Symbol _ethSymbol;
        private RollingWindow<decimal> _btcPrices;
        private RollingWindow<decimal> _ethPrices;
        private RollingWindow<decimal> _spreads;
        private SimpleMovingAverage _spreadMean;
        private StandardDeviation _spreadStd;
        private ExponentialMovingAverage _ewmaVol;
        private decimal _hedgeRatio;
        private decimal _threshold = 2.0m;
        private decimal _thetaMin = 1.5m;
        private decimal _thetaMax = 3.0m;
        private decimal _k = 2.0m;
        private decimal _initialCapital;
        private decimal _maxDrawdown = 0.05m;
        private decimal _sigma0 = 0.05m; // baseline volatility
        private int _lookback = 100;
        private bool _inPosition = false;
        
        // OU process parameters
        private decimal _lambda; // mean-reversion speed
        private decimal _mu;     // long-term mean
        private decimal _sigma;  // volatility

        public override void Initialize()
        {
            SetStartDate(2020, 1, 1);
            SetEndDate(2020, 1, 31);  // Shorter test period
            SetCash(100000);
            _initialCapital = 100000;

            // Try using the available symbols
            _btcSymbol = AddCrypto("BTCUSDT", Resolution.Hour, Market.Binance).Symbol;
            _ethSymbol = AddCrypto("ETHUSDT", Resolution.Hour, Market.Binance).Symbol;
            
            Debug($"Added BTC symbol: {_btcSymbol}");
            Debug($"Added ETH symbol: {_ethSymbol}");

            _btcPrices = new RollingWindow<decimal>(_lookback);
            _ethPrices = new RollingWindow<decimal>(_lookback);
            _spreads = new RollingWindow<decimal>(_lookback);
            _spreadMean = new SimpleMovingAverage(_lookback);
            _spreadStd = new StandardDeviation(_lookback);
            _ewmaVol = new ExponentialMovingAverage(20); // 20 period EWMA
        }

        public override void OnData(Slice slice)
        {
            // Debug: Print what symbols we have in the slice
            Debug($"OnData called at {Time}. Slice contains {slice.Count} items.");
            
            if (slice.ContainsKey(_btcSymbol))
            {
                Debug($"BTC data available: {slice[_btcSymbol].Close}");
            }
            else
            {
                Debug("BTC data NOT available");
            }
            
            if (slice.ContainsKey(_ethSymbol))
            {
                Debug($"ETH data available: {slice[_ethSymbol].Close}");
            }
            else
            {
                Debug("ETH data NOT available");
            }

            if (!slice.ContainsKey(_btcSymbol) || !slice.ContainsKey(_ethSymbol))
            {
                Debug("Missing data for one or both symbols");
                return;
            }

            var btcPrice = slice[_btcSymbol].Close;
            var ethPrice = slice[_ethSymbol].Close;
            
            Debug($"BTC: {btcPrice}, ETH: {ethPrice}");

            _btcPrices.Add(btcPrice);
            _ethPrices.Add(ethPrice);

            if (_btcPrices.Count < _lookback)
            {
                Debug($"Waiting for more data. Current count: {_btcPrices.Count}/{_lookback}");
                return;
            }

            Debug("Have enough data to start trading logic");

            // Calculate hedge ratio using OLS
            _hedgeRatio = CalculateHedgeRatio();

            // Calculate spread
            var spread = btcPrice - _hedgeRatio * ethPrice;
            _spreads.Add(spread);
            _spreadMean.Update(spread);
            _spreadStd.Update(spread);

            // Fit OU process
            FitOUProcess();

            // Adaptive threshold
            _threshold = _thetaMin + (_thetaMax - _thetaMin) / (1 + (decimal)Math.Exp((double)(- _k * (_sigma / _sigma0 - 1))));

            if (_spreadMean.IsReady && _spreadStd.IsReady)
            {
                var zScore = (spread - _mu) / _sigma;
                
                Debug($"Z-Score: {zScore}, Threshold: {_threshold}, In Position: {_inPosition}");

                if (Math.Abs(zScore) > _threshold && !_inPosition)
                {
                    Debug($"Entering position - Z-Score {zScore} exceeds threshold {_threshold}");
                    
                    // Enter position
                    var btcQuantity = Portfolio.TotalPortfolioValue / (2 * btcPrice);
                    var ethQuantity = btcQuantity * _hedgeRatio;

                    if (zScore > 0)
                    {
                        // Spread is high, expect mean reversion: short BTC, long ETH
                        Debug($"Short BTC {btcQuantity}, Long ETH {ethQuantity}");
                        MarketOrder(_btcSymbol, -btcQuantity);
                        MarketOrder(_ethSymbol, ethQuantity);
                    }
                    else
                    {
                        // Spread is low, expect mean reversion: long BTC, short ETH
                        Debug($"Long BTC {btcQuantity}, Short ETH {ethQuantity}");
                        MarketOrder(_btcSymbol, btcQuantity);
                        MarketOrder(_ethSymbol, -ethQuantity);
                    }
                    _inPosition = true;
                }
                else if (Math.Abs(zScore) < 0.5m && _inPosition)
                {
                    Debug($"Exiting position - Z-Score {zScore} below exit threshold");
                    // Exit position
                    Liquidate();
                    _inPosition = false;
                }
            }
            else
            {
                Debug("Indicators not ready yet");
            }
        }

        private decimal CalculateHedgeRatio()
        {
            var btcList = _btcPrices.ToList();
            var ethList = _ethPrices.ToList();
            var n = btcList.Count;

            var sumX = ethList.Sum();
            var sumY = btcList.Sum();
            var sumXY = btcList.Zip(ethList, (y, x) => y * x).Sum();
            var sumX2 = ethList.Sum(x => x * x);

            return (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        }

        private void FitOUProcess()
        {
            var spreads = _spreads.ToList();
            if (spreads.Count < 2) return;

            var deltaS = new List<decimal>();
            var sLag = new List<decimal>();

            for (int i = 1; i < spreads.Count; i++)
            {
                deltaS.Add(spreads[i] - spreads[i-1]);
                sLag.Add(spreads[i-1]);
            }

            var n = deltaS.Count;
            var sumDelta = deltaS.Sum();
            var sumSLag = sLag.Sum();
            var sumDeltaSLag = deltaS.Zip(sLag, (d, s) => d * s).Sum();
            var sumSLag2 = sLag.Sum(s => s * s);

            // OLS: deltaS = alpha + beta * sLag
            var beta = (n * sumDeltaSLag - sumDelta * sumSLag) / (n * sumSLag2 - sumSLag * sumSLag);
            var alpha = (sumDelta - beta * sumSLag) / n;

            _lambda = -beta;
            _mu = -alpha / beta;
            
            // Calculate sigma
            var residuals = deltaS.Zip(sLag, (d, s) => d - alpha - beta * s).ToList();
            _sigma = (decimal)Math.Sqrt(residuals.Sum(r => (double)(r * r)) / (n - 2));
        }
    }
}