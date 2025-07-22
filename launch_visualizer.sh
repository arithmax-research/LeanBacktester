#!/bin/bash

# Lean Backtest Visualizer Launcher
# This script sets up the environment and launches the Streamlit app

echo "Arithmax Backtest Visualizer Launcher"
echo "===================================="
echo "🎯 Launching Backtest Visualizer..."
echo "The app will open in your default web browser."
echo "If it doesn't open automatically, navigate to: http://localhost:8501"
echo ""
echo "To stop the app, press Ctrl+C in this terminal."
echo ""

streamlit run backtest_visualizer.py --server.headless=false --server.port=8501
