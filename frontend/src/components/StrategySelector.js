import React from 'react';

const StrategySelector = ({
  strategies,
  selectedStrategy,
  selectedRun,
  onStrategyChange,
  onRunChange
}) => {
  const strategyNames = Object.keys(strategies);

  return (
    <div className="strategy-selector">
      <h3>Strategy Selection</h3>

      <div className="form-group">
        <label htmlFor="strategy-select">Select Strategy:</label>
        <select
          id="strategy-select"
          value={selectedStrategy}
          onChange={(e) => onStrategyChange(e.target.value)}
        >
          <option value="">Choose a strategy...</option>
          {strategyNames.map(strategy => (
            <option key={strategy} value={strategy}>
              {strategy} ({strategies[strategy].length} runs)
            </option>
          ))}
        </select>
      </div>

      {selectedStrategy && (
        <div className="form-group">
          <label htmlFor="run-select">Select Backtest Run:</label>
          <select
            id="run-select"
            value={selectedRun}
            onChange={(e) => onRunChange(e.target.value)}
          >
            <option value="">Choose a run...</option>
            {strategies[selectedStrategy]?.map(run => (
              <option key={run} value={run}>
                {run}
              </option>
            ))}
          </select>
        </div>
      )}

      <div className="strategy-info">
        <h4>Available Strategies:</h4>
        <ul>
          {strategyNames.map(strategy => (
            <li key={strategy}>
              {strategy}: {strategies[strategy].length} runs
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
};

export default StrategySelector;
