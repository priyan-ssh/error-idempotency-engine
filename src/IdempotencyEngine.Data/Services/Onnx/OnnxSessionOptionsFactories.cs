using System.Runtime.InteropServices;
using IdempotencyEngine.Data.Abstractions;
using IdempotencyEngine.Domain.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace IdempotencyEngine.Data.Services.Onnx;

/// <summary>
/// Factory for creating ONNX SessionOptions based on the configured hardware execution provider (CPU, DirectML, OpenVINO).
/// </summary>
internal static class OnnxNativeResolver
{
    private static int _initialized;

    /// <summary>
    /// Ensures that the native ONNX Runtime libraries are loaded and the appropriate execution provider is initialized based on the current platform and configuration. This method is idempotent and can be called multiple times without adverse effects. It handles loading of native libraries for Linux platforms, including OpenVINO and Intel NPU Compiler libraries, and sets up a custom DllImport resolver for the ONNX Runtime library.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var baseDir = AppContext.BaseDirectory;
            var nativeCandidates = new[]
            {
                Path.Combine(baseDir, "Runtimes", "linux-x64", "native"),
                Path.Combine(baseDir, "runtimes", "linux-x64", "native")
            };

            var nativeDir = nativeCandidates.FirstOrDefault(Directory.Exists) ?? nativeCandidates[0];

            // Pre-load OpenVINO and Intel NPU Compiler libraries into process space
            if (Directory.Exists(nativeDir))
            {
                var preloadLibs = new[]
                {
                    "libtbb.so.12",
                    "libopenvino.so",
                    "libopenvino_intel_npu_compiler.so",
                    "libnpu_driver_compiler.so"
                };

                foreach (var lib in preloadLibs)
                {
                    var fullPath = Path.Combine(nativeDir, lib);
                    if (File.Exists(fullPath))
                    {
                        NativeLibrary.TryLoad(fullPath, out _);
                    }
                }
            }

            NativeLibrary.SetDllImportResolver(typeof(SessionOptions).Assembly, (libraryName, assembly, searchPath) =>
            {
                if (libraryName == "onnxruntime" || libraryName == "onnxruntime.dll")
                {
                    var candidates = new[]
                    {
                        Path.Combine(nativeDir, "libonnxruntime.so"),
                        Path.Combine(baseDir, "libonnxruntime.so")
                    };

                    foreach (var path in candidates)
                    {
                        if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                        {
                            return handle;
                        }
                    }
                }
                return IntPtr.Zero;
            });
        }
    }
}

/// <summary>
/// Default CPU execution provider factory using graph optimization level ALL.
/// </summary>
public class CpuSessionOptionsFactory : IOnnxSessionOptionsFactory
{
    public SessionOptions CreateOptions()
    {
        OnnxNativeResolver.EnsureInitialized();
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
    private readonly ILogger<OpenVinoSessionOptionsFactory> _logger;

    public OpenVinoSessionOptionsFactory(string deviceType = "NPU", ILogger<OpenVinoSessionOptionsFactory>? logger = null)
    {
        _deviceType = deviceType;
        _logger = logger ?? NullLogger<OpenVinoSessionOptionsFactory>.Instance;
    }

    public SessionOptions CreateOptions()
    {
        OnnxNativeResolver.EnsureInitialized();
        var options = new SessionOptions();
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        _logger.LogInformation("Initializing strict OpenVINO Execution Provider for device: {DeviceType}", _deviceType);
        options.AppendExecutionProvider_OpenVINO(_deviceType);
        _logger.LogInformation("Successfully initialized OpenVINO Execution Provider for device: {DeviceType}", _deviceType);

        return options;
    }
}

/// <summary>
/// Composite selector factory that dynamically instantiates and delegates to the appropriate hardware SessionOptions factory based on OnnxHardwareOptions configuration.
/// </summary>
public class ConfigurableOnnxSessionOptionsFactory : IOnnxSessionOptionsFactory
{
    private readonly OnnxHardwareOptions _hardwareOptions;
    private readonly ILogger<ConfigurableOnnxSessionOptionsFactory> _logger;

    public ConfigurableOnnxSessionOptionsFactory(
        IOptions<OnnxHardwareOptions>? hardwareOptions,
        ILogger<ConfigurableOnnxSessionOptionsFactory>? logger = null)
    {
        _logger = logger ?? NullLogger<ConfigurableOnnxSessionOptionsFactory>.Instance;

        if (hardwareOptions?.Value == null)
        {
            _logger.LogWarning("OnnxHardwareOptions configuration missing or null. Silently falling back to default CPU execution provider.");
            _hardwareOptions = new OnnxHardwareOptions();
        }
        else
        {
            _hardwareOptions = hardwareOptions.Value;
        }
    }

    public SessionOptions CreateOptions()
    {
        var provider = _hardwareOptions.ExecutionProvider?.ToUpperInvariant();
        _logger.LogInformation("Configuring ONNX execution provider: {Provider} (DeviceId: {DeviceId})", provider ?? "CPU", _hardwareOptions.DeviceId);

        IOnnxSessionOptionsFactory selectedFactory = provider switch
        {
            "DIRECTML" or "GPU" => new DirectMlSessionOptionsFactory(_hardwareOptions.DeviceId),
            "OPENVINO" or "NPU" => new OpenVinoSessionOptionsFactory("NPU"),
            _ => new CpuSessionOptionsFactory()
        };

        return selectedFactory.CreateOptions();
    }
}
