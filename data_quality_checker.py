#!/usr/bin/env python3
"""
Data Quality Checker RAG Agent
Uses DeepSeek to analyze downloaded data quality and identify issues.
"""

import os
import json
import pandas as pd
from pathlib import Path
from deep_seek_coder import DeepSeekCoder
from data_pipeline.env_loader import load_env_file

# Load environment variables
load_env_file()

def analyze_data_file(file_path):
    """
    Analyze a single data file and return basic statistics.
    """
    try:
        if file_path.suffix.lower() == '.csv':
            df = pd.read_csv(file_path)
        elif file_path.suffix.lower() == '.parquet':
            df = pd.read_parquet(file_path)
        elif file_path.suffix.lower() in ['.h5', '.hdf5']:
            df = pd.read_hdf(file_path)
        else:
            return f"Unsupported file format: {file_path.suffix}"

        # Basic statistics
        stats = {
            'file_path': str(file_path),
            'rows': len(df),
            'columns': list(df.columns),
            'dtypes': df.dtypes.astype(str).to_dict(),
            'null_counts': df.isnull().sum().to_dict(),
            'date_range': None,
            'numeric_stats': {}
        }

        # Try to identify date column and get range
        date_cols = [col for col in df.columns if 'date' in col.lower() or 'time' in col.lower()]
        if date_cols:
            date_col = date_cols[0]
            try:
                df[date_col] = pd.to_datetime(df[date_col])
                stats['date_range'] = {
                    'start': df[date_col].min().strftime('%Y-%m-%d'),
                    'end': df[date_col].max().strftime('%Y-%m-%d')
                }
            except:
                pass

        # Numeric column statistics
        numeric_cols = df.select_dtypes(include=['number']).columns
        for col in numeric_cols[:5]:  # Limit to first 5 numeric columns
            stats['numeric_stats'][col] = {
                'min': float(df[col].min()),
                'max': float(df[col].max()),
                'mean': float(df[col].mean()),
                'std': float(df[col].std()),
                'zeros': int((df[col] == 0).sum())
            }

        return stats

    except Exception as e:
        return f"Error analyzing {file_path}: {str(e)}"

def check_data_quality_with_deepseek(data_stats, data_sample=""):
    """
    Use DeepSeek to analyze data quality based on statistics.
    """
    coder = DeepSeekCoder()
    prompt = f"""
Analyze the following data file statistics and provide a quality assessment:

Data Statistics:
{json.dumps(data_stats, indent=2)}

Data Sample (first 10 rows):
{data_sample}

Please provide:
1. Overall quality score (1-10)
2. Identified issues (missing data, outliers, inconsistencies, etc.)
3. Recommendations for data cleaning or improvement
4. Suitability for backtesting/trading

Be specific about any problems found and suggest fixes.
"""

    try:
        return coder.generate_code(prompt)
    except Exception as e:
        return f"Error in quality analysis: {e}"

def find_data_files(data_dir="data"):
    """
    Find all data files in the data directory.
    """
    data_path = Path(data_dir)
    if not data_path.exists():
        return []

    extensions = ['.csv', '.parquet', '.h5', '.hdf5', '.pkl']
    files = []
    for ext in extensions:
        files.extend(data_path.rglob(f'*{ext}'))

    return files

def main():
    print("Data Quality Checker RAG Agent")
    print("=" * 50)

    # Find data files
    data_files = find_data_files()
    if not data_files:
        print("No data files found in 'data' directory")
        return

    print(f"Found {len(data_files)} data files to analyze")

    for file_path in data_files:
        print(f"\nAnalyzing: {file_path}")
        print("-" * 40)

        # Analyze file
        stats = analyze_data_file(file_path)
        if isinstance(stats, str):  # Error message
            print(f"{stats}")
            continue

        # Get data sample
        try:
            if file_path.suffix.lower() == '.csv':
                sample = pd.read_csv(file_path, nrows=10).to_string()
            else:
                sample = "Sample not available for this format"
        except:
            sample = "Could not load sample"

        # Quality check with DeepSeek
        print("Analyzing quality with DeepSeek...")
        quality_report = check_data_quality_with_deepseek(stats, sample)

        print("\nQuality Report:")
        print(quality_report)

if __name__ == "__main__":
    main()