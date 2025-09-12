import React from 'react';

const PerformanceMetrics = ({ metrics }) => {
  if (!metrics) return null;

  const formatCurrency = (value) => `$${value.toLocaleString()}`;
  const formatPercent = (value) => `${value.toFixed(2)}%`;
  const formatNumber = (value) => value.toLocaleString();

  return (
    <div className="performance-metrics">
      <h2>Performance Metrics</h2>

      <div className="metrics-grid">
        <div className="metric-card">
          <h3>Total P&L</h3>
          <p className={metrics.totalPnL >= 0 ? 'positive' : 'negative'}>
            {formatCurrency(metrics.totalPnL)}
          </p>
        </div>

        <div className="metric-card">
          <h3>Win Rate</h3>
          <p>{formatPercent(metrics.winRate)}</p>
        </div>

        <div className="metric-card">
          <h3>Total Trades</h3>
          <p>{formatNumber(metrics.totalTrades)}</p>
        </div>

        <div className="metric-card">
          <h3>Profit Factor</h3>
          <p>{metrics.profitFactor.toFixed(2)}</p>
        </div>

        <div className="metric-card">
          <h3>Sharpe Ratio</h3>
          <p>{metrics.sharpeRatio.toFixed(3)}</p>
        </div>

        <div className="metric-card">
          <h3>Max Drawdown</h3>
          <p className="negative">{formatPercent(metrics.maxDrawdown)}</p>
        </div>

        <div className="metric-card">
          <h3>Largest Profit</h3>
          <p className="positive">{formatCurrency(metrics.largestProfit)}</p>
        </div>

        <div className="metric-card">
          <h3>Largest Loss</h3>
          <p className="negative">{formatCurrency(metrics.largestLoss)}</p>
        </div>

        <div className="metric-card">
          <h3>Average P&L</h3>
          <p className={metrics.averagePnL >= 0 ? 'positive' : 'negative'}>
            {formatCurrency(metrics.averagePnL)}
          </p>
        </div>

        <div className="metric-card">
          <h3>Total Fees</h3>
          <p className="negative">{formatCurrency(metrics.totalFees)}</p>
        </div>

        <div className="metric-card">
          <h3>Winning Trades</h3>
          <p className="positive">{formatNumber(metrics.winningTrades)}</p>
        </div>

        <div className="metric-card">
          <h3>Losing Trades</h3>
          <p className="negative">{formatNumber(metrics.losingTrades)}</p>
        </div>
      </div>
    </div>
  );
};

export default PerformanceMetrics;
