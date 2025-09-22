import os
import requests
from data_pipeline.env_loader import load_env_file
from dotenv import load_dotenv
load_dotenv()
load_env_file()

# Load QuantConnect documentation excerpts
def get_quantconnect_docs():
    """Extract relevant excerpts from QuantConnect documentation PDF."""
    try:
        from PyPDF2 import PdfReader
        pdf_path = "/home/misango/codechest/LeanBacktester/Quant-Connect-documentation.pdf"
        reader = PdfReader(pdf_path)
        
        # Extract key sections (pages 1-50 for basics, indicators around 200-300, etc.)
        docs = ""
        # Basic algorithm structure (pages 1-10)
        for page in reader.pages[:10]:
            docs += page.extract_text() + "\n"
        # Indicators (approximate pages, adjust as needed)
        for page in reader.pages[190:250]:  # Assuming indicators start around page 200
            docs += page.extract_text() + "\n"
        return docs[:5000]  # Limit to 5000 chars
    except Exception as e:
        return f"Error loading docs: {e}"

QUANTCONNECT_DOCS = get_quantconnect_docs()

class DeepSeekCoder:
    _mode = None  # Class variable to store mode
    
    def __init__(self, mode=None):
        if DeepSeekCoder._mode is None:
            self.mode = mode or os.getenv('DEEP_SEEK_MODE')
            if not self.mode:
                self.mode = input("Choose DeepSeek mode (api/ollama): ").strip().lower()
                if self.mode not in ['api', 'ollama']:
                    raise ValueError("Invalid mode. Choose 'api' or 'ollama'.")
            DeepSeekCoder._mode = self.mode
        else:
            self.mode = DeepSeekCoder._mode
        
        if self.mode == 'api':
            self.api_key = os.getenv('DEEP_SEEK_API')
            if not self.api_key:
                raise ValueError("DEEP_SEEK_API not found in environment variables")
            self.base_url = "https://api.deepseek.com/v1"
        elif self.mode == 'ollama':
            self.base_url = "http://localhost:11434"

    def generate_code(self, prompt, model=None):
        """
        Generate C# code based on the prompt using DeepSeek API or Ollama.
        """
        if self.mode == 'api':
            model = model or "deepseek-chat"
            headers = {
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json"
            }
            data = {
                "model": model,
                "messages": [
                    {"role": "system", "content": "You are an expert C# developer for QuantConnect/LEAN algorithms. Generate complete, runnable C# code based on strategy descriptions. Always provide the full code without truncation. NEVER use markdown code blocks or backticks - return only raw C# code that can be directly compiled."},
                    {"role": "user", "content": prompt}
                ],
                "max_tokens": 4000,
                "temperature": 0.3
            }
            response = requests.post(f"{self.base_url}/chat/completions", headers=headers, json=data, timeout=300)
            if response.status_code == 200:
                result = response.json()
                return result['choices'][0]['message']['content']
            else:
                raise Exception(f"API call failed: {response.status_code} - {response.text}")
        elif self.mode == 'ollama':
            model = model or "deepseek-coder:6.7b"
            data = {
                "model": model,
                "prompt": prompt,
                "stream": False,
                "options": {
                    "temperature": 0.3,
                    "num_predict": 4000
                }
            }
            response = requests.post(f"{self.base_url}/api/generate", json=data, timeout=600)
            if response.status_code == 200:
                result = response.json()
                return result['response']
            else:
                raise Exception(f"Ollama call failed: {response.status_code} - {response.text}")

def generate_strategy_code(strategy_text, example_code=""):
    """
    Generate C# strategy code from text description.
    """
    coder = DeepSeekCoder()
    system_prompt = "You are an expert C# developer for QuantConnect/LEAN algorithms. Generate complete, runnable C# code based on strategy descriptions. Always provide the full code without truncation. NEVER use markdown code blocks or backticks - return only raw C# code that can be directly compiled."
    user_prompt = f"""
Based on the following strategy description, generate a complete QuantConnect/LEAN C# algorithm class.

Strategy Description:
{strategy_text}

Use this format and structure similar to existing strategies. The class should inherit from QCAlgorithm.

Include:
- Initialize method with date ranges, cash, symbols
- Indicators as needed (use correct QuantConnect types: RelativeStrengthIndex for RSI, ExponentialMovingAverage for EMA, AverageTrueRange for ATR, IntradayVwap for VWAP, etc.)
- OnData method for trading logic
- Proper risk management
- Logging

Reference QuantConnect Documentation:
{QUANTCONNECT_DOCS[:1000]}

Important: Use the correct QuantConnect namespaces and types. Include necessary using directives at the top.

Example structure:
{example_code}

IMPORTANT: Return ONLY the raw C# code without any markdown formatting, code blocks, or explanations. Start directly with the using statements and namespace declaration.
"""
    if coder.mode == 'ollama':
        prompt = f"{system_prompt}\n\n{user_prompt}"
    else:
        prompt = user_prompt
    code = coder.generate_code(prompt)
    # Clean up the response - remove markdown code blocks if present
    code = clean_code_response(code)
    return code

def clean_code_response(code):
    """
    Clean up AI response by removing markdown code blocks and extracting just the C# code.
    """
    # Remove markdown code blocks
    if "```csharp" in code:
        # Extract content between ```csharp and ```
        start = code.find("```csharp") + 9
        end = code.find("```", start)
        if end > start:
            code = code[start:end].strip()
    elif "```" in code:
        # Handle generic code blocks
        start = code.find("```") + 3
        end = code.find("```", start)
        if end > start:
            code = code[start:end].strip()

    # Remove any remaining backticks
    code = code.replace("`", "")

    # Ensure it starts with using statements or namespace
    lines = code.split('\n')
    cleaned_lines = []
    for line in lines:
        line = line.strip()
        if line and not line.startswith('```'):
            cleaned_lines.append(line)

    return '\n'.join(cleaned_lines)

def fix_compilation_errors(generated_code, compilation_errors, strategy_text):
    """
    Fix compilation errors in generated C# code using DeepSeek (prefers API for better debugging).
    """
    # Prefer API for debugging if available, fallback to current mode
    try:
        coder = DeepSeekCoder(mode='api')
    except ValueError:
        coder = DeepSeekCoder()  # Fallback to user choice
    
    system_prompt = "You are an expert C# developer for QuantConnect/LEAN algorithms. Fix compilation errors in the provided code and return only the corrected C# code."
    user_prompt = f"""
Apply targeted fixes to the existing C# code based on the compilation errors. Do NOT regenerate the entire code from scratch—only modify the parts that are broken.

Original Strategy Description:
{strategy_text[:500]}

Existing Code with Errors:
{generated_code}

Compilation Errors:
{compilation_errors}

Reference QuantConnect Documentation:
{QUANTCONNECT_DOCS[:500]}

Fix only the specific errors mentioned. Use correct QuantConnect types and namespaces. Return the complete corrected C# code.
"""
    if coder.mode == 'ollama':
        prompt = f"{system_prompt}\n\n{user_prompt}"
    else:
        prompt = user_prompt
    try:
        fixed_code = coder.generate_code(prompt)
        fixed_code = clean_code_response(fixed_code)
        return fixed_code
    except Exception as e:
        return f"Error in fixing compilation: {e}"

def generate_data_requirements_summary(generated_code, strategy_text):
    """
    Generate a summary of data requirements from the generated strategy code (prefers API for accuracy).
    """
    # Prefer API for analysis if available, fallback to current mode
    try:
        coder = DeepSeekCoder(mode='api')
    except ValueError:
        coder = DeepSeekCoder()  # Fallback to user choice
    
    system_prompt = "Analyze the provided C# QuantConnect strategy code and strategy description to provide a concise JSON summary of data requirements."
    user_prompt = f"""
Analyze the following generated C# QuantConnect strategy code and the original strategy description to provide a concise summary of data requirements.

Generated Code:
{generated_code[:2000]}  # First 2000 chars to avoid token limits

Original Strategy Description:
{strategy_text[:1000]}  # First 1000 chars

Provide a JSON-like summary with:
- symbols: List of stock/crypto symbols used
- start_date: Backtest start date (YYYY-MM-DD)
- end_date: Backtest end date (YYYY-MM-DD) 
- resolution: Data resolution (Daily/Minute/Hour)
- data_types: Types of data needed (equity, crypto, options, etc.)
- special_requirements: Any special data needs (earnings dates, volume spikes, etc.)

Format as a simple JSON object.
"""
    if coder.mode == 'ollama':
        prompt = f"{system_prompt}\n\n{user_prompt}"
    else:
        prompt = user_prompt
    try:
        return coder.generate_code(prompt)
    except Exception as e:
        return f"Error generating data requirements: {e}"
