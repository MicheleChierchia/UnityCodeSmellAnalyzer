import os
import subprocess
import shutil
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed

GAMES_FILE = "games.txt"
CSV_FILE = os.path.join("Evaluation", "OverallEvaluationDataset.csv")
REPOS_DIR = "Repos"

# Ensure directories exist
os.makedirs(REPOS_DIR, exist_ok=True)
os.makedirs(os.path.join("Evaluation", "Results"), exist_ok=True)

csv_lock = threading.Lock()

def build_analyzers():
    print("Building analyzers...")
    projects = [
        "Analyzer/ProjectAnalyzer/ProjectAnalyzer.csproj",
        "Analyzer/CSharpAnalyzer/CSharpAnalyzer.csproj",
        "Analyzer/CodeSmellAnalyzer/CodeSmellAnalyzer.csproj",
        "Analyzer/UnityDataAnalyzer/UnityDataAnalyzer.csproj",
        "Analyzer/MetaSmellAnalyzer/MetaSmellAnalyzer.csproj"
    ]
    
    for proj in projects:
        subprocess.run(["dotnet", "build", proj, "-c", "Release"], check=True)
    print("Analyzers built successfully.")

def get_dir_size_mb(path):
    total_size = 0
    for dirpath, _, filenames in os.walk(path):
        for f in filenames:
            fp = os.path.join(dirpath, f)
            if not os.path.islink(fp) and os.path.exists(fp):
                total_size += os.path.getsize(fp)
    return total_size / (1024 * 1024)

def process_game(line_num, url_raw, processed_urls):
    url = url_raw.strip('"\r\n\t ')
    if not url:
        return
        
    # Fix missing https://github.com/ prefix
    if not url.startswith("http"):
        url = f"https://github.com/{url}"
        
    if url in processed_urls:
        print(f"Skipping [{line_num}] {url} (already in CSV)")
        return
        
    print(f"======================================================\nProcessing [{line_num}] {url}\n======================================================")
    
    game_name = url.strip("/").split("/")[-1]
    repo_path = os.path.join(REPOS_DIR, f"{line_num}_{game_name}")
    
    # 1. Clone
    if os.path.exists(repo_path):
        shutil.rmtree(repo_path, ignore_errors=True)
        
    try:
        subprocess.run(["git", "clone", url, repo_path], check=True)
    except subprocess.CalledProcessError:
        print(f"Failed to clone {url}")
        return
        
    # 1.5 Check size limit (500 MB)
    size_mb = get_dir_size_mb(repo_path)
    if size_mb > 500:
        print(f"Skipping {game_name} (Size too large: {int(size_mb)} MB > 500 MB)")
        shutil.rmtree(repo_path, ignore_errors=True)
        return
        
    # 2. Count commits
    try:
        result = subprocess.run(
            ["git", "-C", repo_path, "rev-list", "--count", "HEAD"], 
            capture_output=True, text=True, check=True
        )
        commits = result.stdout.strip()
    except subprocess.CalledProcessError:
        commits = "0"
        
    # 3. Run ProjectAnalyzer
    try:
        subprocess.run([
            "dotnet", "run", "--project", "Analyzer/ProjectAnalyzer/ProjectAnalyzer.csproj",
            "-c", "Release", "--", repo_path, os.path.join("Evaluation", "Results", game_name)
        ], check=True)
    except subprocess.CalledProcessError:
        print(f"Warning: ProjectAnalyzer failed or exited with an error for {game_name}.")
        
    # 4. Append to CSV
    with csv_lock:
        with open(CSV_FILE, "a", encoding="utf-8") as f:
            f.write(f"{line_num};{url};{commits}\n")
            
    # 5. Clean up repo
    shutil.rmtree(repo_path, ignore_errors=True)
    
    print(f"Finished processing {game_name}. Check Evaluation/Results/{game_name} for details.")


def main():
    try:
        build_analyzers()
    except subprocess.CalledProcessError:
        print("Failed to build one or more analyzers. Exiting.")
        return

    processed_urls = set()
    if os.path.exists(CSV_FILE):
        with open(CSV_FILE, "r", encoding="utf-8") as f:
            for line in f:
                parts = line.strip().split(";")
                if len(parts) >= 2:
                    processed_urls.add(parts[1])

    if not os.path.exists(GAMES_FILE):
        print(f"Could not find {GAMES_FILE}")
        return

    with open(GAMES_FILE, "r", encoding="utf-8") as f:
        lines = f.readlines()

    print("Starting batch analysis...")
    
    # Allow maximum of 4 concurrent jobs
    with ThreadPoolExecutor(max_workers=4) as executor:
        futures = []
        for i, line in enumerate(lines):
            line_num = i
            futures.append(executor.submit(process_game, line_num, line, processed_urls))
            
        for future in as_completed(futures):
            # We can capture exceptions here if needed
            try:
                future.result()
            except Exception as e:
                print(f"An error occurred in a worker thread: {e}")

    print("Batch analysis completed!")

if __name__ == "__main__":
    main()
