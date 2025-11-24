#!/usr/bin/env python3
"""
RAGENTIC System for Implementing Trading Strategies
Uses DeepSeek to generate C# or Python QuantConnect/LEAN algorithms from text descriptions.
"""

import os
import sys
import json
import argparse
import subprocess
from pathlib import Path
from deep_seek_coder import (
    generate_strategy_code, 
    generate_python_strategy_code,
    get_python_example_code,
    generate_data_requirements_summary, 
    fix_compilation_errors
)

try:
    from tqdm import tqdm
    HAS_TQDM = True
except ImportError:
    HAS_TQDM = False

def read_strategy_file(file_path):
    """Read the strategy description from .txt file."""
    with open(file_path, 'r') as f:
        return f.read()

def get_example_code():
    """Get example code from existing strategy for reference."""
    example_path = Path(__file__).parent / "arithmax-strategies" / "EMA_Cross_over" / "Main.cs"
    if example_path.exists():
        with open(example_path, 'r') as f:
            return f.read()[:1000]  # First 1000 chars as example
    return ""

def create_strategy_folder(strategy_name):
    """Create a new C# strategy folder with proper structure."""
    strategy_path = Path(__file__).parent / "arithmax-strategies" / strategy_name
    strategy_path.mkdir(parents=True, exist_ok=True)
    
    (strategy_path / "bin").mkdir(exist_ok=True)
    (strategy_path / "obj").mkdir(exist_ok=True)
    
    csproj_content = f'''<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
        <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
        <TargetFramework>net9.0</TargetFramework>
        <OutputPath>bin/$(Configuration)</OutputPath>
        <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
        <DefaultItemExcludes>$(DefaultItemExcludes);backtests/*/code/**;live/*/code/**;optimizations/*/code/**</DefaultItemExcludes>
        <NoWarn>CS0618</NoWarn>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="QuantConnect.Lean" Version="2.5.*"/>
        <PackageReference Include="QuantConnect.DataSource.Libraries" Version="2.5.*"/>
    </ItemGroup>
</Project>'''
    
    csproj_path = strategy_path / f"{strategy_name}.csproj"
    with open(csproj_path, 'w') as f:
        f.write(csproj_content)
    
    return strategy_path

def create_python_strategy_folder(strategy_name):
    """Create a new Python strategy folder with LEAN structure."""
    strategy_path = Path(__file__).parent / "arithmax-strategies" / strategy_name
    strategy_path.mkdir(parents=True, exist_ok=True)
    
    return strategy_path

def generate_config_json(strategy_name, description, language='csharp'):
    """Generate config.json for the strategy."""
    config = {
        "algorithm-language": "Python" if language == 'python' else "CSharp",
        "parameters": {},
        "description": description,
        "local-id": hash(strategy_name) % 1000000000  # Simple hash for ID
    }
    return config

def main(strategy_txt_path, language='csharp'):
    SUPPORTED_LANGUAGES = ['python', 'csharp']
    if language not in SUPPORTED_LANGUAGES:
        print(f"Error: Unsupported language '{language}'")
        print(f"Supported languages: {', '.join(SUPPORTED_LANGUAGES)}")
        sys.exit(1)
    
    strategy_text = read_strategy_file(strategy_txt_path)

    file_stem = Path(strategy_txt_path).stem
    lines = strategy_text.split('\n')[:10]  # First few lines
    strategy_name = None
    for line in lines:
        line = line.strip()
        print(f"Checking line: {repr(line)}")
        if line and not line.startswith('//') and not line.startswith('-') and 'Strategy' in line and len(line) < 100:
            part = line.split('Strategy')[0].strip()
            print(f"Part before Strategy: {repr(part)}")
            strategy_name = part.replace(' ', '').replace('-', '').replace('(', '').replace(')', '') + 'Strategy'
            print(f"Generated name: {strategy_name}")
            break
    
    if not strategy_name:
        strategy_name = ''.join(word.capitalize() for word in file_stem.split('_')) + 'Strategy'

    print(f"Final Strategy name: {strategy_name}")

    if language == 'python':
        example_code = get_python_example_code()
        print("Generating Python code using DeepSeek...")
    else:
        example_code = get_example_code()
        print("Generating C# code using DeepSeek...")

    if HAS_TQDM:
        with tqdm(total=100, desc="Generating code") as pbar:
            pbar.update(10)  # Start
            if language == 'python':
                generated_code = generate_python_strategy_code(strategy_text, example_code)
            else:
                generated_code = generate_strategy_code(strategy_text, example_code)
            pbar.update(70)  # Code generated
            data_requirements = generate_data_requirements_summary(generated_code, strategy_text, language)
            pbar.update(20)  # Summary complete
    else:
        if language == 'python':
            generated_code = generate_python_strategy_code(strategy_text, example_code)
        else:
            generated_code = generate_strategy_code(strategy_text, example_code)
        data_requirements = generate_data_requirements_summary(generated_code, strategy_text, language)

    print("\nData Requirements Summary:")
    print(data_requirements)
    print()

    if language == 'python':
        strategy_path = create_python_strategy_folder(strategy_name)
    else:
        strategy_path = create_strategy_folder(strategy_name)

    if language == 'python':
        main_file_path = strategy_path / "main.py"
        with open(main_file_path, 'w') as f:
            f.write(generated_code)
        print(f"Created {main_file_path}")
    else:
        main_file_path = strategy_path / "Main.cs"
        with open(main_file_path, 'w') as f:
            f.write(generated_code)
        
        try:
            format_result = subprocess.run(["dotnet", "format", str(strategy_path)], 
                                         capture_output=True, text=True, timeout=30)
            if format_result.returncode != 0:
                print(f"Warning: Code formatting failed: {format_result.stderr}")
        except (subprocess.TimeoutExpired, FileNotFoundError):
            print("Warning: dotnet format not available or timed out")

    description = f"Generated strategy based on {Path(strategy_txt_path).name}"
    config = generate_config_json(strategy_name, description, language)
    config_path = strategy_path / "config.json"
    with open(config_path, 'w') as f:
        json.dump(config, f, indent=4)

    if language == 'python':
        research_path = strategy_path / "research.ipynb"
    else:
        research_path = strategy_path / "Research.ipynb"
    
    if not research_path.exists():
        with open(research_path, 'w') as f:
            f.write('{"cells": [], "metadata": {}, "nbformat": 4, "nbformat_minor": 2}')

    if language == 'csharp':
        print("Compiling and debugging the generated C# code...")
        max_compilation_attempts = 0
        compilation_successful = False
        
        for compilation_attempt in range(max_compilation_attempts):
            print(f"Compilation attempt {compilation_attempt + 1}/{max_compilation_attempts}...")
            
            try:
                result = subprocess.run(["dotnet", "build"], cwd=strategy_path, 
                                      capture_output=True, text=True, timeout=60)
                
                if result.returncode == 0:
                    print("Compilation successful!")
                    compilation_successful = True
                    break
                else:
                    print("Compilation failed with errors:")
                    error_output = (result.stdout + result.stderr).strip()
                    print(error_output[-2000:])  # Last 2000 chars of combined output
                    
                    if compilation_attempt < max_compilation_attempts - 1:
                        print(f"\nSending errors to DeepSeek for debugging...")
                        
                        fixed_code = fix_compilation_errors(generated_code, error_output, strategy_text)
                        
                        if fixed_code and not fixed_code.startswith("Error"):
                            generated_code = fixed_code
                            with open(main_file_path, 'w') as f:
                                f.write(fixed_code)
                            print("Code updated with fixes. Retrying compilation...")
                        else:
                            print("Failed to get fixes from DeepSeek. Code may need manual review.")
                            break
                    else:
                        print("Maximum compilation attempts reached. Code may need manual review.")
                        
            except subprocess.TimeoutExpired:
                print(f"Compilation attempt {compilation_attempt + 1} timed out after 60 seconds")
            except FileNotFoundError:
                print("dotnet not found - skipping compilation check")
                break
            except Exception as e:
                print(f"Compilation attempt {compilation_attempt + 1} failed: {e}")
                break

        print(f"Strategy '{strategy_name}' created successfully in {strategy_path}")
        print(f"Files created/updated: Main.cs, config.json, Research.ipynb")
        if not compilation_successful:
            print("Note: The generated code may require manual review to fix compilation errors")
    else:
        print("Validating Python syntax...")
        try:
            import py_compile
            py_compile.compile(str(main_file_path), doraise=True)
            print("✓ Python syntax validation passed")
        except py_compile.PyCompileError as e:
            print(f"⚠ Python syntax validation failed: {e}")
            print("Note: The code may still work, but please review for syntax errors")
        except Exception as e:
            print(f"⚠ Could not validate Python syntax: {e}")
        
        print(f"\nStrategy '{strategy_name}' created successfully in {strategy_path}")
        print(f"Files created: main.py, config.json, research.ipynb")
        print("Note: Run 'lean backtest' in the strategy folder to test the strategy.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description='Generate QuantConnect/LEAN trading strategies from text descriptions'
    )
    parser.add_argument(
        'strategy_file',
        type=str,
        help='Path to the strategy description text file'
    )
    parser.add_argument(
        '--lang',
        type=str,
        choices=['python', 'csharp'],
        default='csharp',
        help='Target language for strategy generation (default: csharp)'
    )
    
    args = parser.parse_args()
    
    if not os.path.exists(args.strategy_file):
        print(f"Error: File {args.strategy_file} not found")
        sys.exit(1)

    main(args.strategy_file, args.lang)
