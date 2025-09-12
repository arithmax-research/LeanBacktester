#!/bin/bash

# Lean Backtest Visualizer Launcher (React + Flask)
# This script sets up and launches the React frontend and Flask backend

echo "Arithmax Backtest Visualizer Launcher"
echo "===================================="
echo "🎯 Launching React + Flask Backtest Visualizer..."
echo "The app will open in your default web browser."
echo "Frontend: http://localhost:3000"
echo "Backend API: http://localhost:5000"
echo ""
echo "To stop the app, press Ctrl+C in each terminal."
echo ""

# Check if Python virtual environment exists
if [ ! -d "venv" ]; then
    echo "Creating Python virtual environment..."
    python3 -m venv venv
fi

# Activate virtual environment and install dependencies
echo "Activating virtual environment and installing Python dependencies..."
source venv/bin/activate
pip install -r requirements.txt

# Start Flask backend in background
echo "Starting Flask backend..."
python backend.py &
BACKEND_PID=$!

# Wait a moment for backend to start
sleep 3

# Start React frontend
echo "Starting React frontend..."
cd frontend
npm start &
FRONTEND_PID=$!

# Wait for user to stop
echo ""
echo "Both services are running. Press Ctrl+C to stop."

# Function to cleanup processes
cleanup() {
    echo ""
    echo "Stopping services..."
    kill $BACKEND_PID 2>/dev/null
    kill $FRONTEND_PID 2>/dev/null
    exit 0
}

# Set trap for cleanup
trap cleanup SIGINT SIGTERM

# Wait for processes
wait
