#!/bin/bash
# SteamNetFix 跨平台构建脚本
# 用法: ./build.sh [选项] [平台]
# 示例: ./build.sh           (只构建当前平台)
#       ./build.sh --all     (构建所有平台)
#       ./build.sh win-x64   (只构建指定的平台)

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

# 定义目标平台（不使用关联数组，兼容 macOS bash 3.2）
RIDS=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")
NAMES=("Windows x64" "Windows ARM64" "Linux x64" "Linux ARM64" "macOS x64" "macOS ARM64 (Apple Silicon)")

# 检测当前运行平台的 RID
detect_current_rid() {
    local os=$(uname -s)
    local arch=$(uname -m)

    case "$os" in
        Darwin)
            case "$arch" in
                x86_64)  echo "osx-x64" ;;
                arm64)   echo "osx-arm64" ;;
                *)       echo "osx-$arch" ;;
            esac
            ;;
        Linux)
            case "$arch" in
                x86_64)  echo "linux-x64" ;;
                aarch64) echo "linux-arm64" ;;
                *)       echo "linux-$arch" ;;
            esac
            ;;
        MINGW*|MSYS*|CYGWIN*)
            case "$arch" in
                x86_64)  echo "win-x64" ;;
                aarch64) echo "win-arm64" ;;
                *)       echo "win-$arch" ;;
            esac
            ;;
        *)
            echo "unknown-$os-$arch"
            ;;
    esac
}

# 根据 RID 获取友好名称
platform_name() {
    local rid=$1
    for i in "${!RIDS[@]}"; do
        if [ "${RIDS[$i]}" = "$rid" ]; then
            echo "${NAMES[$i]}"
            return
        fi
    done
    echo "$rid"
}

build_platform() {
    local rid=$1
    local name=$(platform_name "$rid")
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

BUILD_ALL=false
if [ -n "$1" ]; then
    if [ "$1" = "--all" ] || [ "$1" = "all" ]; then
        BUILD_ALL=true
    else
        build_platform "$1"
    fi
fi

if [ "$BUILD_ALL" = true ]; then
    for rid in "${RIDS[@]}"; do
        build_platform "$rid"
    done
elif [ -z "$1" ]; then
    # 默认：只构建当前平台
    current_rid=$(detect_current_rid)
    current_name=$(platform_name "$current_rid")
    echo "  检测到当前平台: $current_name ($current_rid)"
    echo ""
    echo "  💡 提示: 使用 ./build.sh --all 可构建所有平台"
    echo ""
    build_platform "$current_rid"
fi

echo ""
echo "  ===================="
echo "  构建完成!"
echo ""
echo "  产物目录: $OUTPUT_DIR/"
echo ""

# 显示所有产物
for rid in "${RIDS[@]}"; do
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
