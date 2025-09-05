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
import { Line } from 'react-chartjs-2';

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend
);

const EquityChart = ({ data }) => {
  if (!data || data.length === 0) {
    return <div className="chart-placeholder">No equity data available</div>;
  }

  // Process data for Chart.js
  const labels = data.map(point => new Date(point.timestamp).toLocaleDateString());
  const values = data.map(point => point.value);

  // Calculate returns and drawdown
  const returns = [];
  const drawdown = [];

  for (let i = 0; i < values.length; i++) {
    if (i === 0) {
      returns.push(0);
      drawdown.push(0);
    } else {
      const ret = (values[i] - values[i-1]) / values[i-1] * 100;
      returns.push(ret);

      const peak = Math.max(...values.slice(0, i+1));
      const dd = (values[i] - peak) / peak * 100;
      drawdown.push(dd);
    }
  }

  const chartData = {
    labels,
    datasets: [
      {
        label: 'Portfolio Value',
        data: values,
        borderColor: '#00D4AA',
        backgroundColor: 'rgba(0, 212, 170, 0.1)',
        yAxisID: 'y',
      },
      {
        label: 'Daily Returns (%)',
        data: returns,
        borderColor: '#FF6B6B',
        backgroundColor: 'rgba(255, 107, 107, 0.1)',
        yAxisID: 'y1',
        hidden: true, // Hidden by default
      },
      {
        label: 'Drawdown (%)',
        data: drawdown,
        borderColor: '#FF4757',
        backgroundColor: 'rgba(255, 71, 87, 0.2)',
        fill: true,
        yAxisID: 'y1',
        hidden: true, // Hidden by default
      },
    ],
  };

  const options = {
    responsive: true,
    interaction: {
      mode: 'index',
      intersect: false,
    },
    stacked: false,
    plugins: {
      title: {
        display: true,
        text: 'Portfolio Performance Analysis',
      },
      legend: {
        display: true,
      },
    },
    scales: {
      y: {
        type: 'linear',
        display: true,
        position: 'left',
        title: {
          display: true,
          text: 'Portfolio Value ($)',
        },
      },
      y1: {
        type: 'linear',
        display: true,
        position: 'right',
        title: {
          display: true,
          text: 'Returns/Drawdown (%)',
        },
        grid: {
          drawOnChartArea: false,
        },
      },
    },
  };

  return (
    <div className="chart-container">
      <h2>Portfolio Performance</h2>
      <Line data={chartData} options={options} />
    </div>
  );
};

export default EquityChart;
