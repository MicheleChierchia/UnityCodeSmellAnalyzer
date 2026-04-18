#!/bin/bash

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <game_folder>"
    exit 1
fi

GAME_FOLDER=$(realpath "$1")
GAME_NAME=$(basename "$GAME_FOLDER")
mkdir -p "./Results/$GAME_NAME"
RESULTS_DIR=$(realpath "./Results/$GAME_NAME")

mkdir -p "$RESULTS_DIR/Code"
mkdir -p "$RESULTS_DIR/Data"

# Paths to executables
CSHARP_ANALYZER="./Analyzer/CSharpAnalyzer/bin/Debug/net8.0/linux-x64/CSharpAnalyzer"
CODE_SMELL_ANALYZER="./Analyzer/CodeSmellAnalyzer/bin/Debug/net8.0/linux-x64/CodeSmellAnalyzer"
UNITY_DATA_ANALYZER="./Analyzer/UnityDataAnalyzer/bin/Debug/net8.0/linux-x64/UnityDataAnalyzer"
META_SMELL_ANALYZER="./Analyzer/MetaSmellAnalyzer/bin/Debug/net8.0/linux-x64/MetaSmellAnalyzer"
SMELL_FILE=$(realpath "./Analyzer/MetaSmellAnalyzer/smell.txt")

# Ensure executables exist (optional: build them if not found)
if [ ! -f "$CSHARP_ANALYZER" ]; then
    echo "Executables not found. Building projects..."
    dotnet build Analyzer/CSharpAnalyzer/CSharpAnalyzer.csproj
    dotnet build Analyzer/CodeSmellAnalyzer/CodeSmellAnalyzer.csproj
    dotnet build Analyzer/UnityDataAnalyzer/UnityDataAnalyzer.csproj
    dotnet build Analyzer/MetaSmellAnalyzer/MetaSmellAnalyzer.csproj
fi

echo "Starting analysis for $GAME_NAME..."

# 1. CSharpAnalyzer
echo "Running CSharpAnalyzer..."
cd "$(dirname "$CSHARP_ANALYZER")"
./CSharpAnalyzer -n "$GAME_NAME" -p "$GAME_FOLDER" -r "$RESULTS_DIR/Code" -v
cd - > /dev/null

# 2. CodeSmellAnalyzer
echo "Running CodeSmellAnalyzer..."
cd "$(dirname "$CODE_SMELL_ANALYZER")"
./CodeSmellAnalyzer -d "$RESULTS_DIR/Code/CodeAnalysis.json" -r "$RESULTS_DIR/Code" -c -v
cd - > /dev/null

# 3. UnityDataAnalyzer
echo "Running UnityDataAnalyzer..."
cd "$(dirname "$UNITY_DATA_ANALYZER")"
./UnityDataAnalyzer -n "$GAME_NAME" -d "$GAME_FOLDER" -r "$RESULTS_DIR/Data" -v
cd - > /dev/null

# 4. MetaSmellAnalyzer
echo "Running MetaSmellAnalyzer..."
cd "$(dirname "$META_SMELL_ANALYZER")"
./MetaSmellAnalyzer -d "$RESULTS_DIR/Data/mainResults" "$RESULTS_DIR/Data/metaResults" -r "$RESULTS_DIR/Data" -c -v -f "$SMELL_FILE"
cd - > /dev/null

echo "Analysis complete. Results are in $RESULTS_DIR"
