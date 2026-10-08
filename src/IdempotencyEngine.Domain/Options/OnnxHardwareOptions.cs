namespace IdempotencyEngine.Domain.Options;

/// <summary>
/// Hardware execution configuration options for ONNX Runtime.
/// </summary>
public class OnnxHardwareOptions
{
    /// <summary>
    /// Target execution provider ("CPU", "DirectML", "OpenVINO"). Default is "CPU".
    /// </summary>
    public string ExecutionProvider { get; set; } = "CPU";

    /// <summary>
    /// Device ID for hardware accelerator (e.g. 0 for primary GPU).
    /// </summary>
    public int DeviceId { get; set; } = 0;
}
