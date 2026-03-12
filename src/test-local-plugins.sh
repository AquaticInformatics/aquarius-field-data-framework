#!/bin/bash

set -e

VERSION=${1:-"26.1"}
FRAMEWORK=${2:-"net10"}
CONFIGURATION=${3:-"Release"}

echo "=== Local Plugin Tests ==="
echo "Version: $VERSION"
echo "Framework: $FRAMEWORK"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_DIR="$SCRIPT_DIR"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
RELEASE_DIR="$ROOT_DIR/releases/$VERSION"

if [ ! -d "$RELEASE_DIR" ]; then
    echo "Error: Release directory not found: $RELEASE_DIR"
    echo "Run local-deploy-build.sh first to create artifacts"
    exit 1
fi

if [ "$FRAMEWORK" = "net10" ]; then
    FW_PATH="net10.0"
    ARTIFACT_SUFFIX="-net10"
    FRAMEWORK_DLL="$SRC_DIR/Library/$FW_PATH/FieldDataPluginFramework.dll"
else
    FW_PATH="net472"
    ARTIFACT_SUFFIX=""
    FRAMEWORK_DLL="$SRC_DIR/Library/FieldDataPluginFramework.dll"
fi

PLUGIN_TESTER_ZIP="$RELEASE_DIR/PluginTester$ARTIFACT_SUFFIX.zip"
PLUGIN_TESTER_EXE="$RELEASE_DIR/PluginTester$ARTIFACT_SUFFIX/PluginTester.exe"

if [ "$FRAMEWORK" = "net10" ]; then
    NESTED_EXE="$RELEASE_DIR/PluginTester$ARTIFACT_SUFFIX/net10.0/PluginTester.exe"
    if [ -f "$NESTED_EXE" ]; then
        PLUGIN_TESTER_EXE="$NESTED_EXE"
    fi
fi

if [ "$FRAMEWORK" != "net10" ] && [ -f "$RELEASE_DIR/PluginTester.exe" ]; then
    PLUGIN_TESTER_EXE="$RELEASE_DIR/PluginTester.exe"
fi

if [ ! -f "$PLUGIN_TESTER_EXE" ] && [ -f "$PLUGIN_TESTER_ZIP" ]; then
    echo "Extracting PluginTester..."
    EXTRACT_DIR="$RELEASE_DIR/PluginTester$ARTIFACT_SUFFIX"
    mkdir -p "$EXTRACT_DIR"
    unzip -q -o "$PLUGIN_TESTER_ZIP" -d "$EXTRACT_DIR"
    
    if [ "$FRAMEWORK" = "net10" ]; then
        NESTED_EXE="$EXTRACT_DIR/net10.0/PluginTester.exe"
        if [ -f "$NESTED_EXE" ]; then
            PLUGIN_TESTER_EXE="$NESTED_EXE"
        fi
    fi
fi

if [ ! -f "$PLUGIN_TESTER_EXE" ]; then
    echo "Error: PluginTester not found: $PLUGIN_TESTER_EXE"
    exit 1
fi

if [ ! -f "$FRAMEWORK_DLL" ]; then
    echo "Error: Framework DLL not found: $FRAMEWORK_DLL"
    exit 1
fi

echo "Using PluginTester: $PLUGIN_TESTER_EXE"
echo "Using Framework: $FRAMEWORK_DLL"
echo ""

TEST_WORKSPACE="$ROOT_DIR/test-results/$VERSION-$FRAMEWORK"
rm -rf "$TEST_WORKSPACE"
mkdir -p "$TEST_WORKSPACE"

PASSED_COUNT=0
FAILED_COUNT=0
SKIPPED_COUNT=0

declare -a TEST_RESULTS=()

test_plugin() {
    local NAME="$1"
    local PLUGIN_PATH="$2"
    local DATA_PATH="$3"
    local SETTINGS="$4"
    
    echo "Testing: $NAME"
    
    if [ ! -f "$PLUGIN_PATH" ]; then
        echo "Warning: Plugin not found: $PLUGIN_PATH"
        TEST_RESULTS+=("{\"Name\":\"$NAME\",\"Status\":\"Skipped\",\"Error\":\"Plugin not found\"}")
        SKIPPED_COUNT=$((SKIPPED_COUNT + 1))
        return 1
    fi
    
    if [ ! -f "$DATA_PATH" ]; then
        echo "Warning: Test data not found: $DATA_PATH"
        TEST_RESULTS+=("{\"Name\":\"$NAME\",\"Status\":\"Skipped\",\"Error\":\"Test data not found\"}")
        SKIPPED_COUNT=$((SKIPPED_COUNT + 1))
        return 1
    fi
    
    local SETTINGS_ARGS=""
    if [ -n "$SETTINGS" ]; then
        SETTINGS_ARGS="-Setting=$SETTINGS"
    fi
    
    if mono "$PLUGIN_TESTER_EXE" \
        -Verbose=True \
        -FrameworkAssemblyPath="$FRAMEWORK_DLL" \
        -Plugin="$PLUGIN_PATH" \
        -Data="$DATA_PATH" \
        $SETTINGS_ARGS > /dev/null 2>&1; then
        echo "  PASSED"
        TEST_RESULTS+=("{\"Name\":\"$NAME\",\"Status\":\"Passed\"}")
        PASSED_COUNT=$((PASSED_COUNT + 1))
        return 0
    else
        local EXIT_CODE=$?
        echo "  FAILED (Exit code: $EXIT_CODE)"
        TEST_RESULTS+=("{\"Name\":\"$NAME\",\"Status\":\"Failed\",\"Error\":\"Exit code $EXIT_CODE\"}")
        FAILED_COUNT=$((FAILED_COUNT + 1))
        return 1
    fi
}

echo ""
echo "--- JSON Plugin ---"
JSON_PLUGIN="$RELEASE_DIR/JsonFieldData$ARTIFACT_SUFFIX.plugin"
JSON_DATA_DIR="$SRC_DIR/JsonFieldData/data"

JSON_FILE_COUNT=$(find "$JSON_DATA_DIR" -maxdepth 1 -name "*.json" -type f 2>/dev/null | wc -l)

if [ $JSON_FILE_COUNT -eq 0 ]; then
    echo "Warning: No JSON test data files found in $JSON_DATA_DIR"
    TEST_RESULTS+=("{\"Name\":\"JSON\",\"Status\":\"Skipped\",\"Error\":\"No test data files\"}")
    SKIPPED_COUNT=$((SKIPPED_COUNT + 1))
else
    echo "Found $JSON_FILE_COUNT JSON test files"
    
    while IFS= read -r -d '' JSON_FILE; do
        JSON_BASENAME=$(basename "$JSON_FILE" .json)
        test_plugin "JSON-$JSON_BASENAME" "$JSON_PLUGIN" "$JSON_FILE" ""
    done < <(find "$JSON_DATA_DIR" -maxdepth 1 -name "*.json" -type f -print0)
fi

echo ""
echo "--- MultiFile Plugin ---"

CURRENT_DIR=$(pwd)
cd "$TEST_WORKSPACE"

MULTIFILE_DIR="$TEST_WORKSPACE/MultiFile"
mkdir -p "$MULTIFILE_DIR"
cd "$MULTIFILE_DIR"

MULTIFILE_PLUGIN="$RELEASE_DIR/MultiFile$ARTIFACT_SUFFIX.plugin"
if [ ! -f "$MULTIFILE_PLUGIN" ]; then
    echo "Warning: MultiFile plugin not found"
    TEST_RESULTS+=("{\"Name\":\"MultiFile\",\"Status\":\"Skipped\",\"Error\":\"Plugin not found\"}")
    SKIPPED_COUNT=$((SKIPPED_COUNT + 1))
else
    DATA_DIR="$MULTIFILE_DIR/data"
    mkdir -p "$DATA_DIR"
    
    SOURCE_DATA_DIR="$SRC_DIR/JsonFieldData/data"
    
    COPIED_COUNT=0
    while IFS= read -r -d '' JSON_FILE; do
        cp "$JSON_FILE" "$DATA_DIR/"
        COPIED_COUNT=$((COPIED_COUNT + 1))
    done < <(find "$SOURCE_DATA_DIR" -maxdepth 1 -name "*.json" -type f -print0)
    
    if [ $COPIED_COUNT -eq 0 ]; then
        echo "Warning: No test data files found"
        TEST_RESULTS+=("{\"Name\":\"MultiFile\",\"Status\":\"Skipped\",\"Error\":\"No test data\"}")
        SKIPPED_COUNT=$((SKIPPED_COUNT + 1))
    else
        echo "Copying $COPIED_COUNT JSON files for MultiFile test..."
        
        CONFIG_PATH="$MULTIFILE_DIR/multifile-config.json"
        cat > "$CONFIG_PATH" << 'EOF'
{
  "Plugins": [
    {
      "AssemblyQualifiedTypeName": "JsonFieldData.Plugin, JsonFieldData"
    }
  ]
}
EOF
        
        ZIP_PATH="$MULTIFILE_DIR/multi-data.zip"
        (cd "$DATA_DIR" && zip -q "$ZIP_PATH" *)
        
        JSON_PLUGIN_COPY="$RELEASE_DIR/JsonFieldData$ARTIFACT_SUFFIX.plugin"
        if [ -f "$JSON_PLUGIN_COPY" ]; then
            cp "$JSON_PLUGIN_COPY" "$MULTIFILE_DIR/"
        fi
        
        test_plugin "MultiFile" "$MULTIFILE_PLUGIN" "$ZIP_PATH" "Config=$CONFIG_PATH"
    fi
fi

cd "$CURRENT_DIR"

echo ""
echo "=== Test Report ==="

for i in "${!TEST_RESULTS[@]}"; do
    RESULT="${TEST_RESULTS[$i]}"
    
    if echo "$RESULT" | grep -q '"Status":"Passed"'; then
        NAME=$(echo "$RESULT" | grep -o '"Name":"[^"]*"' | cut -d'"' -f4)
        echo "PASSED: $NAME"
    elif echo "$RESULT" | grep -q '"Status":"Failed"'; then
        NAME=$(echo "$RESULT" | grep -o '"Name":"[^"]*"' | cut -d'"' -f4)
        ERROR=$(echo "$RESULT" | grep -o '"Error":"[^"]*"' | cut -d'"' -f4)
        echo "FAILED: $NAME"
        echo "  Error: $ERROR"
    elif echo "$RESULT" | grep -q '"Status":"Skipped"'; then
        NAME=$(echo "$RESULT" | grep -o '"Name":"[^"]*"' | cut -d'"' -f4)
        ERROR=$(echo "$RESULT" | grep -o '"Error":"[^"]*"' | cut -d'"' -f4)
        echo "SKIPPED: $NAME"
        echo "  Error: $ERROR"
    fi
done

echo ""
echo "$PASSED_COUNT passed, $FAILED_COUNT failed, $SKIPPED_COUNT skipped"

REPORT_PATH="$TEST_WORKSPACE/test-report.json"
echo "[" > "$REPORT_PATH"
for i in "${!TEST_RESULTS[@]}"; do
    echo "  ${TEST_RESULTS[$i]}" >> "$REPORT_PATH"
    if [ $i -lt $((${#TEST_RESULTS[@]} - 1)) ]; then
        echo "," >> "$REPORT_PATH"
    fi
done
echo "]" >> "$REPORT_PATH"

echo ""
echo "Detailed results saved to: $REPORT_PATH"

if [ $FAILED_COUNT -gt 0 ]; then
    exit 1
fi
