#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# setup-onnx-runtimes.sh
# Downloads and configures:
#   1. Official OpenVINO & ONNX Runtime native libraries (Linux x86_64) into
#      src/IdempotencyEngine.Data/Runtimes/linux-x64/native/
#   2. The ONNX embedding model (all-MiniLM-L6-v2.onnx) into
#      src/IdempotencyEngine.Data/Models/
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
RUNTIMES_DIR="${REPO_ROOT}/src/IdempotencyEngine.Data/Runtimes/linux-x64/native"
MODELS_DIR="${REPO_ROOT}/src/IdempotencyEngine.Data/Models"
TMP_DIR="/tmp/onnx-runtime-setup-$$"

mkdir -p "${RUNTIMES_DIR}" "${MODELS_DIR}" "${TMP_DIR}"
trap 'rm -rf "${TMP_DIR}"' EXIT

echo "=========================================================================="
echo " Setting up ONNX Runtime, OpenVINO NPU Compiler, and Embedding Model"
echo "=========================================================================="

# ------------------------------------------------------------------------------
# 1. Download official ONNX Runtime OpenVINO Linux C++ libraries
# ------------------------------------------------------------------------------
ORT_OV_VERSION="1.24.1"
ORT_OV_URL="https://files.pythonhosted.org/packages/d6/ea/1b6ea1fdb0b9c5ba2d70d5718df28ea3a18a996f01da1b8c8d8dd66133be/onnxruntime_openvino-${ORT_OV_VERSION}-cp312-cp312-manylinux_2_28_x86_64.whl"

echo "--> [1/3] Downloading Intel ONNX Runtime + OpenVINO C++ libraries (v${ORT_OV_VERSION})..."
curl -sSL "${ORT_OV_URL}" -o "${TMP_DIR}/ort-openvino.whl"

python3 -c "
import zipfile, os

whl_path = '${TMP_DIR}/ort-openvino.whl'
target_dir = '${RUNTIMES_DIR}'

with zipfile.ZipFile(whl_path, 'r') as z:
    for name in z.namelist():
        if name.startswith('onnxruntime/capi/') and (name.endswith('.so') or '.so.' in name):
            fn = os.path.basename(name)
            if not fn or fn.startswith('onnxruntime_pybind11'):
                continue
            with open(os.path.join(target_dir, fn), 'wb') as f:
                f.write(z.read(name))
"

cd "${RUNTIMES_DIR}"
ln -sf "libonnxruntime.so.${ORT_OV_VERSION}" libonnxruntime.so

# ------------------------------------------------------------------------------
# 2. Download official Intel NPU Driver & Graph Compiler (v1.38.0)
# ------------------------------------------------------------------------------
NPU_VERSION="1.38.0.20260910-34487311128"
NPU_URL="https://github.com/intel/linux-npu-driver/releases/download/v1.38.0/linux-npu-driver-v1.38.0.20260910-34487311128-ubuntu2404.tar.gz"

echo "--> [2/3] Downloading Intel NPU standalone graph compiler (v1.38.0)..."
curl -sSL "${NPU_URL}" -o "${TMP_DIR}/intel-npu.tar.gz"

cd "${TMP_DIR}"
tar -xf intel-npu.tar.gz
ar -x "intel-driver-compiler-npu_${NPU_VERSION}~ubuntu24.04_amd64.deb"
tar -xf data.tar.*

cp ./usr/lib/x86_64-linux-gnu/libopenvino_intel_npu_compiler*.so "${RUNTIMES_DIR}/"
cd "${RUNTIMES_DIR}"
ln -sf libopenvino_intel_npu_compiler_loader.so libnpu_driver_compiler.so
chmod +x *.so*

# ------------------------------------------------------------------------------
# 3. Ensure ONNX Embedding Model (all-MiniLM-L6-v2.onnx) is present
# ------------------------------------------------------------------------------
MODEL_PATH="${MODELS_DIR}/all-MiniLM-L6-v2.onnx"
MODEL_URL="https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx"

if [ -f "${MODEL_PATH}" ]; then
    echo "--> [3/3] ONNX embedding model already exists at: ${MODEL_PATH}"
else
    echo "--> [3/3] Downloading all-MiniLM-L6-v2.onnx model (~87 MB)..."
    curl -sSL "${MODEL_URL}" -o "${MODEL_PATH}"
fi

echo "=========================================================================="
echo " Installation Complete!"
echo " Native Libraries: ${RUNTIMES_DIR}"
echo " ONNX Model:       ${MODEL_PATH}"
echo ""
echo " To enable Linux Level Zero driver to locate the NPU compiler directly"
echo " without sudo or system symlinks, export LD_LIBRARY_PATH in your shell:"
echo ""
echo "   export LD_LIBRARY_PATH=\"${RUNTIMES_DIR}:\$LD_LIBRARY_PATH\""
echo "=========================================================================="
