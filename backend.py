#!/usr/bin/env python3
"""
Lean Backtest Visualizer API

Flask API backend for serving backtest data to the React frontend
"""

import json
import os
import pandas as pd
import numpy as np
from datetime import datetime
from flask import Flask, jsonify, request
from flask_cors import CORS
from pathlib import Path
import glob
from typing import Dict, List, Any, Optional, Tuple


class BacktestDataService:
    def __init__(self, root_path: str):
        self.root_path = Path(root_path)
        self.strategies = self._discover_strategies()

    def _discover_strategies(self) -> Dict[str, List[str]]:
        """Discover all available strategies and their backtest runs"""
        strategies = {}

        strategy_path = self.root_path / "arithmax-strategies"

        if not strategy_path.exists():
            return strategies

        strategy_dirs = glob.glob(str(strategy_path / "*"))

        for strategy_dir in strategy_dirs:
            strategy_name = Path(strategy_dir).name
            backtest_dir = Path(strategy_dir) / "backtests"

            if backtest_dir.exists():
                backtest_runs = [d.name for d in backtest_dir.iterdir() if d.is_dir()]
                backtest_runs.sort(reverse=True)

                if backtest_runs:
                    strategies[strategy_name] = backtest_runs

        return strategies

    def load_backtest_data(self, strategy_name: str, backtest_run: str) -> Dict[str, Any]:
        """Load backtest data from JSON files"""
        backtest_path = self.root_path / "arithmax-strategies" / strategy_name / "backtests" / backtest_run

        data = {}

        # Load summary data
        summary_files = glob.glob(str(backtest_path / "*-summary.json"))
        if summary_files:
            with open(summary_files[0], 'r') as f:
                data['summary'] = json.load(f)

        # Load order events
        order_files = glob.glob(str(backtest_path / "*-order-events.json"))
        if order_files:
            with open(order_files[0], 'r') as f:
                data['orders'] = json.load(f)

        # Load main backtest data
        main_files = glob.glob(str(backtest_path / "*[0-9].json"))
        if main_files:
            main_file = [f for f in main_files if not any(suffix in f for suffix in ['-summary', '-order-events'])][0]
            with open(main_file, 'r') as f:
                data['main'] = json.load(f)

        return data

    def parse_equity_curve(self, data: Dict[str, Any]) -> List[Dict]:
        """Parse equity curve data for API response"""
        try:
            if 'charts' in data:
                charts = data['charts']

                if 'Strategy Equity' in charts:
                    strategy_equity = charts['Strategy Equity']

                    if 'series' in strategy_equity:
                        series = strategy_equity['series']

                        if 'Equity' in series:
                            equity_data = series['Equity']

                            if 'values' in equity_data:
                                values = equity_data['values']

                                equity_curve = []
                                for point in values:
                                    if len(point) >= 2:
                                        timestamp = point[0]
                                        value = point[1]
                                        equity_curve.append({
                                            'timestamp': timestamp * 1000,  # Convert to milliseconds for JS
                                            'value': value
                                        })

                                return equity_curve

            # Fallback to old format
            if 'main' in data:
                charts = data['main'].get('Charts', {})

                equity_data = []
                for chart_name, chart_data in charts.items():
                    if 'Equity' in chart_name or 'Portfolio' in chart_name:
                        series = chart_data.get('Series', {})
                        for series_name, series_data in series.items():
                            if 'Values' in series_data:
                                for point in series_data['Values']:
                                    equity_data.append({
                                        'timestamp': point['x'] * 1000,
                                        'value': point['y']
                                    })

                return equity_data

            return []
        except Exception as e:
            print(f"Error parsing equity curve: {e}")
            return []

    def parse_trades(self, data: Dict[str, Any]) -> List[Dict]:
        """Parse trade data for API response"""
        if 'orders' not in data:
            return []

        orders = data['orders']
        if not orders:
            return []

        # Group orders by order ID
        order_groups = {}
        for order in orders:
            order_id = order['orderId']
            if order_id not in order_groups:
                order_groups[order_id] = []
            order_groups[order_id].append(order)

        trades = []
        for order_id, order_list in order_groups.items():
            filled_orders = [o for o in order_list if o['status'] == 'filled']

            for order in filled_orders:
                trades.append({
                    'timestamp': order['time'] * 1000,
                    'symbol': order['symbol'],
                    'direction': order['direction'],
                    'quantity': order['quantity'],
                    'price': order['fillPrice'],
                    'value': abs(order['quantity']) * order['fillPrice'],
                    'fee': order.get('orderFeeAmount', 0)
                })

        return trades

    def get_performance_metrics(self, data: Dict[str, Any]) -> Dict[str, Any]:
        """Extract performance metrics for API response"""
        if 'summary' not in data:
            return {}

        summary = data['summary']
        total_perf = summary.get('totalPerformance', {})
        trade_stats = total_perf.get('tradeStatistics', {})
        portfolio_stats = total_perf.get('portfolioStatistics', {})

        return {
            'totalTrades': trade_stats.get('totalNumberOfTrades', 0),
            'winningTrades': trade_stats.get('numberOfWinningTrades', 0),
            'losingTrades': trade_stats.get('numberOfLosingTrades', 0),
            'winRate': float(trade_stats.get('winRate', 0)) * 100,
            'totalPnL': float(trade_stats.get('totalProfitLoss', 0)),
            'totalProfit': float(trade_stats.get('totalProfit', 0)),
            'totalLoss': float(trade_stats.get('totalLoss', 0)),
            'largestProfit': float(trade_stats.get('largestProfit', 0)),
            'largestLoss': float(trade_stats.get('largestLoss', 0)),
            'averagePnL': float(trade_stats.get('averageProfitLoss', 0)),
            'profitFactor': float(trade_stats.get('profitFactor', 0)),
            'sharpeRatio': float(trade_stats.get('sharpeRatio', 0)),
            'sortinoRatio': float(trade_stats.get('sortinoRatio', 0)),
            'maxDrawdown': float(portfolio_stats.get('drawdown', 0)) * 100,
            'totalFees': float(trade_stats.get('totalFees', 0)),
            'averageTradeDuration': trade_stats.get('averageTradeDuration', 'N/A')
        }


app = Flask(__name__)
CORS(app)  # Enable CORS for React frontend

# Initialize data service
current_dir = Path(__file__).parent
data_service = BacktestDataService(current_dir)


@app.route('/api/strategies', methods=['GET'])
def get_strategies():
    """Get list of available strategies"""
    return jsonify(data_service.strategies)


@app.route('/api/backtest/<strategy>/<run>', methods=['GET'])
def get_backtest_data(strategy, run):
    """Get backtest data for a specific strategy and run"""
    try:
        data = data_service.load_backtest_data(strategy, run)

        if not data:
            return jsonify({'error': 'Backtest data not found'}), 404

        response = {
            'equityCurve': data_service.parse_equity_curve(data),
            'trades': data_service.parse_trades(data),
            'metrics': data_service.get_performance_metrics(data)
        }

        return jsonify(response)
    except Exception as e:
        return jsonify({'error': str(e)}), 500


if __name__ == '__main__':
    app.run(debug=True, port=5000)
