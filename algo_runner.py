#!/usr/bin/env python3
"""
Arithmax Algorithm Runner - Interactive CLI tool for running backtests
Automatically handles data downloading and algorithm execution
"""

import os
import sys
import subprocess
import json
import re
import zipfile
from datetime import datetime, timedelta
from pathlib import Path
from typing import Dict, List, Tuple, Optional
import argparse

class AlgorithmRunner:
    def __init__(self):
        self.base_dir = Path(__file__).parent
        self.arithmax_dir = self.base_dir / "arithmax-strategies"
        self.data_dir = self.base_dir / "data"
        self.data_pipeline_dir = self.base_dir / "data_pipeline"
        
    def print_banner(self):
        """Print welcome banner"""
        print("=" * 60)
        print("Leanbacktester Terminal Runner")
        print("   Automated Backtesting Pipeline")
        print("=" * 60)
        print()

    def discover_algorithms(self) -> Dict[str, List[Dict]]:
        """Discover all available algorithms"""
        algorithms = {"python": [], "csharp": []}
        
        # Python algorithms
        python_dir = self.arithmax_dir / "Python_Algorithms"
        if python_dir.exists():
            for algo_dir in python_dir.iterdir():
                if algo_dir.is_dir() and not algo_dir.name.startswith('.'):
                    main_file = algo_dir / "main.py"
                    if main_file.exists():
                        algorithms["python"].append({
                            "name": algo_dir.name,
                            "path": str(algo_dir),
                            "main_file": str(main_file),
                            "type": "python"
                        })
        
        # C# algorithms
        csharp_dir = self.arithmax_dir / "csharp_Algorithms"
        if csharp_dir.exists():
            for algo_dir in csharp_dir.iterdir():
                if algo_dir.is_dir() and not algo_dir.name.startswith('.'):
                    # Look for .csproj files
                    csproj_files = list(algo_dir.glob("*.csproj"))
                    cs_files = list(algo_dir.glob("*.cs"))
                    
                    if csproj_files and cs_files:
                        # Try to find the main algorithm class
                        main_class = self.find_main_algorithm_class(cs_files)
                        algorithms["csharp"].append({
                            "name": algo_dir.name,
                            "path": str(algo_dir),
                            "csproj": str(csproj_files[0]),
                            "main_class": main_class,
                            "type": "csharp"
                        })
        
        return algorithms

    def find_main_algorithm_class(self, cs_files: List[Path]) -> Optional[str]:
        """Find the main algorithm class in C# files"""
        for cs_file in cs_files:
            try:
                content = cs_file.read_text()
                # Look for class that inherits from QCAlgorithm
                match = re.search(r'class\s+(\w+)\s*:\s*QCAlgorithm', content)
                if match:
                    # Try to find namespace (handle dotted namespaces)
                    namespace_match = re.search(r'namespace\s+([\w\.]+)', content)
                    namespace = namespace_match.group(1) if namespace_match else None
                    class_name = match.group(1)
                    
                    if namespace:
                        return f"{namespace}.{class_name}"
                    else:
                        return class_name
            except Exception:
                continue
        return None

    def display_algorithms(self, algorithms: Dict[str, List[Dict]]) -> List[Dict]:
        """Display available algorithms and return flat list"""
        all_algorithms = []
        index = 1
        
        print("Available Algorithms:")
        print()
        
        if algorithms["python"]:
            print("Python Algorithms:")
            for algo in algorithms["python"]:
                print(f"  {index:2d}. {algo['name']} (Python)")
                all_algorithms.append(algo)
                index += 1
            print()
        
        if algorithms["csharp"]:
            print("C# Algorithms:")
            for algo in algorithms["csharp"]:
                print(f"  {index:2d}. {algo['name']} (C#)")
                all_algorithms.append(algo)
                index += 1
            print()
        
        return all_algorithms

    def get_algorithm_symbols(self, algorithm: Dict) -> List[str]:
        """Extract symbols used by an algorithm"""
        symbols = []
        
        if algorithm["type"] == "python":
            try:
                content = Path(algorithm["main_file"]).read_text()
                # Look for AddEquity, AddCrypto, etc. with various quote styles
                patterns = [
                    r'AddEquity\(["\']([^"\']+)["\']',
                    r'AddCrypto\(["\']([^"\']+)["\']',
                    r'AddForex\(["\']([^"\']+)["\']',
                    r'AddOption\(["\']([^"\']+)["\']',
                    r'AddFuture\(["\']([^"\']+)["\']',
                    r'self\.AddEquity\(["\']([^"\']+)["\']',
                    r'self\.AddCrypto\(["\']([^"\']+)["\']'
                ]
                
                for pattern in patterns:
                    matches = re.findall(pattern, content, re.IGNORECASE)
                    symbols.extend(matches)
                    
            except Exception as e:
                print(f"Debug: Error reading Python file: {e}")
        
        elif algorithm["type"] == "csharp":
            try:
                cs_files = list(Path(algorithm["path"]).glob("*.cs"))
                for cs_file in cs_files:
                    content = cs_file.read_text()
                    # Look for AddEquity and other patterns
                    patterns = [
                        r'AddEquity\(["\']([^"\']+)["\']',
                        r'AddCrypto\(["\']([^"\']+)["\']',
                        r'AddForex\(["\']([^"\']+)["\']',
                        r'AddOption\(["\']([^"\']+)["\']',
                        r'AddFuture\(["\']([^"\']+)["\']',
                        # Also look for symbols in dictionaries/lists
                        r'["\']([A-Z]{1,5})["\'].*//.*(?:Leveraged|ETF|Stock)',
                        r'\{["\']([A-Z]{1,5})["\'],',
                        # Look for direct symbol assignments
                        r'_symbols.*["\']([A-Z]{1,5})["\']'
                    ]
                    
                    for pattern in patterns:
                        matches = re.findall(pattern, content, re.IGNORECASE)
                        symbols.extend(matches)
                        
            except Exception as e:
                print(f"Debug: Error reading C# files: {e}")
        
        # Remove duplicates and filter out obviously non-symbol matches
        unique_symbols = []
        for symbol in set(symbols):
            # Filter for valid stock symbols (1-5 uppercase letters)
            if re.match(r'^[A-Z]{1,5}$', symbol.upper()):
                unique_symbols.append(symbol.upper())
        
        return sorted(unique_symbols)

    def analyze_data_coverage(self, symbols: List[str]) -> Dict:
        """Analyze actual date coverage for each symbol"""
        import zipfile
        from datetime import datetime
        
        coverage = {}
        
        for symbol in symbols:
            symbol_lower = symbol.lower()
            coverage[symbol] = {
                'available': False,
                'start_date': None,
                'end_date': None,
                'total_days': 0,
                'file_path': None,
                'file_type': None
            }
            
            # Check daily zip files first (most common)
            daily_zip = self.data_dir / "equity" / "usa" / "daily" / f"{symbol_lower}.zip"
            if daily_zip.exists():
                try:
                    with zipfile.ZipFile(daily_zip, 'r') as zf:
                        files = [f for f in zf.namelist() if f.endswith('.csv')]
                        if files:
                            # Get date range from filename patterns or content
                            dates = []
                            for file in files[:5]:  # Sample first few files
                                try:
                                    with zf.open(file) as csv_file:
                                        lines = csv_file.read().decode('utf-8').split('\n')
                                        for line in lines[1:6]:  # Skip header, check first few data lines
                                            if line.strip():
                                                parts = line.split(',')
                                                if len(parts) > 0:
                                                    # Parse date (format: YYYYMMDD)
                                                    date_str = parts[0]
                                                    if len(date_str) >= 8:
                                                        date = datetime.strptime(date_str[:8], '%Y%m%d')
                                                        dates.append(date)
                                except:
                                    continue
                            
                            if dates:
                                coverage[symbol].update({
                                    'available': True,
                                    'start_date': min(dates),
                                    'end_date': max(dates),
                                    'total_days': len(dates),
                                    'file_path': str(daily_zip),
                                    'file_type': 'daily_zip'
                                })
                except Exception as e:
                    print(f"Warning: Could not analyze {daily_zip}: {e}")
            
            # Check minute folders if daily zip not found
            if not coverage[symbol]['available']:
                minute_path = self.data_dir / "equity" / "usa" / "minute" / symbol_lower
                if minute_path.exists():
                    coverage[symbol].update({
                        'available': True,
                        'file_path': str(minute_path),
                        'file_type': 'minute_folder'
                    })
        
        return coverage

    def display_data_coverage(self, symbols: List[str], coverage: Dict):
        """Display data coverage information to user"""
        print("\n" + "="*60)
        print("📊 Data Coverage Analysis")
        print("="*60)
        
        available_symbols = []
        missing_symbols = []
        date_ranges = []
        
        for symbol in symbols:
            info = coverage[symbol]
            if info['available']:
                available_symbols.append(symbol)
                if info['start_date'] and info['end_date']:
                    start_str = info['start_date'].strftime('%Y-%m-%d')
                    end_str = info['end_date'].strftime('%Y-%m-%d')
                    days = info['total_days']
                    print(f"✅ {symbol:6} | {start_str} to {end_str} | {days:,} days | {info['file_type']}")
                    date_ranges.append((info['start_date'], info['end_date']))
                else:
                    print(f"✅ {symbol:6} | Available but date range unknown | {info['file_type']}")
            else:
                missing_symbols.append(symbol)
                print(f"❌ {symbol:6} | No data found")
        
        # Calculate optimal date range
        if date_ranges:
            overall_start = max(start for start, end in date_ranges)  # Latest start date
            overall_end = min(end for start, end in date_ranges)      # Earliest end date
            
            if overall_start <= overall_end:
                print(f"\n🎯 Recommended backtest range (data available for ALL symbols):")
                print(f"   From: {overall_start.strftime('%Y-%m-%d')}")
                print(f"   To:   {overall_end.strftime('%Y-%m-%d')}")
                print(f"   Duration: {(overall_end - overall_start).days} days")
                
                return {
                    'available': available_symbols,
                    'missing': missing_symbols,
                    'suggested_start': overall_start.strftime('%Y-%m-%d'),
                    'suggested_end': overall_end.strftime('%Y-%m-%d'),
                    'coverage': coverage
                }
        
        return {
            'available': available_symbols,
            'missing': missing_symbols,
            'suggested_start': None,
            'suggested_end': None,
            'coverage': coverage
        }

    def check_data_availability(self, symbols: List[str]) -> Tuple[List[str], List[str]]:
        """Check which symbols have data available"""
        available = []
        missing = []
        
        for symbol in symbols:
            # Check in equity data folders (both minute folders and daily zip files)
            equity_minute_path = self.data_dir / "equity" / "usa" / "minute" / symbol.lower()
            equity_daily_zip = self.data_dir / "equity" / "usa" / "daily" / f"{symbol.lower()}.zip"
            crypto_path = self.data_dir / "crypto" / "binance" / "minute" / symbol.lower()
            
            if equity_minute_path.exists() or equity_daily_zip.exists() or crypto_path.exists():
                available.append(symbol)
            else:
                missing.append(symbol)
        
        return available, missing

    def prompt_data_download(self, missing_symbols: List[str]) -> bool:
        """Prompt user to download missing data"""
        print(f" Missing data for symbols: {', '.join(missing_symbols)}")
        print()
        
        response = input("Would you like to download the missing data? [Y/n]: ").strip().lower()
        return response in ['', 'y', 'yes']

    def get_download_parameters(self, symbols: List[str]) -> Dict:
        """Get download parameters from user"""
        print(" Data Download Configuration:")
        print()
        
        # Default date range (last 2 years)
        end_date = datetime.now()
        start_date = end_date - timedelta(days=730)
        
        print(f"Symbols to download: {', '.join(symbols)}")
        print(f"Default date range: {start_date.strftime('%Y-%m-%d')} to {end_date.strftime('%Y-%m-%d')}")
        print()
        
        # Ask if user wants to customize
        customize = input("Customize date range? [y/N]: ").strip().lower()
        
        if customize in ['y', 'yes']:
            while True:
                try:
                    start_str = input(f"Start date (YYYY-MM-DD) [{start_date.strftime('%Y-%m-%d')}]: ").strip()
                    if start_str:
                        start_date = datetime.strptime(start_str, '%Y-%m-%d')
                    break
                except ValueError:
                    print("Invalid date format. Please use YYYY-MM-DD")
            
            while True:
                try:
                    end_str = input(f"End date (YYYY-MM-DD) [{end_date.strftime('%Y-%m-%d')}]: ").strip()
                    if end_str:
                        end_date = datetime.strptime(end_str, '%Y-%m-%d')
                    break
                except ValueError:
                    print("Invalid date format. Please use YYYY-MM-DD")
        
        resolution = input("Resolution (minute/hour/daily) [minute]: ").strip().lower()
        if not resolution:
            resolution = "minute"
        
        return {
            "symbols": symbols,
            "start_date": start_date.strftime('%Y-%m-%d'),
            "end_date": end_date.strftime('%Y-%m-%d'),
            "resolution": resolution
        }

    def download_data(self, params: Dict) -> bool:
        """Download data using the data pipeline"""
        print()
        print("Downloading data...")
        
        # Construct data pipeline command
        cmd = [
            sys.executable,
            str(self.data_pipeline_dir / "main.py"),
            "--source", "alpaca",  # Default to equity data
            "--equity-symbols"] + params["symbols"] + [
            "--start-date", params["start_date"],
            "--end-date", params["end_date"],
            "--resolution", params["resolution"]
        ]
        
        try:
            # Change to data pipeline directory
            original_cwd = os.getcwd()
            os.chdir(self.data_pipeline_dir)
            
            result = subprocess.run(cmd, capture_output=True, text=True)
            
            os.chdir(original_cwd)
            
            if result.returncode == 0:
                print(" Data download completed successfully!")
                return True
            else:
                print(f" Data download failed: {result.stderr}")
                return False
                
        except Exception as e:
            print(f" Error running data pipeline: {e}")
            return False

    def run_python_algorithm(self, algorithm: Dict, backtest_params: Dict = None) -> bool:
        """Run Python algorithm using Lean CLI"""
        print(f"🐍 Running Python algorithm: {algorithm['name']}")
        
        # Use relative path from base directory
        algo_path = f"arithmax-strategies/Python_Algorithms/{algorithm['name']}"
        
        cmd = ["lean", "backtest", algo_path]
        
        # Add custom config if parameters provided
        if backtest_params:
            config_path = self.create_custom_config(backtest_params)
            cmd.extend(["--config", config_path])
            print(f"Using custom configuration: {config_path}")
        
        try:
            original_cwd = os.getcwd()
            os.chdir(self.base_dir)
            
            print(f"Executing: {' '.join(cmd)}")
            result = subprocess.run(cmd, capture_output=False)
            
            os.chdir(original_cwd)
            
            return result.returncode == 0
            
        except Exception as e:
            print(f" Error running Python algorithm: {e}")
            return False

    def run_csharp_algorithm(self, algorithm: Dict, backtest_params: Dict = None) -> bool:
        """Run C# algorithm using Docker method"""
        print(f"Running C# algorithm: {algorithm['name']}")
        
        # First, build the project
        print("Building C# project...")
        try:
            result = subprocess.run(
                ["dotnet", "build"],
                cwd=algorithm["path"],
                capture_output=True,
                text=True
            )
            
            if result.returncode != 0:
                print(f"Build failed: {result.stderr}")
                return False
            
            print("Build successful")
            
        except Exception as e:
            print(f"Build error: {e}")
            return False
        
        # Run using Docker method
        algo_name = algorithm["name"]
        dll_path = f"{algo_name}/bin/Debug/{algo_name}.dll"
        
        if not algorithm["main_class"]:
            print(" Could not determine main algorithm class")
            return False
        
        # Use the run_backtest.sh script
        script_path = self.base_dir / "run_backtest.sh"
        
        # Check if the script exists
        if not script_path.exists():
            print(f" run_backtest.sh not found at {script_path}")
            return False
        
        # Prepare command arguments
        cmd = [str(script_path), algorithm["main_class"], str(algorithm["path"])]
        
        # Add custom config if parameters provided
        if backtest_params:
            config_path = self.create_custom_config(backtest_params)
            print(f"Using custom configuration: {config_path}")
            # Note: The config file approach would need modification to run_backtest.sh
            # For now, we'll use the standard approach since Lean CLI params didn't work
        
        try:
            original_cwd = os.getcwd()
            os.chdir(self.base_dir)
            
            print(f"Executing: {' '.join(cmd)}")
            result = subprocess.run(cmd, capture_output=False)
            
            os.chdir(original_cwd)
            
            return result.returncode == 0
            
        except Exception as e:
            print(f" Error running C# algorithm: {e}")
            return False

    def create_custom_config(self, backtest_params: Dict) -> str:
        """Create custom Lean configuration with backtest parameters"""
        # Read base configuration
        base_config_path = self.base_dir / "lean.json"
        if not base_config_path.exists():
            print(f"Warning: Base config {base_config_path} not found, using minimal config")
            config = {
                "environment": "backtesting",
                "algorithm-type-name": "BasicTemplateAlgorithm",
                "algorithm-language": "Python",
                "algorithm-location": ".",
                "data-folder": "./data",
                "debugging": False,
                "debugging-method": "LocalCmdline",
                "job-queue-handler": "QuantConnect.Queues.JobQueue",
                "api-handler": "QuantConnect.Api.Api",
                "messaging-handler": "QuantConnect.Messaging.Messaging",
                "transaction-handler": "QuantConnect.Lean.Engine.TransactionHandlers.BacktestingTransactionHandler"
            }
        else:
            with open(base_config_path, 'r') as f:
                config = json.load(f)
        
        # Override with backtest parameters
        config.update({
            "start-date": backtest_params['start_date'],
            "end-date": backtest_params['end_date'],
            "cash": backtest_params['initial_cash']
        })
        
        # Save custom configuration
        custom_config_path = self.base_dir / "lean_custom.json"
        with open(custom_config_path, 'w') as f:
            json.dump(config, f, indent=2)
        
        return str(custom_config_path)

    def get_backtest_parameters(self, symbols: List[str] = None) -> Dict:
        """Get backtest parameters from user input with data coverage analysis"""
        print("\n" + "="*50)
        print("🎯 Backtest Parameter Configuration")
        print("="*50)
        
        # Analyze data coverage if symbols provided
        coverage_info = None
        if symbols:
            print("\n🔍 Analyzing data coverage for selected symbols...")
            coverage = self.analyze_data_coverage(symbols)
            coverage_info = self.display_data_coverage(symbols, coverage)
            
            # Handle missing data
            if coverage_info['missing']:
                print(f"\n⚠️  Missing data for: {', '.join(coverage_info['missing'])}")
                if input("Would you like to continue anyway? (y/n): ").lower() != 'y':
                    return None
        
        params = {}
        
        # Get start date with suggestions
        while True:
            if coverage_info and coverage_info['suggested_start']:
                default_start = coverage_info['suggested_start']
                prompt = f"\n📅 Enter start date (YYYY-MM-DD) [suggested: {default_start}]: "
            else:
                default_start = "2020-01-01"
                prompt = f"\n📅 Enter start date (YYYY-MM-DD) [default: {default_start}]: "
            
            start_input = input(prompt).strip()
            if not start_input:
                start_input = default_start
            
            try:
                datetime.strptime(start_input, '%Y-%m-%d')
                params['start_date'] = start_input
                break
            except ValueError:
                print("❌ Invalid date format. Please use YYYY-MM-DD")
        
        # Get end date with suggestions
        while True:
            if coverage_info and coverage_info['suggested_end']:
                default_end = coverage_info['suggested_end']
                prompt = f"📅 Enter end date (YYYY-MM-DD) [suggested: {default_end}]: "
            else:
                default_end = "2023-12-31"
                prompt = f"📅 Enter end date (YYYY-MM-DD) [default: {default_end}]: "
            
            end_input = input(prompt).strip()
            if not end_input:
                end_input = default_end
            
            try:
                end_date = datetime.strptime(end_input, '%Y-%m-%d')
                start_date = datetime.strptime(params['start_date'], '%Y-%m-%d')
                
                if end_date <= start_date:
                    print("❌ End date must be after start date")
                    continue
                    
                params['end_date'] = end_input
                break
            except ValueError:
                print("❌ Invalid date format. Please use YYYY-MM-DD")
        
        # Get initial cash
        while True:
            cash_input = input("💰 Enter initial cash amount [default: 100000]: ").strip()
            if not cash_input:
                cash_input = "100000"
            
            try:
                cash_amount = float(cash_input)
                if cash_amount <= 0:
                    print("❌ Cash amount must be positive")
                    continue
                params['initial_cash'] = cash_amount
                break
            except ValueError:
                print("❌ Invalid cash amount. Please enter a number")
        
        # Summary
        print(f"\n✅ Backtest Configuration:")
        print(f"   📅 Period: {params['start_date']} to {params['end_date']}")
        print(f"   💰 Initial Cash: ${params['initial_cash']:,.2f}")
        
        # Store coverage info for potential use
        if coverage_info:
            params['coverage_info'] = coverage_info
        
        return params

    def run_algorithm(self, algorithm: Dict, backtest_params: Dict = None) -> bool:
        """Run the selected algorithm with optional backtest parameters"""
        print()
        print("Starting Algorithm Execution")
        print(f"Algorithm: {algorithm['name']} ({algorithm['type'].upper()})")
        
        if backtest_params:
            print(f"Parameters: {backtest_params['start_date']} to {backtest_params['end_date']}, ${backtest_params['initial_cash']:,.2f}")
        print()
        
        if algorithm["type"] == "python":
            return self.run_python_algorithm(algorithm, backtest_params)
        else:
            return self.run_csharp_algorithm(algorithm, backtest_params)

    def interactive_mode(self):
        """Run in interactive mode"""
        self.print_banner()
        
        # Discover algorithms
        print("Discovering algorithms...")
        algorithms = self.discover_algorithms()
        all_algorithms = self.display_algorithms(algorithms)
        
        if not all_algorithms:
            print(" No algorithms found in arithmax-strategies folder!")
            return
        
        # Get user selection
        while True:
            try:
                choice = input(f"Select algorithm (1-{len(all_algorithms)}) or 'q' to quit: ").strip()
                
                if choice.lower() == 'q':
                    print("Goodbye! 👋")
                    return
                
                choice_idx = int(choice) - 1
                if 0 <= choice_idx < len(all_algorithms):
                    selected_algorithm = all_algorithms[choice_idx]
                    break
                else:
                    print(f"Please enter a number between 1 and {len(all_algorithms)}")
                    
            except ValueError:
                print("Please enter a valid number or 'q' to quit")
        
        print()
        print(f"Selected: {selected_algorithm['name']} ({selected_algorithm['type'].upper()})")
        
        # Check data requirements
        print("🔍 Analyzing data requirements...")
        symbols = self.get_algorithm_symbols(selected_algorithm)
        
        if symbols:
            print(f"Required symbols: {', '.join(symbols)}")
            available, missing = self.check_data_availability(symbols)
            
            if available:
                print(f" Available data: {', '.join(available)}")
            
            if missing:
                if self.prompt_data_download(missing):
                    params = self.get_download_parameters(missing)
                    if not self.download_data(params):
                        print(" Data download failed. Cannot proceed.")
                        return
                else:
                    print(" Cannot run algorithm without required data.")
                    return
        else:
            print("  Could not automatically detect required symbols.")
            proceed = input("Continue anyway? [Y/n]: ").strip().lower()
            if proceed not in ['', 'y', 'yes']:
                return
        
        # Get backtest parameters with data coverage analysis
        backtest_params = self.get_backtest_parameters(symbols if symbols else None)
        if backtest_params is None:
            print("Parameter collection cancelled.")
            return
        
        # Run the algorithm with parameters
        success = self.run_algorithm(selected_algorithm, backtest_params)
        
        print()
        if success:
            print("🎉 Algorithm execution completed successfully!")
        else:
            print(" Algorithm execution failed.")

    def list_mode(self):
        """List all available algorithms"""
        algorithms = self.discover_algorithms()
        all_algorithms = self.display_algorithms(algorithms)
        
        print(f"Total: {len(all_algorithms)} algorithms found")
        print(f"  Python: {len(algorithms['python'])}")
        print(f"  C#: {len(algorithms['csharp'])}")

def main():
    parser = argparse.ArgumentParser(description='Arithmax Algorithm Runner')
    parser.add_argument('--list', action='store_true', help='List available algorithms')
    parser.add_argument('--algorithm', type=str, help='Run specific algorithm by name')
    
    args = parser.parse_args()
    
    runner = AlgorithmRunner()
    
    if args.list:
        runner.list_mode()
    elif args.algorithm:
        # TODO: Implement direct algorithm running
        print(f"Direct algorithm running not yet implemented. Use interactive mode.")
        runner.interactive_mode()
    else:
        runner.interactive_mode()

if __name__ == "__main__":
    main()