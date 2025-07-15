"""
Alpaca data downloader for Lean format
"""

import alpaca_trade_api as tradeapi
import pandas as pd
from datetime import datetime, timedelta
import pytz
import time
import os
from typing import List, Dict, Optional
from tqdm import tqdm

from config import (
    ALPACA_API_KEY, ALPACA_SECRET_KEY, ALPACA_BASE_URL,
    EQUITY_DATA_PATH, LEAN_TIMEZONE_EQUITY, LEAN_TIME_FORMAT,
    ALPACA_RATE_LIMIT
)
from utils import (
    setup_logging, ensure_directory_exists, format_lean_date,
    create_lean_tradebar_csv, write_lean_zip_file, get_trading_days,
    DataValidator
)

logger = setup_logging()

class AlpacaDataDownloader:
    """Download equity data from Alpaca and convert to Lean format"""
    
    def __init__(self):
        if not ALPACA_API_KEY or not ALPACA_SECRET_KEY:
            raise ValueError("Alpaca API credentials not found. Please set ALPACA_API_KEY and ALPACA_SECRET_KEY environment variables.")
        
        self.api = tradeapi.REST(
            ALPACA_API_KEY,
            ALPACA_SECRET_KEY,
            ALPACA_BASE_URL,
            api_version='v2'
        )
        
        self.rate_limit_delay = 60 / ALPACA_RATE_LIMIT
        
    def get_bars(self, symbol: str, timeframe: str, start_date: datetime, end_date: datetime) -> List[Dict]:
        """Get bar data from Alpaca"""
        try:
            # Convert timeframe to Alpaca format
            alpaca_timeframe = self._convert_timeframe(timeframe)
            
            # Get data from Alpaca
            bars = self.api.get_bars(
                symbol,
                alpaca_timeframe,
                start=start_date.strftime('%Y-%m-%d'),
                end=end_date.strftime('%Y-%m-%d'),
                adjustment='raw'
            )
            
            # Convert to our format
            data = []
            for bar in bars:
                data.append({
                    'timestamp': bar.t.replace(tzinfo=pytz.timezone(LEAN_TIMEZONE_EQUITY)),
                    'open': float(bar.o),
                    'high': float(bar.h),
                    'low': float(bar.l),
                    'close': float(bar.c),
                    'volume': int(bar.v)
                })
            
            # Rate limiting
            time.sleep(self.rate_limit_delay)
            
            return data
            
        except Exception as e:
            logger.error(f"Error getting bars for {symbol}: {str(e)}")
            return []
    
    def _convert_timeframe(self, timeframe: str) -> str:
        """Convert resolution to Alpaca timeframe"""
        timeframe_map = {
            'minute': '1Min',
            'hour': '1Hour',
            'daily': '1Day'
        }
        
        return timeframe_map.get(timeframe, '1Min')
    
    def download_symbol_data(self, symbol: str, resolution: str, start_date: datetime, end_date: datetime):
        """Download and save data for a single symbol"""
        logger.info(f"Downloading {symbol} data for {resolution} resolution")
        
        if resolution == 'daily' or resolution == 'hour':
            # For daily/hour, save all data in one file
            data = self.get_bars(symbol, resolution, start_date, end_date)
            
            if data:
                # Clean and validate data
                cleaned_data = DataValidator.clean_ohlcv_data(data)
                
                if cleaned_data:
                    output_path = os.path.join(EQUITY_DATA_PATH, resolution, f"{symbol.lower()}.zip")
                    csv_filename = f"{symbol.lower()}_{resolution}_trade.csv"
                    
                    # Group data by date for processing
                    daily_data = {}
                    for bar in cleaned_data:
                        date_key = bar['timestamp'].strftime(LEAN_TIME_FORMAT)
                        if date_key not in daily_data:
                            daily_data[date_key] = []
                        daily_data[date_key].append(bar)
                    
                    # Create CSV content for all dates
                    all_csv_content = []
                    for date_key in sorted(daily_data.keys()):
                        date_bars = daily_data[date_key]
                        csv_content = create_lean_tradebar_csv(date_bars, symbol, date_bars[0]['timestamp'], resolution)
                        all_csv_content.extend(csv_content)
                    
                    if all_csv_content:
                        write_lean_zip_file(all_csv_content, output_path, csv_filename)
                        logger.info(f"Saved {len(all_csv_content)} bars for {symbol} {resolution}")
        
        else:
            # For minute/second, save data by date
            trading_days = get_trading_days(start_date, end_date)
            
            for date in tqdm(trading_days, desc=f"Downloading {symbol} {resolution}"):
                date_start = date.replace(hour=0, minute=0, second=0)
                date_end = date.replace(hour=23, minute=59, second=59)
                
                data = self.get_bars(symbol, resolution, date_start, date_end)
                
                if data:
                    # Clean and validate data
                    cleaned_data = DataValidator.clean_ohlcv_data(data)
                    
                    if cleaned_data:
                        # Create directory structure
                        symbol_dir = os.path.join(EQUITY_DATA_PATH, resolution, symbol.lower())
                        ensure_directory_exists(symbol_dir)
                        
                        # Create file paths
                        date_str = format_lean_date(date)
                        output_path = os.path.join(symbol_dir, f"{date_str}_trade.zip")
                        csv_filename = f"{date_str}_{symbol.lower()}_{resolution}_trade.csv"
                        
                        # Convert to Lean format
                        csv_content = create_lean_tradebar_csv(cleaned_data, symbol, date, resolution)
                        
                        if csv_content:
                            write_lean_zip_file(csv_content, output_path, csv_filename)
                            logger.debug(f"Saved {len(csv_content)} bars for {symbol} on {date_str}")
                
                # Rate limiting
                time.sleep(self.rate_limit_delay)
    
    def download_multiple_symbols(self, symbols: List[str], resolution: str, start_date: datetime, end_date: datetime):
        """Download data for multiple symbols"""
        logger.info(f"Starting download for {len(symbols)} symbols")
        
        for symbol in tqdm(symbols, desc="Downloading symbols"):
            try:
                self.download_symbol_data(symbol, resolution, start_date, end_date)
            except Exception as e:
                logger.error(f"Error downloading {symbol}: {str(e)}")
                continue
        
        logger.info("Download completed")

def main():
    """Main function for testing"""
    from config import DEFAULT_EQUITY_SYMBOLS, DEFAULT_START_DATE, DEFAULT_END_DATE
    
    downloader = AlpacaDataDownloader()
    
    # Test with a small set of symbols
    test_symbols = ['AAPL', 'GOOGL', 'MSFT']
    test_start = datetime.now() - timedelta(days=30)
    test_end = datetime.now()
    
    # Download minute data
    downloader.download_multiple_symbols(test_symbols, 'minute', test_start, test_end)
    
    # Download daily data
    downloader.download_multiple_symbols(test_symbols, 'daily', test_start, test_end)

if __name__ == "__main__":
    main()
