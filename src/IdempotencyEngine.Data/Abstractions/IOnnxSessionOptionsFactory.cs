using Microsoft.ML.OnnxRuntime;

namespace IdempotencyEngine.Data.Abstractions;

/// <summary>
/// Factory strategy interface for configuring ONNX Runtime SessionOptions hardware execution providers.
/// </summary>
public interface IOnnxSessionOptionsFactory
{
    /// <summary>
    /// Creates and configures a SessionOptions instance for ONNX model inference.
    /// </summary>
    SessionOptions CreateOptions();
}
