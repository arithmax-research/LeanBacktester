import os
import asyncio
from semantic_kernel import Kernel
from semantic_kernel.connectors.ai.ollama import OllamaChatCompletion
from semantic_kernel.memory import SemanticTextMemory
from semantic_kernel.memory.volatile_memory_store import VolatileMemoryStore
from semantic_kernel.core_plugins import TextMemoryPlugin
from semantic_kernel.functions import KernelFunctionFromPrompt
from PyPDF2 import PdfReader
import chromadb
from chromadb.config import Settings

# Initialize ChromaDB
chroma_client = chromadb.PersistentClient(path="./chroma_db")
collection = chroma_client.get_or_create_collection(name="quantconnect_docs")

# Extract and chunk PDF
def extract_pdf_chunks(pdf_path, chunk_size=1000):
    reader = PdfReader(pdf_path)
    chunks = []
    for page in reader.pages:
        text = page.extract_text()
        for i in range(0, len(text), chunk_size):
            chunks.append(text[i:i+chunk_size])
    return chunks

# Setup RAG memory
async def setup_memory(kernel):
    pdf_chunks = extract_pdf_chunks("/home/misango/codechest/LeanBacktester/Quant-Connect-documentation.pdf")
    
    # Add chunks to ChromaDB
    for i, chunk in enumerate(pdf_chunks):
        collection.add(
            documents=[chunk],
            metadatas=[{"source": "quantconnect_docs", "chunk_id": i}],
            ids=[f"chunk_{i}"]
        )
    
    # Create semantic memory
    memory_store = VolatileMemoryStore()
    memory = SemanticTextMemory(memory_store, embeddings_generator=None)  # We'll use Chroma for retrieval
    
    kernel.add_plugin(TextMemoryPlugin(memory), "memory")
    return memory

# Create agents
async def create_agents(kernel):
    # Code Generation Agent
    code_gen_prompt = """
    You are an expert QuantConnect C# developer. Generate complete, compilable C# code for the given strategy description.
    Use the provided context from QuantConnect documentation.
    
    Strategy: {{$input}}
    Context: {{$context}}
    
    Return only the raw C# code.
    """
    code_gen_function = KernelFunctionFromPrompt(code_gen_prompt)
    
    # Debugging Agent
    debug_prompt = """
    Fix compilation errors in this C# code. Apply minimal changes to resolve the errors.
    
    Code: {{$code}}
    Errors: {{$errors}}
    Context: {{$context}}
    
    Return the corrected code.
    """
    debug_function = KernelFunctionFromPrompt(debug_prompt)
    
    # Optimization Agent
    optimize_prompt = """
    Optimize this C# code for performance and best practices.
    
    Code: {{$code}}
    Context: {{$context}}
    
    Return the optimized code.
    """
    optimize_function = KernelFunctionFromPrompt(optimize_prompt)
    
    return {
        "code_gen": code_gen_function,
        "debug": debug_function,
        "optimize": optimize_function
    }

# RAG retrieval
def retrieve_context(query, n_results=5):
    results = collection.query(query_texts=[query], n_results=n_results)
    return " ".join(results['documents'][0])

# Main RAG agent
async def rag_agent(strategy_text):
    # Setup kernel
    kernel = Kernel()
    kernel.add_service(OllamaChatCompletion(service_id="ollama-deepseek", ai_model_id="deepseek-coder:6.7b"))
    
    # Setup memory
    memory = await setup_memory(kernel)
    
    # Create agents
    agents = await create_agents(kernel)
    
    # Retrieve context
    context = retrieve_context(strategy_text)
    
    # Generate initial code
    result = await kernel.invoke(agents["code_gen"], input=strategy_text, context=context)
    code = str(result)
    
    # Clean code
    code = clean_code_response(code)
    
    # Compile and debug if needed
    # (Add compilation logic here)
    
    # Optimize
    result = await kernel.invoke(agents["optimize"], code=code, context=context)
    optimized_code = str(result)
    
    return optimized_code

# Helper function (reuse from existing)
def clean_code_response(code):
    # Same as before
    if "```csharp" in code:
        start = code.find("```csharp") + 9
        end = code.find("```", start)
        if end > start:
            code = code[start:end].strip()
    elif "```" in code:
        start = code.find("```") + 3
        end = code.find("```", start)
        if end > start:
            code = code[start:end].strip()
    code = code.replace("`", "")
    lines = code.split('\n')
    cleaned_lines = [line.strip() for line in lines if line.strip() and not line.startswith('```')]
    return '\n'.join(cleaned_lines)

if __name__ == "__main__":
    import sys
    strategy_file = sys.argv[1]
    with open(strategy_file, 'r') as f:
        strategy_text = f.read()
    
    result = asyncio.run(rag_agent(strategy_text))
    print(result)