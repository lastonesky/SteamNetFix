#!/bin/bash
# SteamNetFix 跨平台构建脚本
# 用法: ./build.sh [平台]
# 示例: ./build.sh           (构建所有平台)
#       ./build.sh win-x64   (只构建 Windows x64)

set -e

PROJECT_NAME="SteamNetFix"
OUTPUT_DIR="./publish"

echo ""
echo "  SteamNetFix 构建脚本"
echo "  ===================="
echo ""

# 清理旧的构建
if [ -d "$OUTPUT_DIR" ]; then
    echo "  清理旧的构建产物..."
    rm -rf "$OUTPUT_DIR"
fi
mkdir -p "$OUTPUT_DIR"

# 定义目标平台
declare -A PLATFORMS=(
    ["win-x64"]="Windows x64"
    ["win-arm64"]="Windows ARM64"
    ["linux-x64"]="Linux x64"
    ["linux-arm64"]="Linux ARM64"
    ["osx-x64"]="macOS x64"
    ["osx-arm64"]="macOS ARM64 (Apple Silicon)"
)

build_platform() {
    local rid=$1
    local name=${PLATFORMS[$rid]:-$rid}
    echo ""
    echo "  构建 $name ($rid)..."

    if dotnet publish -c Release -r "$rid" -o "$OUTPUT_DIR/$rid" -v minimal; then
        # 清理多余文件
        rm -f "$OUTPUT_DIR/$rid/"*.pdb
        rm -f "$OUTPUT_DIR/$rid/"*.xml
        rm -f "$OUTPUT_DIR/$rid/"*.staticwebassets.endpoints.json
        rm -f "$OUTPUT_DIR/$rid/web.config"

        # 获取文件大小
        local exe_path=""
        if [ -f "$OUTPUT_DIR/$rid/$PROJECT_NAME.exe" ]; then
            exe_path="$OUTPUT_DIR/$rid/$PROJECT_NAME.exe"
        elif [ -f "$OUTPUT_DIR/$rid/$PROJECT_NAME" ]; then
            exe_path="$OUTPUT_DIR/$rid/$PROJECT_NAME"
        fi

        if [ -n "$exe_path" ]; then
            local size=$(du -h "$exe_path" | cut -f1)
            echo "    成功: $exe_path ($size)"
        fi
    else
        echo "    失败: 构建 $name 出错"
    fi
    echo ""
}

# 构建
if [ -n "$1" ]; then
    build_platform "$1"
else
    for rid in "${!PLATFORMS[@]}"; do
        build_platform "$rid"
    done
fi

echo ""
echo "  ===================="
echo "  构建完成!"
echo ""
echo "  产物目录: $OUTPUT_DIR/"
echo ""

# 显示所有产物
for rid in "${!PLATFORMS[@]}"; do
    local_path="$OUTPUT_DIR/$rid"
    if [ -f "$local_path/$PROJECT_NAME.exe" ]; then
        size=$(du -h "$local_path/$PROJECT_NAME.exe" | cut -f1)
        echo "    $rid  ($size)"
    elif [ -f "$local_path/$PROJECT_NAME" ]; then
        size=$(du -h "$local_path/$PROJECT_NAME" | cut -f1)
        echo "    $rid  ($size)"
    fi
done

echo ""
