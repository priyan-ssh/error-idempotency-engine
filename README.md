# The Idempotency Engine

A dev-time memory for errors, problems, and decisions.

## Tech Stack & Runtime Environment
- **API Backend:** C# .NET 10 Web API (`net10.0`)
- **Database & Vectors:** PostgreSQL 16 + `pgvector` HNSW indexes
- **Database Migrations:** DbUp (Plain `.sql` scripts)
- **Watcher Service:** Go
- **Agent Adapter:** Node.js / TypeScript MCP Server

---

## Getting Started (Fresh Clone Setup)

1. **Clone the repository:**
   ```bash
   git clone <repo-url>
   cd error-idempotency-engine
   ```

2. **Download native ONNX Runtime & OpenVINO libraries (required for Linux):**
   ```bash
   ./scripts/setup-onnx-runtimes.sh
   ```
   *This downloads the official precompiled Intel OpenVINO C++ libraries and NPU graph compiler into `src/IdempotencyEngine.Data/runtimes/` so inference works out-of-the-box.*

3. **Build & Test:**
   ```bash
   dotnet build
   dotnet test
   ```

---

## Hardware-Accelerated Vector Embeddings (CPU, GPU, NPU)

The Idempotency Engine includes a pluggable **Hardware Acceleration Strategy Pattern** for generating vector embeddings (`all-MiniLM-L6-v2`, 384 dimensions) via ONNX Runtime.

### Supported Execution Providers
1. **CPU** (Default multi-threaded processing)
2. **OpenVINO / NPU** (Optimized for Intel Core Ultra NPU acceleration)
3. **DirectML / GPU** (Optimized for Intel Arc / DirectX 12 GPU acceleration)

---

### Configuration (`appsettings.json`)

Configure your hardware provider dynamically in `src/IdempotencyEngine.Api/appsettings.json`:

```json
"OnnxHardwareOptions": {
  "ExecutionProvider": "CPU", 
  "DeviceId": 0
}
```

*Change `"ExecutionProvider"` to `"OpenVINO"` (or `"NPU"`) for NPU acceleration, or `"DirectML"` (or `"GPU"`) for GPU acceleration.*

---

### Required Packages & Native Runtime (`IdempotencyEngine.Data`)

| Hardware Target | Managed Binding Package | Native Binaries |
|---|---|---|
| **CPU (Default)** | `Microsoft.ML.OnnxRuntime.Managed` | Native Linux/Win runtime |
| **Intel NPU (OpenVINO)** | `Microsoft.ML.OnnxRuntime.Managed` | OpenVINO + NPU Compiler (`./scripts/setup-onnx-runtimes.sh`) |
| **Intel Arc GPU (DirectML)** | `Microsoft.ML.OnnxRuntime.DirectML` | Windows / DirectX 12 native packages |

For an in-depth hardware acceleration walkthrough, view [docs/OnnxGuide.html](docs/OnnxGuide.html).

#### Linux Intel NPU Quick Start
1. **Install Host User-space Driver & Loader:**
   ```bash
   # Fedora
   sudo dnf install --setopt=install_weak_deps=False intel-npu-driver oneapi-level-zero

   # Ubuntu 24.04+ / Debian
   sudo apt install intel-level-zero-npu libze-loader1
   ```

2. **Download & Setup Bundled Native OpenVINO Runtimes and Model:**
   ```bash
   ./scripts/setup-onnx-runtimes.sh
   ```

3. **Export `LD_LIBRARY_PATH` for NPU Compiler Discovery:**
   ```bash
   export LD_LIBRARY_PATH="$PWD/src/IdempotencyEngine.Data/Runtimes/linux-x64/native:$LD_LIBRARY_PATH"
   ```
   *This enables the Linux Level Zero NPU driver to locate the bundled NPU graph compiler directly within the repository.*

4. **Enable NPU in `appsettings.json`:**
   ```json
   "OnnxHardwareOptions": {
     "ExecutionProvider": "OpenVINO",
     "DeviceType": "NPU",
     "DeviceId": 0
   }
   ```

---

### Architecture & Design Pattern
- **`IOnnxSessionOptionsFactory`**: Strategy interface for configuring ONNX session options.
- **`ConfigurableOnnxSessionOptionsFactory`**: Composite selector factory that reads `OnnxHardwareOptions` at runtime and instantiates the appropriate hardware factory (`CpuSessionOptionsFactory`, `OpenVinoSessionOptionsFactory`, or `DirectMlSessionOptionsFactory`).
- **`OnnxNativeResolver`**: Automatically binds the solution's bundled native OpenVINO and ONNX Runtime libraries on Linux without requiring manual system-level symlinks.
