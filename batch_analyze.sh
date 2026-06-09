#!/bin/bash

GAMES_FILE="games.txt"
CSV_FILE="Evaluation/OverallEvaluationDataset.csv"
REPOS_DIR="Repos"

# Ensure directories exist
mkdir -p "$REPOS_DIR"
mkdir -p "Evaluation/Results"

# Build all analyzer executables so they can be found by ProjectAnalyzer
echo "Building analyzers..."
dotnet build Analyzer/ProjectAnalyzer/ProjectAnalyzer.csproj -c Release
dotnet build Analyzer/CSharpAnalyzer/CSharpAnalyzer.csproj -c Release
dotnet build Analyzer/CodeSmellAnalyzer/CodeSmellAnalyzer.csproj -c Release
dotnet build Analyzer/UnityDataAnalyzer/UnityDataAnalyzer.csproj -c Release
dotnet build Analyzer/MetaSmellAnalyzer/MetaSmellAnalyzer.csproj -c Release
echo "Analyzers built successfully."

line_num=0
while IFS= read -r url_quoted || [ -n "$url_quoted" ]; do
    url=$(echo "$url_quoted" | tr -d '"' | tr -d '\r')
    if [ -z "$url" ]; then
        continue
    fi
    
    # Fix missing https://github.com/ prefix
    if [[ "$url" != http* ]]; then
        url="https://github.com/$url"
    fi
    
    # Check if this URL is already in the CSV (resumability)
    if grep -qF "$url" "$CSV_FILE"; then
        echo "Skipping [$line_num] $url (already in CSV)"
        ((line_num++))
        continue
    fi
    
    (
        echo "======================================================"
        echo "Processing [$line_num] $url"
        echo "======================================================"
        
        game_name=$(basename "$url")
        repo_path="$REPOS_DIR/${line_num}_${game_name}"
        
        # 1. Clone
        rm -rf "$repo_path"
        git clone "$url" "$repo_path"
        if [ $? -ne 0 ]; then
            echo "Failed to clone $url"
            exit 1
        fi
        
        # 2. Count commits
        commits=$(git -C "$repo_path" rev-list --count HEAD)
        
        # 3. Run ProjectAnalyzer
        dotnet run --project Analyzer/ProjectAnalyzer/ProjectAnalyzer.csproj -c Release -- "$repo_path" "Evaluation/Results/$game_name"
        
        # 4. Append to CSV
        echo "$line_num;$url;$commits" >> "$CSV_FILE"
        
        # 5. Clean up repo
        rm -rf "$repo_path"
        
        echo "Finished processing $game_name. Check Evaluation/Results/$game_name for details."
    ) &
    
    # Allow maximum of 4 concurrent jobs
    while [ $(jobs -r -p | wc -l) -ge 4 ]; do
        sleep 1
    done
    
    ((line_num++))
done < "$GAMES_FILE"

# Wait for all remaining background jobs to finish
wait

echo "Batch analysis completed!"
