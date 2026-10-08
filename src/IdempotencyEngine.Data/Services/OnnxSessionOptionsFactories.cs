using IdempotencyEngine.Data.Abstractions;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace IdempotencyEngine.Data.Services;

/// <summary>
/// Default CPU execution provider factory using graph optimization level ALL.
/// </summary>
public class CpuSessionOptionsFactory : IOnnxSessionOptionsFactory
{
    public SessionOptions CreateOptions()
    {
        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        return options;
    }
}

/// <summary>
/// Execution provider factory targeting DirectML hardware acceleration (Intel Arc GPU / DirectX 12).
/// </summary>
public class DirectMlSessionOptionsFactory : IOnnxSessionOptionsFactory
{
    private readonly int _deviceId;

    public DirectMlSessionOptionsFactory(int deviceId = 0)
    {
        _deviceId = deviceId;
    }

    public SessionOptions CreateOptions()
    {
        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.AppendExecutionProvider_DML(_deviceId);
        return options;
    }
}

/// <summary>
/// Execution provider factory targeting OpenVINO acceleration (Intel NPU / Core Ultra processors).
/// </summary>
public class OpenVinoSessionOptionsFactory : IOnnxSessionOptionsFactory
{
    private readonly string _deviceType;

    public OpenVinoSessionOptionsFactory(string deviceType = "NPU")
    {
        _deviceType = deviceType;
    }

    public SessionOptions CreateOptions()
    {
        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.AppendExecutionProvider_OpenVINO(_deviceType);
        return options;
    }
}

/// <summary>
/// Composite selector factory that dynamically instantiates and delegates to the appropriate hardware SessionOptions factory based on OnnxHardwareOptions configuration.
/// </summary>
public class ConfigurableOnnxSessionOptionsFactory : IOnnxSessionOptionsFactory
{
    private readonly OnnxHardwareOptions _hardwareOptions;

    public ConfigurableOnnxSessionOptionsFactory(IOptions<OnnxHardwareOptions> hardwareOptions)
    {
        _hardwareOptions = hardwareOptions?.Value ?? new OnnxHardwareOptions();
    }

    public SessionOptions CreateOptions()
    {
        IOnnxSessionOptionsFactory selectedFactory = _hardwareOptions.ExecutionProvider?.ToUpperInvariant() switch
        {
            "DIRECTML" or "GPU" => new DirectMlSessionOptionsFactory(_hardwareOptions.DeviceId),
            "OPENVINO" or "NPU" => new OpenVinoSessionOptionsFactory("NPU"),
            _ => new CpuSessionOptionsFactory()
        };

        return selectedFactory.CreateOptions();
    }
}
