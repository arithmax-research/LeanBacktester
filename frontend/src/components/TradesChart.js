import React from 'react';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
} from 'chart.js';
import { Scatter } from 'react-chartjs-2';

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend
);

const TradesChart = ({ data }) => {
  if (!data || data.length === 0) {
    return <div className="chart-placeholder">No trade data available</div>;
  }

  // Separate buy and sell trades
  const buyTrades = data.filter(trade => trade.direction === 'buy');
  const sellTrades = data.filter(trade => trade.direction === 'sell');

  const chartData = {
    datasets: [
      {
        label: 'Buy Orders',
        data: buyTrades.map(trade => ({
          x: new Date(trade.timestamp).toLocaleDateString(),
          y: trade.price,
        })),
        backgroundColor: '#00D4AA',
        pointStyle: 'triangle',
        pointRadius: 8,
        rotation: 0,
      },
      {
        label: 'Sell Orders',
        data: sellTrades.map(trade => ({
          x: new Date(trade.timestamp).toLocaleDateString(),
          y: trade.price,
        })),
        backgroundColor: '#FF6B6B',
        pointStyle: 'triangle',
        pointRadius: 8,
        rotation: 180,
      },
    ],
  };

  const options = {
    responsive: true,
    plugins: {
      title: {
        display: true,
        text: 'Trade Signals',
      },
      legend: {
        display: true,
      },
      tooltip: {
        callbacks: {
          label: function(context) {
            const trade = data[context.dataIndex];
            return [
              `${trade.direction.toUpperCase()} ${trade.symbol}`,
              `Price: $${trade.price.toFixed(2)}`,
              `Quantity: ${trade.quantity}`,
              `Value: $${trade.value.toLocaleString()}`,
            ];
          },
        },
      },
    },
    scales: {
      x: {
        title: {
          display: true,
          text: 'Date',
        },
      },
      y: {
        title: {
          display: true,
          text: 'Price ($)',
        },
      },
    },
  };

  return (
    <div className="chart-container">
      <h2>Trade Signals</h2>
      <Scatter data={chartData} options={options} />
    </div>
  );
};

export default TradesChart;
