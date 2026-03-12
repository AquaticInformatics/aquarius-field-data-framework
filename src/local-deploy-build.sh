#!/bin/bash

set -e

VERSION=${1:-"26.1"}
FRAMEWORK=${2:-"net10"}
CONFIGURATION=${3:-"Release"}

echo "=== Local Deploy Build ==="
echo "Version: $VERSION"
echo "Framework: $FRAMEWORK"
echo "Configuration: $CONFIGURATION"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$SCRIPT_DIR"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
SOLUTION_FILE="$SRC_DIR/FieldDataFramework.sln"
NUSPEC_FILE="$SRC_DIR/Aquarius.FieldDataFramework.nuspec"
RELEASE_DIR="$ROOT_DIR/releases/$VERSION"

if [ ! -f "$SOLUTION_FILE" ]; then
    echo "Error: Solution file not found: $SOLUTION_FILE"
    exit 1
fi

echo "Creating release directory: $RELEASE_DIR"
rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

echo "Updating NuGet package version to $VERSION..."
if [ -f "$NUSPEC_FILE" ]; then
    sed -i.bak "s|<version>.*</version>|<version>$VERSION</version>|g" "$NUSPEC_FILE"
    sed -i.bak "s|0\.0\.0|$VERSION|g" "$NUSPEC_FILE"
    rm -f "$NUSPEC_FILE.bak"
fi

echo ""
echo "Restoring NuGet packages..."
if ! dotnet restore "$SOLUTION_FILE"; then
    echo "Error: Package restore failed"
    exit 1
fi

echo ""
echo "Building solution..."
if ! dotnet build "$SOLUTION_FILE" -c "$CONFIGURATION" --no-restore; then
    echo "Error: Build failed"
    exit 1
fi

echo ""
echo "Creating NuGet package..."
cd "$ROOT_DIR"
if ! nuget pack "$NUSPEC_FILE"; then
    echo "Error: NuGet pack failed. Ensure nuget is installed and all files exist."
    exit 1
fi

echo ""
echo "Collecting artifacts..."

copy_artifact() {
    local SOURCE_PATH="$1"
    local DEST_NAME="$2"
    local TYPE="$3"

    local FULL_SOURCE="$ROOT_DIR/$SOURCE_PATH"

    if [ ! -e "$FULL_SOURCE" ]; then
        echo "Warning: Artifact not found: $FULL_SOURCE"
        return
    fi

    local DEST_PATH="$RELEASE_DIR/$DEST_NAME"

    if [ "$TYPE" = "zip" ]; then
        echo "  - Zipping $DEST_NAME..."
        (cd "$(dirname "$FULL_SOURCE")" && zip -r "$DEST_PATH.zip" "$(basename "$FULL_SOURCE")" > /dev/null 2>&1)
    elif [ "$TYPE" = "file" ]; then
        echo "  - Copying $DEST_NAME..."
        cp "$FULL_SOURCE" "$DEST_PATH"
    fi
}

if [ "$FRAMEWORK" = "net10" ]; then
    FW_PATH="net10.0"
    ARTIFACT_SUFFIX="-net10"
elif [ "$FRAMEWORK" = "net472" ]; then
    FW_PATH=""
    ARTIFACT_SUFFIX=""
elif [ "$FRAMEWORK" = "net48" ]; then
    FW_PATH=""
    ARTIFACT_SUFFIX=""
else
    FW_PATH="$FRAMEWORK"
    ARTIFACT_SUFFIX="-$FRAMEWORK"
fi

if [ "$FRAMEWORK" = "net10" ]; then
    copy_artifact "src/PluginPackager/bin/$CONFIGURATION/$FW_PATH" "PluginPackager$ARTIFACT_SUFFIX" "zip"
    copy_artifact "src/PluginTester/bin/$CONFIGURATION/$FW_PATH" "PluginTester$ARTIFACT_SUFFIX" "zip"
    copy_artifact "src/JsonFieldData/deploy/$CONFIGURATION/$FW_PATH/JsonFieldData.plugin" "JsonFieldData$ARTIFACT_SUFFIX.plugin" "file"
    copy_artifact "src/MultiFile/deploy/$CONFIGURATION/$FW_PATH/MultiFile.plugin" "MultiFile$ARTIFACT_SUFFIX.plugin" "file"
    copy_artifact "src/MultiFile.Configurator/bin/$CONFIGURATION/$FW_PATH" "MultiFile.Configurator$ARTIFACT_SUFFIX" "zip"
    copy_artifact "src/FieldVisitHotFolderService/bin/$CONFIGURATION/$FW_PATH" "FieldVisitHotFolderService$ARTIFACT_SUFFIX" "zip"
else
    copy_artifact "src/Library" "FieldDataPluginFramework" "zip"
    copy_artifact "src/PluginPackager/bin/$CONFIGURATION/PluginPackager.exe" "PluginPackager.exe" "file"
    copy_artifact "src/PluginTester/bin/$CONFIGURATION/PluginTester.exe" "PluginTester.exe" "file"
    copy_artifact "src/JsonFieldData/deploy/$CONFIGURATION/JsonFieldData.plugin" "JsonFieldData.plugin" "file"
    copy_artifact "src/MultiFile/deploy/$CONFIGURATION/MultiFile.plugin" "MultiFile.plugin" "file"
    copy_artifact "src/MultiFile.Configurator/bin/$CONFIGURATION/MultiFile.Configurator.exe" "MultiFile.Configurator.exe" "file"
    copy_artifact "src/FieldVisitHotFolderService/bin/$CONFIGURATION" "FieldVisitHotFolderService" "zip"
fi

echo "  - Copying NuGet packages..."
find "$ROOT_DIR" -maxdepth 1 -name "Aquarius.FieldDataFramework.*.nupkg" -exec cp {} "$RELEASE_DIR/" \;

echo ""
echo "=== Build Complete ==="
echo "Artifacts saved to: $RELEASE_DIR"
echo ""
echo "Artifact list:"
ls -lh "$RELEASE_DIR" | tail -n +2 | awk '{print "  - " $9 " (" $5 ")"}'
