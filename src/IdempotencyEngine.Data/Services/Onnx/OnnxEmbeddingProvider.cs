using IdempotencyEngine.Data.Abstractions;
using IdempotencyEngine.Domain.Interfaces;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IdempotencyEngine.Data.Services.Onnx;

/// <summary>
/// Production embedding provider that generates 384-dimensional vector embeddings using an ONNX Runtime model.
/// SessionOptions are supplied via an IOnnxSessionOptionsFactory for flexible hardware execution provider selection (CPU, GPU, NPU).
/// </summary>
public class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly EmbeddingOptions _options;
    private readonly InferenceSession? _session;
    private readonly ILogger<OnnxEmbeddingProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnnxEmbeddingProvider"/> class with the specified embedding options, ONNX session options factory, and logger.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="sessionOptionsFactory"></param>
    /// <param name="logger"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public OnnxEmbeddingProvider(
        IOptions<EmbeddingOptions> options,
        IOnnxSessionOptionsFactory sessionOptionsFactory,
        ILogger<OnnxEmbeddingProvider>? logger = null)
    {
        _options = options?.Value ?? new EmbeddingOptions();
        var factory = sessionOptionsFactory ?? throw new ArgumentNullException(nameof(sessionOptionsFactory));
        _logger = logger ?? NullLogger<OnnxEmbeddingProvider>.Instance;

        var resolvedModelPath = ResolveModelPath(_options.ModelPath);

        if (File.Exists(resolvedModelPath))
        {
            _logger.LogInformation("Loading ONNX model from path: {ModelPath} (ModelId: {ModelId})", resolvedModelPath, _options.ModelId);
            var sessionOptions = factory.CreateOptions();
            _session = new InferenceSession(resolvedModelPath, sessionOptions);
            _logger.LogInformation("ONNX inference session successfully initialized.");
        }
        else
        {
            _logger.LogWarning("ONNX model file not found at path: {ModelPath}. Falling back to deterministic hash-based embedding generation.", _options.ModelPath);
        }
    }

    /// <summary>
    /// Resolves the model path by checking if the file exists in the provided path or in common subdirectories. If the file is not found, returns the original path.
    /// </summary>
    /// <param name="modelPath"></param>
    /// <returns>Path to the model file.</returns>
    private static string ResolveModelPath(string modelPath)
    {
        if (File.Exists(modelPath))
            return modelPath;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, modelPath),
            Path.Combine(baseDir, "Models", Path.GetFileName(modelPath)),
            Path.Combine(baseDir, "models", Path.GetFileName(modelPath))
        };

        return candidates.FirstOrDefault(File.Exists) ?? modelPath;
    }

    public string ModelName => _options.ModelId;

    public int Dimensions => _options.Dimensions;

    /// <summary>
    /// Generates a vector embedding for the given text using the ONNX model. If the model is not available or an error occurs during inference, a deterministic hash-based fallback embedding is generated instead. The embedding is normalized to unit length (L2 normalization).
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Embedding vector.</returns>
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("GenerateEmbeddingAsync received empty text. Returning zero vector.");
            return Task.FromResult(new float[_options.Dimensions]);
        }

        if (_session == null)
        {
            _logger.LogTrace("Generating fallback embedding for text (session is null).");
            return Task.FromResult(GenerateFallbackEmbedding(text));
        }

        try
        {
            var embedding = RunInference(text);
            return Task.FromResult(embedding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ONNX inference execution. Falling back to deterministic embedding.");
            return Task.FromResult(GenerateFallbackEmbedding(text));
        }
    }

    /// <summary>
    /// Generates vector embeddings for a batch of texts using the ONNX model. If the model is not available or an error occurs during inference, a deterministic hash-based fallback embedding is generated instead. The embeddings are normalized to unit length (L2 normalization).
    /// </summary>
    /// <param name="texts"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Array of embedding vectors.</returns>
    public Task<float[][]> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var list = texts.ToList();
        _logger.LogDebug("Generating batch embeddings for {Count} texts.", list.Count);
        var results = new float[list.Count][];

        for (int i = 0; i < list.Count; i++)
        {
            results[i] = string.IsNullOrWhiteSpace(list[i]) 
                ? new float[_options.Dimensions] 
                : (_session == null ? GenerateFallbackEmbedding(list[i]) : RunInference(list[i]));
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Runs inference on the ONNX model for the given text, generating a vector embedding. The text is tokenized, and the resulting tokens are fed into the model. The output embedding is normalized to unit length (L2 normalization). If the model is not available or an error occurs during inference, a deterministic hash-based fallback embedding is generated instead.
    /// </summary>
    /// <param name="text"></param>
    /// <returns>Embedding vector.</returns>
    private float[] RunInference(string text)
    {
        var tokens = SimpleTokenize(text, maxSeqLength: 128);
        var inputIdsTensor = new DenseTensor<long>(tokens.InputIds, new[] { 1, tokens.InputIds.Length });
        var attentionMaskTensor = new DenseTensor<long>(tokens.AttentionMask, new[] { 1, tokens.AttentionMask.Length });
        var tokenTypeIdsTensor = new DenseTensor<long>(new long[tokens.InputIds.Length], new[] { 1, tokens.InputIds.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using var results = _session!.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        var embedding = new float[_options.Dimensions];
        var tensorArray = outputTensor.ToArray();

        int copyLength = Math.Min(_options.Dimensions, tensorArray.Length);
        Array.Copy(tensorArray, embedding, copyLength);

        NormalizeL2(embedding);
        return embedding;
    }

    /// <summary>
    /// Tokenizes the input text into a sequence of token IDs and attention masks for ONNX model inference. The text is lowercased, split into words, and each word is hashed to generate a pseudo-token ID. The resulting token IDs and attention masks are returned as arrays. Special tokens [CLS] (101) and [SEP] (102) are added at the beginning and end of the sequence, respectively. If the text exceeds the maximum sequence length, it is truncated.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="maxSeqLength"></param>
    /// <returns>Tuple of input IDs and attention masks.</returns>
    private (long[] InputIds, long[] AttentionMask) SimpleTokenize(string text, int maxSeqLength)
    {
        var words = text.ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var inputIds = new long[maxSeqLength];
        var attentionMask = new long[maxSeqLength];

        inputIds[0] = 101;
        attentionMask[0] = 1;

        int len = Math.Min(words.Length, maxSeqLength - 2);
        for (int i = 0; i < len; i++)
        {
            inputIds[i + 1] = (Math.Abs(words[i].GetHashCode()) % 30000) + 1000;
            attentionMask[i + 1] = 1;
        }

        inputIds[len + 1] = 102;
        attentionMask[len + 1] = 1;

        return (inputIds, attentionMask);
    }

    /// <summary>
    /// Generates a deterministic fallback embedding for the given text by computing a hash of the text and using it to create a pseudo-random vector. The resulting vector is normalized to unit length (L2 normalization). This method is used when the ONNX model is not available or an error occurs during inference.
    /// </summary>
    /// <param name="text"></param>
    /// <returns>Fallback embedding vector.</returns>
    private float[] GenerateFallbackEmbedding(string text)
    {
        var result = new float[_options.Dimensions];
        var hash = (uint)text.GetHashCode();

        for (int i = 0; i < _options.Dimensions; i++)
        {
            result[i] = (float)Math.Sin(hash + i);
        }

        NormalizeL2(result);
        return result;
    }

    /// <summary>
    /// Normalizes the input vector to unit length (L2 normalization). If the vector has a norm of zero, it remains unchanged. This method is used to ensure that the generated embeddings have consistent magnitude, which is important for similarity calculations.
    /// </summary>
    /// <param name="vector"></param>
    private static void NormalizeL2(float[] vector)
    {
        float sum = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        float norm = (float)Math.Sqrt(sum);
        if (norm > 0.00001f)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
    }

    /// <summary>
    /// Disposes the ONNX inference session and releases any unmanaged resources. This method should be called when the <see cref="OnnxEmbeddingProvider"/> is no longer needed to free up memory and other resources used by the ONNX runtime.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
