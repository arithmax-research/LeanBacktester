using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run <strategy_file>");
    return;
}

        string strategyFile = args[0];
        string strategyText = File.ReadAllText(strategyFile);

        // Setup Semantic Kernel
        var builder = Kernel.CreateBuilder();
        builder.AddOllamaChatCompletion("deepseek-coder:6.7b", new Uri("http://localhost:11434"));
        builder.AddOllamaEmbeddingGenerator("nomic-embed-text", new Uri("http://localhost:11434"));
        var kernel = builder.Build();

        // Setup RAG Memory
#pragma warning disable SKEXP0050
        string ragContext = "";
        Console.WriteLine("Setting up RAG memory...");
        try
        {
            Console.WriteLine("Creating memory builder...");
            var memoryBuilder = new MemoryBuilder();
            Console.WriteLine("Getting embedding service...");
            // Try to get the embedding service - if it fails, we'll use empty context
            var embeddingService = kernel.Services.GetService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>>();
            if (embeddingService != null)
            {
                Console.WriteLine("Embedding service found, creating wrapper...");
                // Create a wrapper for the new embedding service to work with old memory API
                var textEmbeddingService = new TextEmbeddingGenerationServiceWrapper(embeddingService);
                Console.WriteLine("Setting text embedding generation...");
                memoryBuilder.WithTextEmbeddingGeneration(textEmbeddingService);
                Console.WriteLine("Setting memory store...");
                memoryBuilder.WithMemoryStore(new VolatileMemoryStore());
                Console.WriteLine("Building memory...");
                var memory = memoryBuilder.Build();
                Console.WriteLine("Memory built successfully. Loading PDF...");

                Console.WriteLine("Extracting PDF content...");
                string pdfContent = ExtractPdfContent("../Quant-Connect-documentation.pdf");
                Console.WriteLine($"PDF loaded, {pdfContent.Length} characters. Storing in memory...");

                Console.WriteLine("Storing PDF in memory...");
                await StorePdfInMemory(memory, pdfContent);
                Console.WriteLine("PDF stored. Retrieving context...");

                Console.WriteLine("Retrieving context...");
                ragContext = await RetrieveContext(memory, strategyText);
                Console.WriteLine($"Context retrieved, {ragContext.Length} characters.");
            }
            else
            {
                Console.WriteLine("Embedding service not found, using empty context");
                ragContext = "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RAG setup failed, using empty context: {ex.Message}");
            ragContext = "";
        }
#pragma warning restore SKEXP0050

        // Create agents
        var codeGenFunction = kernel.CreateFunctionFromPrompt(
            "You are an expert QuantConnect C# developer. Generate complete, compilable C# code for the given strategy.\n\nStrategy: {{$input}}\n\nContext: {{$context}}\n\nReturn only the raw C# code.",
            functionName: "GenerateCode"
        );

        var debugFunction = kernel.CreateFunctionFromPrompt(
            "Fix compilation errors in this C# code. Apply minimal changes.\n\nCode: {{$code}}\n\nErrors: {{$errors}}\n\nContext: {{$context}}\n\nReturn the corrected code.",
            functionName: "DebugCode"
        );

        // Generate code
        var result = await kernel.InvokeAsync(codeGenFunction, new() { ["input"] = strategyText, ["context"] = ragContext });
        string code = result.ToString();

        // Clean code
        code = CleanCode(code);

        // Simulate compilation check (in real scenario, run dotnet build)
        // For demo, assume no errors or add mock errors
        string errors = ""; // Replace with actual compilation output

        if (!string.IsNullOrEmpty(errors))
        {
            var debugResult = await kernel.InvokeAsync(debugFunction, new() { ["code"] = code, ["errors"] = errors, ["context"] = ragContext });
            code = debugResult.ToString();
            code = CleanCode(code);
        }

        Console.WriteLine(code);

    static string ExtractPdfContent(string pdfPath)
    {
        Console.WriteLine($"Opening PDF: {pdfPath}");
        var pdfReader = new PdfReader(pdfPath);
        var pdfDoc = new PdfDocument(pdfReader);
        Console.WriteLine($"PDF has {pdfDoc.GetNumberOfPages()} pages");
        var strategy = new SimpleTextExtractionStrategy();
        string text = "";

        // Extract only first 10 pages for testing to avoid hanging
        int pagesToExtract = Math.Min(10, pdfDoc.GetNumberOfPages());
        Console.WriteLine($"Extracting first {pagesToExtract} pages...");

        for (int i = 1; i <= pagesToExtract; i++)
        {
            Console.WriteLine($"Extracting page {i}...");
            var page = pdfDoc.GetPage(i);
            text += PdfTextExtractor.GetTextFromPage(page, strategy);
        }
        pdfDoc.Close();
        Console.WriteLine($"Extracted {text.Length} characters from PDF");
        return text;
    }

    static async Task StorePdfInMemory(ISemanticTextMemory memory, string content)
    {
        var chunks = ChunkText(content, 1000);
        for (int i = 0; i < chunks.Count; i++)
        {
            await memory.SaveInformationAsync("quantconnect", chunks[i], $"chunk_{i}");
        }
    }

    static List<string> ChunkText(string text, int chunkSize)
    {
        var chunks = new List<string>();
        for (int i = 0; i < text.Length; i += chunkSize)
        {
            chunks.Add(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
        }
        return chunks;
    }

    static async Task<string> RetrieveContext(ISemanticTextMemory memory, string query)
    {
        var results = await memory.SearchAsync("quantconnect", query, limit: 5).ToListAsync();
        return string.Join("\n", results.Select(r => r.Metadata.Text));
    }

    static string CleanCode(string code)
    {
        // Remove markdown
        if (code.Contains("```csharp"))
        {
            int start = code.IndexOf("```csharp") + 9;
            int end = code.IndexOf("```", start);
            if (end > start) code = code.Substring(start, end - start);
        }
        // Remove backticks
        code = code.Replace("`", "");
        return code.Trim();
}

class TextEmbeddingGenerationServiceWrapper : ITextEmbeddingGenerationService
{
    private readonly Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public TextEmbeddingGenerationServiceWrapper(Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IList<string> data, CancellationToken cancellationToken = default)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync(data, null, cancellationToken);
        return embeddings.Select(e => new ReadOnlyMemory<float>(e.Vector.ToArray())).ToList();
    }

    // Implement the new interface method
    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IList<string> data, Kernel? kernel, CancellationToken cancellationToken = default)
    {
        return await GenerateEmbeddingsAsync(data, cancellationToken);
    }

    // Implement Attributes property
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();
}
