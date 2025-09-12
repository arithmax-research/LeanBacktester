import React, { useState, useEffect } from 'react';
import axios from 'axios';
import './App.css';
import StrategySelector from './components/StrategySelector';
import PerformanceMetrics from './components/PerformanceMetrics';
import EquityChart from './components/EquityChart';
import TradesChart from './components/TradesChart';

function App() {
  const [strategies, setStrategies] = useState({});
  const [selectedStrategy, setSelectedStrategy] = useState('');
  const [selectedRun, setSelectedRun] = useState('');
  const [backtestData, setBacktestData] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    fetchStrategies();
  }, []);

  const fetchStrategies = async () => {
    try {
      const response = await axios.get('http://localhost:5000/api/strategies');
      setStrategies(response.data);
    } catch (err) {
      setError('Failed to fetch strategies');
      console.error(err);
    }
  };

  const fetchBacktestData = async (strategy, run) => {
    setLoading(true);
    setError(null);
    try {
      const response = await axios.get(`http://localhost:5000/api/backtest/${strategy}/${run}`);
      setBacktestData(response.data);
    } catch (err) {
      setError('Failed to fetch backtest data');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const handleStrategyChange = (strategy) => {
    setSelectedStrategy(strategy);
    setSelectedRun('');
    setBacktestData(null);
  };

  const handleRunChange = (run) => {
    setSelectedRun(run);
    fetchBacktestData(selectedStrategy, run);
  };

  return (
    <div className="App">
      <header className="App-header">
        <h1>Arithmax Backtest Visualizer</h1>
        <p>Interactive visualization tool for Arithmax backtest results</p>
      </header>

      <div className="container">
        <aside className="sidebar">
          <StrategySelector
            strategies={strategies}
            selectedStrategy={selectedStrategy}
            selectedRun={selectedRun}
            onStrategyChange={handleStrategyChange}
            onRunChange={handleRunChange}
          />
        </aside>

        <main className="main-content">
          {error && <div className="error">{error}</div>}

          {loading && <div className="loading">Loading backtest data...</div>}

          {backtestData && (
            <>
              <PerformanceMetrics metrics={backtestData.metrics} />

              <div className="charts-container">
                <EquityChart data={backtestData.equityCurve} />
                <TradesChart data={backtestData.trades} />
              </div>
            </>
          )}
        </main>
      </div>
    </div>
  );
}

export default App;
