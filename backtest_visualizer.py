#!/usr/bin/env python3
"""
Lean Backtest Visualizer

Interactive visualization tool for Lean backtest results with TradingView-style charts
Supports multiple strategies and comprehensive performance analysis.
"""

import json
import os
import pandas as pd
import numpy as np
from datetime import datetime
import plotly.graph_objects as go
from plotly.subplots import make_subplots
import plotly.express as px
import streamlit as st
from pathlib import Path
import glob
from typing import Dict, List, Any, Optional, Tuple


class BacktestVisualizer:
    def __init__(self, root_path: str):
        self.root_path = Path(root_path)
        self.strategies = self._discover_strategies()
        self.selected_strategy = None
        self.backtest_data = None
        
    def _discover_strategies(self) -> Dict[str, List[str]]:
        """Discover all available strategies and their backtest runs"""
        strategies = {}
        
        # Look for strategy directories
        strategy_path = self.root_path / "arithmax-strategies"
        print(f"Looking for strategies in: {strategy_path}")
        
        if not strategy_path.exists():
            print(f"Strategy path does not exist: {strategy_path}")
            return strategies
        
        strategy_dirs = glob.glob(str(strategy_path / "*"))
        print(f"Found {len(strategy_dirs)} potential strategy directories")
        
        for strategy_dir in strategy_dirs:
            strategy_name = Path(strategy_dir).name
            backtest_dir = Path(strategy_dir) / "backtests"
            
            print(f"Checking strategy: {strategy_name}")
            print(f"Backtest dir: {backtest_dir}")
            
            if backtest_dir.exists():
                # Get all backtest runs for this strategy
                backtest_runs = [d.name for d in backtest_dir.iterdir() if d.is_dir()]
                backtest_runs.sort(reverse=True)  # Most recent first
                
                print(f"Found {len(backtest_runs)} backtest runs for {strategy_name}")
                
                if backtest_runs:
                    strategies[strategy_name] = backtest_runs
                    print(f"Added strategy {strategy_name} with runs: {backtest_runs}")
            else:
                print(f"No backtests directory found for {strategy_name}")
        
        print(f"Total strategies discovered: {len(strategies)}")
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
        
        # Load main backtest data (equity curve, etc.)
        main_files = glob.glob(str(backtest_path / "*[0-9].json"))
        if main_files:
            # Find the main data file (not summary or order events)
            main_file = [f for f in main_files if not any(suffix in f for suffix in ['-summary', '-order-events'])][0]
            with open(main_file, 'r') as f:
                data['main'] = json.load(f)
        
        return data
    
    def parse_equity_curve(self, data: Dict[str, Any]) -> pd.DataFrame:
        """Parse equity curve data from the main backtest file"""
        try:
            print(f"DEBUG: Starting equity curve parsing")
            print(f"DEBUG: Root keys: {list(data.keys())}")
            
            # Check if there's a charts section with equity data (lowercase)
            if 'charts' in data:
                charts = data['charts']
                print(f"DEBUG: Found charts section with keys: {list(charts.keys())}")
                
                if 'Strategy Equity' in charts:
                    strategy_equity = charts['Strategy Equity']
                    print(f"DEBUG: Found Strategy Equity with keys: {list(strategy_equity.keys())}")
                    
                    if 'series' in strategy_equity:
                        series = strategy_equity['series']
                        print(f"DEBUG: Found series with keys: {list(series.keys())}")
                        
                        if 'Equity' in series:
                            equity_data = series['Equity']
                            print(f"DEBUG: Found Equity data with keys: {list(equity_data.keys())}")
                            
                            if 'values' in equity_data:
                                values = equity_data['values']
                                print(f"DEBUG: Found values array with length: {len(values)}")
                                
                                if len(values) > 0:
                                    print(f"DEBUG: Sample value: {values[0]}")
                                
                                equity_curve = []
                                for point in values:
                                    if len(point) >= 2:
                                        timestamp = point[0]
                                        value = point[1]  # Use the second value (close price)
                                        equity_curve.append({
                                            'timestamp': pd.to_datetime(timestamp, unit='s'),
                                            'value': value
                                        })
                                
                                if equity_curve:
                                    print(f"DEBUG: Created equity curve with {len(equity_curve)} points")
                                    df = pd.DataFrame(equity_curve)
                                    df = df.drop_duplicates(subset=['timestamp']).sort_values('timestamp')
                                    print(f"DEBUG: Final dataframe shape: {df.shape}")
                                    return df
            
            print(f"DEBUG: No charts section found, trying fallback")
            # Fallback: try old structure
            if 'main' not in data:
                print(f"DEBUG: No main section found")
                return pd.DataFrame()
            
            charts = data['main'].get('Charts', {})
            print(f"DEBUG: Old format charts keys: {list(charts.keys())}")
            
            # Look for equity curve data
            equity_data = []
            
            for chart_name, chart_data in charts.items():
                if 'Equity' in chart_name or 'Portfolio' in chart_name:
                    series = chart_data.get('Series', {})
                    for series_name, series_data in series.items():
                        if 'Values' in series_data:
                            for point in series_data['Values']:
                                equity_data.append({
                                    'timestamp': pd.to_datetime(point['x'], unit='s'),
                                    'value': point['y']
                                })
            
            if equity_data:
                df = pd.DataFrame(equity_data)
                df = df.drop_duplicates(subset=['timestamp']).sort_values('timestamp')
                return df
            
            # Fallback: try to extract from rolling window data
            rolling_data = data.get('summary', {}).get('rollingWindow', {})
            if rolling_data:
                # This is a simplified fallback - in practice, you'd need to parse the actual equity curve
                return pd.DataFrame()
            
            print(f"DEBUG: No equity data found")
            return pd.DataFrame()
        except Exception as e:
            print(f"Error parsing equity curve: {e}")
            import traceback
            traceback.print_exc()
            return pd.DataFrame()
    
    def parse_trades(self, data: Dict[str, Any]) -> pd.DataFrame:
        """Parse trade data from order events"""
        if 'orders' not in data:
            return pd.DataFrame()
        
        orders = data['orders']
        if not orders:
            return pd.DataFrame()
        
        # Group orders by order ID to reconstruct trades
        order_groups = {}
        for order in orders:
            order_id = order['orderId']
            if order_id not in order_groups:
                order_groups[order_id] = []
            order_groups[order_id].append(order)
        
        trades = []
        for order_id, order_list in order_groups.items():
            # Find filled orders
            filled_orders = [o for o in order_list if o['status'] == 'filled']
            
            for order in filled_orders:
                trades.append({
                    'timestamp': pd.to_datetime(order['time'], unit='s'),
                    'symbol': order['symbol'],
                    'direction': order['direction'],
                    'quantity': order['quantity'],
                    'price': order['fillPrice'],
                    'value': abs(order['quantity']) * order['fillPrice'],
                    'fee': order.get('orderFeeAmount', 0)
                })
        
        return pd.DataFrame(trades)
    
    def create_equity_curve_chart(self, df: pd.DataFrame) -> go.Figure:
        """Create an interactive equity curve chart"""
        if df.empty:
            return go.Figure()
        
        # Calculate additional metrics
        df['returns'] = df['value'].pct_change()
        df['cumulative_returns'] = (1 + df['returns']).cumprod()
        df['drawdown'] = df['value'] / df['value'].cummax() - 1
        
        # Create subplots
        fig = make_subplots(
            rows=3, cols=1,
            subplot_titles=('Portfolio Value', 'Daily Returns', 'Drawdown'),
            vertical_spacing=0.1,
            row_heights=[0.5, 0.25, 0.25]
        )
        
        # Portfolio value
        fig.add_trace(
            go.Scatter(
                x=df['timestamp'],
                y=df['value'],
                mode='lines',
                name='Portfolio Value',
                line=dict(color='#00D4AA', width=2),
                hovertemplate='<b>%{x}</b><br>Value: $%{y:,.2f}<extra></extra>'
            ),
            row=1, col=1
        )
        
        # Daily returns
        fig.add_trace(
            go.Scatter(
                x=df['timestamp'],
                y=df['returns'] * 100,
                mode='lines',
                name='Daily Returns (%)',
                line=dict(color='#FF6B6B', width=1),
                hovertemplate='<b>%{x}</b><br>Return: %{y:.2f}%<extra></extra>'
            ),
            row=2, col=1
        )
        
        # Drawdown
        fig.add_trace(
            go.Scatter(
                x=df['timestamp'],
                y=df['drawdown'] * 100,
                mode='lines',
                name='Drawdown (%)',
                line=dict(color='#FF4757', width=1),
                fill='tozeroy',
                fillcolor='rgba(255, 71, 87, 0.2)',
                hovertemplate='<b>%{x}</b><br>Drawdown: %{y:.2f}%<extra></extra>'
            ),
            row=3, col=1
        )
        
        # Update layout
        fig.update_layout(
            title='Portfolio Performance Analysis',
            template='plotly_dark',
            showlegend=True,
            height=800,
            hovermode='x unified'
        )
        
        # Update axes
        fig.update_xaxes(title_text="Date", row=3, col=1)
        fig.update_yaxes(title_text="Value ($)", row=1, col=1)
        fig.update_yaxes(title_text="Returns (%)", row=2, col=1)
        fig.update_yaxes(title_text="Drawdown (%)", row=3, col=1)
        
        return fig
    
    def create_trades_chart(self, trades_df: pd.DataFrame) -> go.Figure:
        """Create a trades visualization"""
        if trades_df.empty:
            return go.Figure()
        
        # Create subplots for buy/sell signals
        fig = go.Figure()
        
        # Separate buy and sell orders
        buys = trades_df[trades_df['direction'] == 'buy']
        sells = trades_df[trades_df['direction'] == 'sell']
        
        # Add buy signals
        if not buys.empty:
            fig.add_trace(
                go.Scatter(
                    x=buys['timestamp'],
                    y=buys['price'],
                    mode='markers',
                    name='Buy Orders',
                    marker=dict(
                        color='#00D4AA',
                        size=8,
                        symbol='triangle-up'
                    ),
                    hovertemplate='<b>BUY</b><br>%{x}<br>Price: $%{y:.2f}<extra></extra>'
                )
            )
        
        # Add sell signals
        if not sells.empty:
            fig.add_trace(
                go.Scatter(
                    x=sells['timestamp'],
                    y=sells['price'],
                    mode='markers',
                    name='Sell Orders',
                    marker=dict(
                        color='#FF6B6B',
                        size=8,
                        symbol='triangle-down'
                    ),
                    hovertemplate='<b>SELL</b><br>%{x}<br>Price: $%{y:.2f}<extra></extra>'
                )
            )
        
        fig.update_layout(
            title='Trade Signals',
            template='plotly_dark',
            xaxis_title='Date',
            yaxis_title='Price ($)',
            hovermode='x unified'
        )
        
        return fig
    
    def create_performance_metrics_table(self, data: Dict[str, Any]) -> pd.DataFrame:
        """Create a comprehensive performance metrics table"""
        if 'summary' not in data:
            return pd.DataFrame()
        
        summary = data['summary']
        total_perf = summary.get('totalPerformance', {})
        trade_stats = total_perf.get('tradeStatistics', {})
        portfolio_stats = total_perf.get('portfolioStatistics', {})
        
        # Key performance metrics
        metrics = {
            'Total Trades': trade_stats.get('totalNumberOfTrades', 0),
            'Winning Trades': trade_stats.get('numberOfWinningTrades', 0),
            'Losing Trades': trade_stats.get('numberOfLosingTrades', 0),
            'Win Rate': f"{float(trade_stats.get('winRate', 0)) * 100:.2f}%",
            'Total P&L': f"${float(trade_stats.get('totalProfitLoss', 0)):,.2f}",
            'Total Profit': f"${float(trade_stats.get('totalProfit', 0)):,.2f}",
            'Total Loss': f"${float(trade_stats.get('totalLoss', 0)):,.2f}",
            'Largest Profit': f"${float(trade_stats.get('largestProfit', 0)):,.2f}",
            'Largest Loss': f"${float(trade_stats.get('largestLoss', 0)):,.2f}",
            'Average P&L': f"${float(trade_stats.get('averageProfitLoss', 0)):,.2f}",
            'Profit Factor': f"{float(trade_stats.get('profitFactor', 0)):.2f}",
            'Sharpe Ratio': f"{float(trade_stats.get('sharpeRatio', 0)):.3f}",
            'Sortino Ratio': f"{float(trade_stats.get('sortinoRatio', 0)):.3f}",
            'Max Drawdown': f"{float(portfolio_stats.get('drawdown', 0)) * 100:.2f}%",
            'Total Fees': f"${float(trade_stats.get('totalFees', 0)):,.2f}",
            'Average Trade Duration': trade_stats.get('averageTradeDuration', 'N/A')
        }
        
        return pd.DataFrame(list(metrics.items()), columns=['Metric', 'Value'])
    
    def create_monthly_returns_heatmap(self, equity_df: pd.DataFrame) -> go.Figure:
        """Create a monthly returns heatmap"""
        if equity_df.empty:
            return go.Figure()
        
        # Resample to monthly
        equity_df = equity_df.set_index('timestamp')
        monthly_df = equity_df.resample('M').last()
        monthly_returns = monthly_df['value'].pct_change()
        
        # Create pivot table for heatmap
        monthly_returns_df = monthly_returns.reset_index()
        monthly_returns_df['year'] = monthly_returns_df['timestamp'].dt.year
        monthly_returns_df['month'] = monthly_returns_df['timestamp'].dt.month
        
        # Create pivot table
        pivot_table = monthly_returns_df.pivot(index='year', columns='month', values='value')
        
        # Month names for labels
        month_names = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
                      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
        
        # Create heatmap
        fig = go.Figure(data=go.Heatmap(
            z=pivot_table.values * 100,
            x=month_names,
            y=pivot_table.index,
            colorscale='RdYlGn',
            zmid=0,
            colorbar=dict(title="Monthly Return (%)"),
            hovertemplate='<b>%{y} %{x}</b><br>Return: %{z:.2f}%<extra></extra>'
        ))
        
        fig.update_layout(
            title='Monthly Returns Heatmap',
            template='plotly_dark',
            xaxis_title='Month',
            yaxis_title='Year'
        )
        
        return fig


def main():
    st.set_page_config(
        page_title="Lean Backtest Visualizer",
        page_icon="📈",
        layout="wide",
        initial_sidebar_state="expanded"
    )
    
    st.title("Arithmax Backtest Visualizer")
    st.markdown("Interactive visualization tool for Arithmax backtest results with TradingView-style charts")

    # Initialize visualizer
    current_dir = Path(__file__).parent
    visualizer = BacktestVisualizer(current_dir)
    
    # Sidebar for strategy selection
    st.sidebar.header("Strategy Selection")
    
    # Debug info
    st.sidebar.write(f"Found {len(visualizer.strategies)} strategies")
    
    if not visualizer.strategies:
        st.sidebar.error("No strategies found. Please ensure you have backtest results in the arithmax-strategies directory.")
        st.sidebar.write("Looking for strategies in:")
        st.sidebar.code(str(visualizer.root_path / "arithmax-strategies"))
        st.stop()
    
    # Show discovered strategies
    st.sidebar.write("**Available Strategies:**")
    for strategy, runs in visualizer.strategies.items():
        st.sidebar.write(f"- {strategy}: {len(runs)} runs")
    
    # Strategy selector
    strategy_name = st.sidebar.selectbox(
        "Select Strategy",
        options=list(visualizer.strategies.keys()),
        key="strategy_selector"
    )
    
    # Backtest run selector
    backtest_runs = visualizer.strategies[strategy_name]
    st.sidebar.write(f"**Backtest runs for {strategy_name}:**")
    for run in backtest_runs:
        st.sidebar.write(f"- {run}")
    
    backtest_run = st.sidebar.selectbox(
        "Select Backtest Run",
        options=backtest_runs,
        key="backtest_run_selector"
    )
    
    # Load data
    with st.spinner("Loading backtest data..."):
        data = visualizer.load_backtest_data(strategy_name, backtest_run)
    
    if not data:
        st.error("Failed to load backtest data")
        st.stop()
    
    # Main content area
    st.header(f"📊 {strategy_name} - {backtest_run}")
    
    # Performance metrics
    st.subheader("📈 Performance Metrics")
    metrics_df = visualizer.create_performance_metrics_table(data)
    
    if not metrics_df.empty:
        # Display metrics in columns
        col1, col2, col3, col4 = st.columns(4)
        
        metrics_dict = dict(zip(metrics_df['Metric'], metrics_df['Value']))
        
        with col1:
            st.metric("Total P&L", metrics_dict.get('Total P&L', 'N/A'))
            st.metric("Win Rate", metrics_dict.get('Win Rate', 'N/A'))
            st.metric("Total Trades", metrics_dict.get('Total Trades', 'N/A'))
            st.metric("Profit Factor", metrics_dict.get('Profit Factor', 'N/A'))
        
        with col2:
            st.metric("Sharpe Ratio", metrics_dict.get('Sharpe Ratio', 'N/A'))
            st.metric("Sortino Ratio", metrics_dict.get('Sortino Ratio', 'N/A'))
            st.metric("Max Drawdown", metrics_dict.get('Max Drawdown', 'N/A'))
            st.metric("Total Fees", metrics_dict.get('Total Fees', 'N/A'))
        
        with col3:
            st.metric("Largest Profit", metrics_dict.get('Largest Profit', 'N/A'))
            st.metric("Largest Loss", metrics_dict.get('Largest Loss', 'N/A'))
            st.metric("Average P&L", metrics_dict.get('Average P&L', 'N/A'))
            st.metric("Winning Trades", metrics_dict.get('Winning Trades', 'N/A'))
        
        with col4:
            st.metric("Losing Trades", metrics_dict.get('Losing Trades', 'N/A'))
            st.metric("Total Profit", metrics_dict.get('Total Profit', 'N/A'))
            st.metric("Total Loss", metrics_dict.get('Total Loss', 'N/A'))
            st.metric("Avg Trade Duration", metrics_dict.get('Average Trade Duration', 'N/A'))
    
    # Charts
    st.subheader("📈 Portfolio Performance")
    
    # Debug - Add key information
    st.write("### Debug Information:")
    st.write(f"- Root level keys: {list(data.keys())}")
    
    # Add charts check
    if 'charts' in data:
        charts = data['charts']
        st.write(f"- Charts keys: {list(charts.keys())}")
        if 'Strategy Equity' in charts:
            strategy_equity = charts['Strategy Equity']
            st.write(f"- Strategy Equity keys: {list(strategy_equity.keys())}")
            if 'series' in strategy_equity:
                series = strategy_equity['series']
                st.write(f"- Series keys: {list(series.keys())}")
                if 'Equity' in series:
                    equity_data = series['Equity']
                    st.write(f"- Equity keys: {list(equity_data.keys())}")
                    if 'values' in equity_data:
                        values = equity_data['values']
                        st.write(f"- Values length: {len(values)}")
                        if len(values) > 0:
                            st.write(f"- Sample value: {values[0]}")
    
    # Parse equity curve
    equity_df = visualizer.parse_equity_curve(data)
    
    if not equity_df.empty:
        equity_chart = visualizer.create_equity_curve_chart(equity_df)
        st.plotly_chart(equity_chart, use_container_width=True)
        
        # Monthly returns heatmap
        st.subheader("🔥 Monthly Returns Heatmap")
        heatmap_chart = visualizer.create_monthly_returns_heatmap(equity_df)
        st.plotly_chart(heatmap_chart, use_container_width=True)
    else:
        st.warning("Could not parse equity curve data from the backtest results.")
    
    # Trades visualization
    st.subheader("💰 Trade Signals")
    trades_df = visualizer.parse_trades(data)
    
    if not trades_df.empty:
        trades_chart = visualizer.create_trades_chart(trades_df)
        st.plotly_chart(trades_chart, use_container_width=True)
        
        # Trade statistics
        st.subheader("📊 Trade Statistics")
        
        col1, col2 = st.columns(2)
        
        with col1:
            st.write("**Trade Distribution by Symbol**")
            symbol_counts = trades_df['symbol'].value_counts()
            st.bar_chart(symbol_counts)
        
        with col2:
            st.write("**Trade Volume by Direction**")
            direction_volume = trades_df.groupby('direction')['value'].sum()
            st.bar_chart(direction_volume)
    else:
        st.warning("Could not parse trade data from the backtest results.")
    
    # Detailed metrics table
    st.subheader("📋 Detailed Metrics")
    if not metrics_df.empty:
        # Convert to string type to avoid Arrow serialization issues
        metrics_df = metrics_df.astype(str)
        st.dataframe(metrics_df, use_container_width=True)
    
    # Raw data explorer
    with st.expander("🔍 Raw Data Explorer"):
        st.write("**Summary Data**")
        if 'summary' in data:
            st.json(data['summary'])
        
        st.write("**Sample Order Events**")
        if 'orders' in data and data['orders']:
            st.json(data['orders'][:5])  # Show first 5 orders


if __name__ == "__main__":
    main()
