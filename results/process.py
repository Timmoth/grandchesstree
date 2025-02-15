import pandas as pd
import numpy as np

def load_text_file(filename):
    """Reads test.txt and parses it into a list of tuples (hash, nodes, occurrences)."""
    data = []
    
    try:
        with open(filename, 'r') as file:
            for line in file:
                parts = line.strip().split(',')
                if len(parts) == 3:
                    hash_val, nodes, occurrences = parts
                    data.append((np.uint64(hash_val), np.uint64(nodes), int(occurrences)))  # Store as tuple
        return data
    except FileNotFoundError:
        print(f"Error: File '{filename}' not found.")
        return None
    except Exception as e:
        print(f"Error reading the file: {e}")
        return None

def main():
    try:
        # Ask the user for positionId and depth
        position_id = int(input("Enter the positionId (integer): "))
        depth = int(input("Enter the depth (integer): "))
    except ValueError:
        print("Invalid input. Please enter integer values.")
        return

    # Construct the CSV filename
    csv_filename = f"./perft_p{position_id}_d{depth}_dump.csv"
    
    # Load the CSV file
    try:
        df = pd.read_csv(csv_filename, dtype={"hash": np.uint64, "nodes": np.uint64, "occurrences": int})
        print(f"CSV loaded successfully! {len(df)} rows")

        # Ensure required columns exist
        if not {"hash", "nodes", "occurrences"}.issubset(df.columns):
            print("Error: The CSV file does not contain required columns 'hash', 'nodes', and 'occurrences'.")
            return

        # Convert CSV data to a dictionary for fast lookup {hash -> (nodes, occurrences)}
        csv_data = {row["hash"]: (row["nodes"], row["occurrences"]) for _, row in df.iterrows()}

        # Load and parse test.txt
        text_data = load_text_file("test.txt")
        if text_data is None:
            return  # Stop execution if text file loading fails
        
        match_count = 0
        delta_count = 0
        miss_count = 0
        total_delta = 0
        total_sum = (df["nodes"] * df["occurrences"]).sum()
        bad_hashes = []
        # Check if each (hash, nodes, occurrences) pair from test.txt exists in the CSV
        for hash_val, nodes, occurrences in text_data:
            if hash_val in csv_data:
                csv_nodes, csv_occurrences = csv_data[hash_val]  # Get nodes & occurrences from CSV
                
                if nodes == csv_nodes:
                    match_count += 1
                else:
                    bad_hashes.append(hash_val)
                    delta_count += 1
                    delta = csv_nodes - nodes
                    total_delta += delta * csv_occurrences
                    print(f"Delta {delta_count}: Hash={hash_val} | Test Nodes={nodes} | CSV Nodes={csv_nodes} | Δ={delta}")
            else:
                miss_count += 1
                print(f"Error {miss_count}: Hash={hash_val} not found in CSV!")

        print(f"\nSummary:")
        print(f"✔️ Exact Matches: {match_count}")
        print(f"⚠️ Node Mismatches (Delta): {delta_count} {total_delta}")
        print(f"❌ Missing Hashes: {miss_count}")
        print(f"actual: {total_sum}")
        print(f"delta: {total_delta}")
        print(f"corrected: {total_sum - total_delta}")

        if bad_hashes:
            hash_list_str = ", ".join(map(str, bad_hashes))  # Convert list to comma-separated string

            sql_query = f"SELECT * FROM public.perft_tasks t JOIN public.perft_items i ON t.perft_item_id = i.id WHERE t.depth = 9 and i.hash IN ({hash_list_str});"
        
            print("\nGenerated SQL Query:")
            print(sql_query)

    except FileNotFoundError:
        print(f"Error: CSV file '{csv_filename}' not found.")
    except Exception as e:
        print(f"An error occurred while processing the CSV: {e}")

if __name__ == '__main__':
    main()
