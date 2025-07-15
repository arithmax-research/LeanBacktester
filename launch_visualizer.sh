#!/bin/bash

# Lean Backtest Visualizer Launcher
# This script sets up the environment and launches the Streamlit app

echo "Arithmax Backtest Visualizer Launcher"
echo "===================================="

# Check if Python is installed
if ! command -v python3 &> /dev/null; then
    echo "❌ Python 3 is not installed. Please install Python 3 first."
    exit 1
fi

# Check if pip is installed
if ! command -v pip3 &> /dev/null; then
    echo "❌ pip3 is not installed. Please install pip3 first."
    exit 1
fi

# Install requirements
echo "Installing requirements..."
pip install -r requirements.txt

# Launch Streamlit app
echo "🎯 Launching Backtest Visualizer..."
echo "The app will open in your default web browser."
echo "If it doesn't open automatically, navigate to: http://localhost:8501"
echo ""
echo "To stop the app, press Ctrl+C in this terminal."
echo ""

streamlit run backtest_visualizer.py --server.headless=false --server.port=8501
