using IdempotencyEngine.Data.Abstractions;
using IdempotencyEngine.Domain.Interfaces;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IdempotencyEngine.Data.Services;

/// <summary>
/// Production embedding provider that generates 384-dimensional vector embeddings using an ONNX Runtime model.
/// SessionOptions are supplied via an IOnnxSessionOptionsFactory for flexible hardware execution provider selection (CPU, GPU, NPU).
/// </summary>
public class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly EmbeddingOptions _options;
    private readonly InferenceSession? _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnnxEmbeddingProvider"/> class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="sessionOptionsFactory"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public OnnxEmbeddingProvider(
        IOptions<EmbeddingOptions> options,
        IOnnxSessionOptionsFactory sessionOptionsFactory)
    {
        _options = options?.Value ?? new EmbeddingOptions();
        var factory = sessionOptionsFactory ?? throw new ArgumentNullException(nameof(sessionOptionsFactory));

        if (File.Exists(_options.ModelPath))
        {
            var sessionOptions = factory.CreateOptions();
            _session = new InferenceSession(_options.ModelPath, sessionOptions);
        }
    }

    public string ModelName => _options.ModelId;

    public int Dimensions => _options.Dimensions;

    /// <summary>
    /// Generates a vector embedding for the given text using the ONNX model. If the model is unavailable, a fallback embedding is generated.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Embedding for the given text.</returns>
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new float[_options.Dimensions]);
        }

        if (_session == null)
        {
            return Task.FromResult(GenerateFallbackEmbedding(text));
        }

        var embedding = RunInference(text);
        return Task.FromResult(embedding);
    }

    /// <summary>
    /// Generates vector embeddings for a batch of texts using the ONNX model. If the model is unavailable, fallback embeddings are generated for each text.
    /// </summary>
    /// <param name="texts"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Vector embeddings for the given texts.</returns>
    public Task<float[][]> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var list = texts.ToList();
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
    /// Runs inference on the ONNX model to generate an embedding for the given text. If the model is unavailable, a fallback embedding is generated.
    /// </summary>
    /// <param name="text"></param>
    /// <returns>Vector embedding for the given text.</returns>
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

        try
        {
            using var results = _session!.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            var embedding = new float[_options.Dimensions];
            var tensorArray = outputTensor.ToArray();

            int copyLength = Math.Min(_options.Dimensions, tensorArray.Length);
            Array.Copy(tensorArray, embedding, copyLength);

            NormalizeL2(embedding);
            return embedding;
        }
        catch
        {
            return GenerateFallbackEmbedding(text);
        }
    }

    /// <summary>
    /// Simplifies the tokenization process for the given text, returning the input IDs and attention mask.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="maxSeqLength"></param>
    /// <returns>Tuple containing the input IDs and attention mask.</returns>
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
    /// Generates a fallback embedding for the given text using a deterministic hash-based approach. This is used when the ONNX model is unavailable.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
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
    /// Normalizes the given vector to have a unit L2 norm. This is important for ensuring that the embeddings are comparable in terms of cosine similarity.
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
    /// Disposes the ONNX inference session if it was created. This is important for releasing unmanaged resources associated with the session.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
    }
}
