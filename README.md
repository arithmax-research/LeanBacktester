# LeanBacktester
A comprehensive command-line backtesting platform that integrates with QuantConnect's LEAN engine, featuring automated data acquisition from Alpaca Markets (US equities) and Binance (cryptocurrencies) with seamless conversion to LEAN format.

## Overview

AlgoForge solves the costly data problem in algorithmic trading by providing:
- Free market data acquisition from Alpaca and Binance
- Automatic conversion to QuantConnect LEAN format
- Command-line backtesting interface
- Interactive visualization of backtest results
- Sample trading strategies in both Python and C#

## Features

### Data Pipeline
- **Multiple Data Sources**: Download from Alpaca (US equities), Binance (cryptocurrencies), Polygon (premium market data), and Databento (institutional-grade data)
- **LEAN Format Conversion**: Automatic conversion to QuantConnect's CSV format with proper compression
- **Multiple Resolutions**: Support for tick, minute, hour, and daily data frequencies
- **Rate Limiting**: Built-in API rate limiting to prevent throttling
- **Data Validation**: Comprehensive OHLCV data integrity checks
- **Timezone Handling**: Proper timezone conversion for different markets

### Backtesting Platform
- **LEAN Integration**: Full compatibility with QuantConnect's LEAN engine
- **Multi-Language Support**: Algorithms in Python and C#
- **Sample Strategies**: Pre-built strategies including diversified leverage portfolios
- **Command-Line Interface**: Easy-to-use CLI for running backtests

### Visualization
- **Interactive Charts**: TradingView-style charts for backtest analysis
- **Performance Metrics**: Comprehensive performance statistics and analysis
- **Multi-Strategy Comparison**: Compare multiple strategies side-by-side
- **Streamlit Interface**: Web-based visualization dashboard

## Installation

### Prerequisites
- Python 3.8 or higher
- Git
- .NET 6.0 SDK or higher
- Docker (for LEAN Engine)
- QuantConnect LEAN CLI (required for backtesting)

### LEAN CLI Setup

**1. Install LEAN CLI**:

For macOS:
```bash
# Install using pip
pip install lean

# Or using Homebrew
brew install quantconnect/lean/lean
```

For Linux:
```bash
pip install lean
```

For Windows:
```bash
pip install lean
```

**2. Login to QuantConnect** (Optional but recommended):
```bash
# Login to access cloud features and data
lean login

# Follow the prompts to enter your QuantConnect credentials
# If you don't have an account, create one at https://www.quantconnect.com/
```

**3. Initialize LEAN Configuration**:
```bash
# Navigate to your AlgoForge directory
cd /path/to/AlgoForge

# Initialize LEAN configuration
lean init

# This creates the lean.json configuration file
```

**4. Verify LEAN Installation**:
```bash
lean --version
lean doctor  # Diagnoses common setup issues
```

### Quick Setup

1. **Clone the repository**:
   ```bash
   git clone https://github.com/FranklineMisango/AlgoForge.git
   cd AlgoForge
   ```

2. **Set up LEAN CLI** (follow LEAN CLI Setup above)

3. **Set up the data pipeline**:
   ```bash
   cd data_pipeline
   chmod +x setup.sh
   ./setup.sh
   ```

4. **Configure API keys** (create a `.env` file in the data_pipeline directory):
   ```bash
   # Alpaca API keys (required for equity data)
   ALPACA_API_KEY=your_alpaca_api_key
   ALPACA_SECRET_KEY=your_alpaca_secret_key
   
   # Binance API keys (optional for public crypto data)
   BINANCE_API_KEY=your_binance_api_key
   BINANCE_SECRET_KEY=your_binance_secret_key
   
   # Polygon API key (premium market data)
   POLYGON_API_KEY=your_polygon_api_key
   
   # Databento API key (institutional-grade data)
   DATABENTO_API_KEY=your_databento_api_key
   ```

5. **Test the setup**:
   ```bash
   python test_setup.py
   ```

## Getting API Keys

### Alpaca Markets (For US Equity Data)
1. Visit [Alpaca Markets](https://alpaca.markets/)
2. Create a free account
3. Navigate to your dashboard and generate API keys
4. Use paper trading keys for testing purposes

### Binance (For Cryptocurrency Data)
1. Visit [Binance](https://www.binance.com/)
2. Create an account
3. Generate API keys in your account settings
4. Note: Public data can be accessed without API keys

### Polygon (For Premium Market Data)
1. Visit [Polygon.io](https://polygon.io/)
2. Sign up for an account (free tier available)
3. Navigate to your dashboard to get your API key
4. Supports stocks, options, forex, and crypto data
5. Free tier: 5 API calls per minute, paid plans for higher limits

### Databento (For Institutional-Grade Data)
1. Visit [Databento](https://databento.com/)
2. Create an account (requires approval for institutional use)
3. Generate API credentials in your account settings
4. Provides tick-level data for equities, futures, and options
5. Subscription-based pricing for professional data feeds

## Usage

### Data Download

**Download equity data from Alpaca**:
```bash
cd data_pipeline
python main.py --source alpaca --equity-symbols AAPL GOOGL MSFT --resolution daily
```

**Download cryptocurrency data from Binance**:
```bash
python main.py --source binance --crypto-symbols BTCUSDT ETHUSDT --resolution minute
```

**Download from Polygon**:
```bash
python main.py --source polygon --equity-symbols AAPL TSLA --resolution minute
```

**Download from Databento**:
```bash
python main.py --source databento --equity-symbols SPY QQQ --resolution tick
```

**Download from multiple sources**:
```bash
python main.py --source all --start-date 2023-01-01 --end-date 2023-12-31
```

### LEAN CLI Usage

**Initialize a new algorithm project**:
```bash
# Create a new algorithm project
lean create-project --language python "MyAlgorithm"
# or for C#
lean create-project --language csharp "MyAlgorithm"
```

**Download sample data** (free datasets):
```bash
# Download sample equity data
lean data download --dataset "US Equity Security Master"

# Download sample crypto data (if available)
lean data download --dataset "Crypto Price Data"
```

**Backtest an algorithm**:
```bash
# Backtest a Python algorithm
cd Sample_Strategies/Python_Algorithms/DiversifiedLeverage
lean backtest

# Backtest a C# algorithm
cd Sample_Strategies/C#_Algorithms/DiversifiedLeverage
lean backtest
```

**Live trading setup** (requires QuantConnect subscription):
```bash
# Deploy algorithm for live trading
lean live deploy
```

**Research environment**:
```bash
# Start Jupyter research environment
lean research
```

### Running Sample Strategies

**Python Strategies**:

*Diversified Leverage Strategy*:
```bash
cd Sample_Strategies/Python_Algorithms/DiversifiedLeverage
lean backtest
# Optional: specify custom config
lean backtest --config custom-config.json
```

*Bollinger Bands Mean Reversion Strategy*:
```bash
cd Sample_Strategies/Python_Algorithms/BollingerBandsMeanReversion
lean backtest
# Uses Bollinger Bands and RSI for mean reversion signals
```

*Cryptocurrency Momentum Strategy*:
```bash
cd Sample_Strategies/Python_Algorithms/CryptoMomentum
lean backtest
# Multi-indicator momentum system for cryptocurrencies
```

**C# Strategies**:

*Diversified Leverage Strategy*:
```bash
cd Sample_Strategies/C#_Algorithms/DiversifiedLeverage
lean backtest
```

*Momentum Mean Reversion Strategy*:
```bash
cd Sample_Strategies/C#_Algorithms/MomentumMeanReversion
lean backtest
# Combines momentum and mean reversion signals
```

*Pairs Trading Strategy*:
```bash
cd Sample_Strategies/C#_Algorithms/PairsTrading
lean backtest
# Statistical arbitrage using correlated asset pairs
```

*Sector Rotation Strategy*:
```bash
cd Sample_Strategies/C#_Algorithms/SectorRotation
lean backtest
# Tactical asset allocation rotating between sector ETFs
```

*Market Making Strategy*:
```bash
cd Sample_Strategies/C#_Algorithms/MarketMaking
lean backtest
# Liquidity provision strategy that captures bid-ask spreads
```

### Advanced LEAN Commands

**Custom data integration**:
```bash
# Use your downloaded data with LEAN
lean backtest --data-folder ./data
```

**Optimization**:
```bash
# Run parameter optimization
lean optimize --target "Sharpe Ratio" --target-direction max
```

**Cloud backtesting** (requires QuantConnect subscription):
```bash
# Run backtest in the cloud with more data
lean cloud backtest
```

**Results analysis**:
```bash
# Generate detailed backtest report
lean report
```

### Visualization

**Launch the interactive visualizer**:
```bash
chmod +x launch_visualizer.sh
./launch_visualizer.sh
```

This will start a Streamlit web application at `http://localhost:8501` where you can:
- Upload and analyze backtest results
- View interactive charts and performance metrics
- Compare multiple strategies
- Export analysis reports

## Complete Workflow Guide

### 1. Environment Setup
```bash
# Clone and setup AlgoForge
git clone https://github.com/FranklineMisango/AlgoForge.git
cd AlgoForge

# Install and configure LEAN CLI
pip install lean
lean login  # Optional but recommended
lean init

# Setup data pipeline
cd data_pipeline
./setup.sh
```

### 2. Data Acquisition
```bash
# Configure API keys in data_pipeline/.env
# Download market data
python main.py --source both --start-date 2020-01-01 --end-date 2024-12-31

# Verify data format for LEAN
ls -la data/  # Check downloaded data
```

### 3. Algorithm Development
```bash
# Start with a sample strategy
cd Sample_Strategies/Python_Algorithms/DiversifiedLeverage

# Or create a new algorithm
lean create-project --language python "MyStrategy"
cd MyStrategy
```

### 4. Backtesting and Analysis
```bash
# Run backtest
lean backtest --verbose

# Generate detailed report
lean report

# Visualize results
cd ../../../
./launch_visualizer.sh
```

### 5. Optimization and Refinement
```bash
# Optimize parameters
lean optimize --target "Sharpe Ratio" --target-direction max

# Test with different datasets
lean backtest --start 20180101 --end 20201231  # Bear market period
lean backtest --start 20200301 --end 20211231  # Bull market period
```

### 6. Production Deployment (Optional)
```bash
# Deploy for paper trading first
lean live deploy --environment "paper"

# Deploy for live trading (requires broker integration)
lean live deploy --environment "live"
```

## Project Structure

```
AlgoForge/
├── README.md                          # This file
├── LICENSE                           # Project license
├── backtest_visualizer.py           # Interactive visualization tool
├── launch_visualizer.sh             # Visualizer launcher script
├── data_pipeline/                   # Data acquisition and processing
│   ├── main.py                     # Main pipeline script
│   ├── alpaca_downloader.py        # Alpaca data downloader
│   ├── binance_downloader.py       # Binance data downloader
│   ├── polygon_downloader.py       # Polygon data downloader
│   ├── databento_downloader.py     # Databento data downloader
│   ├── config.py                   # Configuration settings
│   ├── setup.sh                    # Automated setup script
│   └── requirements.txt            # Python dependencies
└── Sample_Strategies/              # Example trading strategies
    ├── Python_Algorithms/          # Python-based strategies
    │   ├── DiversifiedLeverage/    # Diversified leverage ETF strategy
    │   ├── BollingerBandsMeanReversion/  # Bollinger Bands mean reversion strategy
    │   └── CryptoMomentum/         # Cryptocurrency momentum trading strategy
    └── C#_Algorithms/              # C#-based strategies
        ├── DiversifiedLeverage/    # Diversified leverage ETF strategy
        ├── MomentumMeanReversion/  # Combined momentum and mean reversion strategy
        ├── PairsTrading/           # Statistical arbitrage pairs trading strategy
        ├── SectorRotation/         # Tactical asset allocation sector rotation strategy
        └── MarketMaking/           # Liquidity provision market making strategy
```

## Configuration

### Data Pipeline Configuration

Edit `data_pipeline/config.py` to customize:
- Default symbols to download
- Date ranges
- Data resolutions
- Output paths
- API rate limits

### LEAN Configuration

**lean.json**: Main LEAN configuration file created by `lean init`
```json
{
    "algorithm-type-name": "BasicTemplateAlgorithm",
    "algorithm-language": "Python",
    "algorithm-location": "main.py",
    "data-folder": "./data",
    "debugging": false,
    "debugging-method": "PTVSD",
    "log-handler": "ConsoleLogHandler",
    "messaging-handler": "QueueHandler",
    "job-queue-handler": "JobQueue",
    "api-handler": "LocalDiskApiHandler",
    "map-file-provider": "LocalDiskMapFileProvider",
    "factor-file-provider": "LocalDiskFactorFileProvider",
    "data-provider": "DefaultDataProvider",
    "alpha-handler": "DefaultAlphaHandler",
    "object-store": "LocalObjectStore",
    "data-channel-provider": "DataChannelProvider"
}
```

**config.json**: Algorithm-specific configuration for each strategy
```json
{
    "algorithm-language": "Python",
    "parameters": {
        "custom-parameter": "value"
    },
    "description": "Strategy description",
    "local-id": 123456789
}
```

### Strategy Configuration

Each sample strategy includes a `config.json` file where you can modify:

**Common Parameters**:
- Portfolio weights and rebalancing frequency
- Risk management parameters (stop-loss, take-profit)
- Technical indicator periods and thresholds
- Position sizing and exposure limits
- Backtesting date ranges

**Strategy-Specific Examples**:

*Diversified Leverage*: Portfolio weights, rebalancing periods
*Momentum Mean Reversion*: RSI periods, moving average lengths, volatility thresholds
*Pairs Trading*: Correlation thresholds, z-score entry/exit levels, lookback periods
*Sector Rotation*: Momentum periods, sector allocation limits, market regime parameters
*Bollinger Bands*: Band periods, standard deviations, volume confirmation settings
*Crypto Momentum*: MACD parameters, momentum thresholds, trailing stop percentages
*Market Making*: Spread targets, inventory limits, quote refresh intervals, volatility adjustments

### Environment Variables

**Data Pipeline (.env)**:
```bash
# Alpaca API credentials
ALPACA_API_KEY=your_key_here
ALPACA_SECRET_KEY=your_secret_here

# Binance API credentials
BINANCE_API_KEY=your_key_here
BINANCE_SECRET_KEY=your_secret_here

# Polygon API credentials
POLYGON_API_KEY=your_key_here

# Databento API credentials
DATABENTO_API_KEY=your_key_here
```

**LEAN Environment**:
```bash
# QuantConnect credentials (optional)
QC_USER_ID=your_user_id
QC_API_TOKEN=your_api_token

# Custom data paths
LEAN_DATA_FOLDER=/path/to/data
LEAN_RESULTS_FOLDER=/path/to/results
```

## Data Formats

The pipeline converts data to LEAN's standard format:
- **Equity data**: `YYYYMMDD HH:mm,open,high,low,close,volume`
- **Crypto data**: `YYYYMMDD HH:mm,open,high,low,close,volume`
- **Tick data**: `YYYYMMDD HH:mm:ss.fff,price,size` (from Polygon/Databento)
- **Options data**: `YYYYMMDD HH:mm,open,high,low,close,volume,open_interest`
- **Compression**: Automatic ZIP compression for storage efficiency
- **Timezone**: UTC for consistency across markets

### Command Line Options

### Data Pipeline (`main.py`)

```bash
python main.py [options]

Options:
  --source {alpaca,binance,polygon,databento,all}  Data source selection
  --equity-symbols SYMBOL [SYMBOL ...]             Equity symbols to download
  --crypto-symbols SYMBOL [SYMBOL ...]             Crypto symbols to download
  --start-date YYYY-MM-DD                          Start date for data download
  --end-date YYYY-MM-DD                            End date for data download
  --resolution {tick,minute,hour,daily}            Data resolution
  --test                                           Run with test data
```

### LEAN CLI Commands

**Project Management**:
```bash
lean create-project --language python "ProjectName"  # Create new project
lean create-project --language csharp "ProjectName"  # Create C# project
lean delete-project "ProjectName"                    # Delete project
```

**Data Management**:
```bash
lean data download                                    # Download sample data
lean data download --dataset "dataset-name"          # Download specific dataset
lean data clear-cache                                # Clear data cache
lean data list                                       # List available datasets
```

**Backtesting**:
```bash
lean backtest                                        # Run backtest
lean backtest --verbose                              # Verbose logging
lean backtest --config config.json                  # Custom config
lean backtest --start 20200101 --end 20231231       # Custom date range
lean backtest --output results.json                 # Save results
```

**Optimization**:
```bash
lean optimize                                        # Run optimization
lean optimize --target "Sharpe Ratio"               # Optimize specific metric
lean optimize --config optimization.json            # Custom optimization config
```

**Research**:
```bash
lean research                                        # Start Jupyter research environment
lean research --port 8888                           # Custom port
```

**Live Trading** (requires subscription):
```bash
lean live deploy                                     # Deploy for live trading
lean live stop                                       # Stop live trading
lean live status                                     # Check status
```

**Cloud Features** (requires subscription):
```bash
lean cloud backtest                                  # Cloud backtest
lean cloud optimize                                  # Cloud optimization
lean cloud live deploy                               # Cloud live trading
```

**Configuration**:
```bash
lean config list                                     # Show current config
lean config set key value                           # Set config value
lean config get key                                  # Get config value
lean init                                           # Initialize configuration
lean login                                          # Login to QuantConnect
lean logout                                         # Logout
```

**Utilities**:
```bash
lean --version                                       # Show version
lean --help                                         # Show help
lean doctor                                         # Diagnose issues
lean logs                                           # Show logs
lean report                                         # Generate report from results
```

## Troubleshooting

### Common Issues

1. **API Key Errors**: Ensure your API keys are correctly set in the `.env` file
2. **Rate Limiting**: If you encounter rate limits, increase the delay in `config.py`
3. **Data Format Issues**: Verify that downloaded data matches LEAN's expected format
4. **Permission Errors**: Make sure scripts have execute permissions (`chmod +x`)
5. **LEAN CLI Issues**: Run `lean doctor` to diagnose common setup problems
6. **Docker Issues**: Ensure Docker is running for LEAN backtesting
7. **.NET Issues**: Verify .NET 6.0 SDK is installed for C# algorithms

### LEAN-Specific Troubleshooting

**LEAN not found**:
```bash
# Reinstall LEAN CLI
pip uninstall lean
pip install lean

# Verify installation
lean --version
```

**Docker issues**:
```bash
# Check Docker status
docker --version
docker ps

# Pull LEAN Docker image manually if needed
docker pull quantconnect/lean:latest
```

**Configuration issues**:
```bash
# Reset LEAN configuration
lean init --reset

# Check current configuration
lean config list
```

**Data issues**:
```bash
# Clear LEAN cache
lean data clear-cache

# Re-download sample data
lean data download
```

**Backtest failures**:
```bash
# Run with verbose logging
lean backtest --verbose

# Check logs
lean logs
```

### Getting Help

1. Check the detailed documentation in `data_pipeline/README.md`
2. Review the setup guide in `data_pipeline/SETUP_GUIDE.md`
3. Run the test setup script to verify your configuration
4. Check the sample strategies for implementation examples
5. Visit the [QuantConnect Documentation](https://www.quantconnect.com/docs/) for LEAN-specific help
6. Use `lean --help` for command-specific documentation

## Contributing

Contributions are welcome! Please feel free to submit pull requests, report bugs, or suggest new features.

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is licensed under the terms specified in the LICENSE file.

## Disclaimer

This software is for educational and research purposes only. Past performance does not guarantee future results. Always test strategies thoroughly before using real capital.

## Acknowledgments

- QuantConnect for the LEAN algorithmic trading engine
- Alpaca Markets for providing free equity data API
- Binance for cryptocurrency market data
- The open-source community for various dependencies and tools
