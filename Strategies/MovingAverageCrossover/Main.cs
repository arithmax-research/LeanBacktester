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
    public class MovingAverageCrossover : QCAlgorithm
    {
        private SimpleMovingAverage _fastMA;
        private SimpleMovingAverage _slowMA;
        private Symbol _symbol;

        public override void Initialize()
        {
            // Set the date range for backtesting
            SetStartDate(2020, 1, 1);
            SetEndDate(2025, 08, 31);
            SetCash(1000);

            // Add equity with PLTR
            _symbol = AddEquity("PLTR", Resolution.Daily).Symbol;
            
            // Set up moving averages
            _fastMA = SMA(_symbol, 50);
            _slowMA = SMA(_symbol, 200);

            // Set warmup period to ensure indicators are ready
            SetWarmUp(200);
        }

        public override void OnData(Slice data)
        {
            // Don't trade during warmup period
            if (IsWarmingUp) return;
            
            // Ensure both moving averages are ready
            if (!_fastMA.IsReady || !_slowMA.IsReady) return;
            
            // Golden cross: Fast MA crosses above Slow MA (buy signal)
            if (!Portfolio.Invested && _fastMA > _slowMA)
            {
                SetHoldings(_symbol, 1.0);
                Debug($"Purchased {_symbol}: Fast MA ({_fastMA.Current.Value:F2}) > Slow MA ({_slowMA.Current.Value:F2})");
            }
            // Death cross: Fast MA crosses below Slow MA (sell signal)
            else if (Portfolio.Invested && _fastMA < _slowMA)
            {
                Liquidate(_symbol);
                Debug($"Sold {_symbol}: Fast MA ({_fastMA.Current.Value:F2}) < Slow MA ({_slowMA.Current.Value:F2})");
            }
        }
    }
}
