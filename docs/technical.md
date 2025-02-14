There are four main components to the system:

## Database
We're using PostgreSQL to store tasks, results, accounts, and API keys, with EF Core as the ORM.

### Tables:

- **accounts** – Stores basic information on each user, including ID, name, email, and role.
- **api_keys** – Stores authentication information (the API key itself is stored in a hashed format).
- **perft_items** – At each depth, we split the search into 101,240 tasks. This is done by first searching to depth 4 (197,281 positions), then storing a task for each of the 101,240 unique positions reachable, along with the number of times it occurs. The results from each worker task are multiplied by the occurrences.
- **perft_tasks** – Each row corresponds to the results for a specific task at a certain depth, computed by a single user. To verify correctness, multiple entries will exist for each task/depth, contributed by different users using different hashes.

## API
We use .NET for the API, which is containerized using Docker and deployed on a VPS.

The API is fairly straightforward and allows the client to:

- Query for a batch of tasks (using row-level locking to ensure concurrent requests don't receive the same tasks).
- Submit a batch of results to be stored in the database.
- Query for real-time and total analytics.
- Query for compiled results.

**Note:** The analytics/results endpoints use response caching, so they may take a few minutes to update.

## Web App
We have a React/TypeScript frontend using Tailwind CSS, which provides an interface for viewing the current progress of the active search, including the leaderboard.

## Client
The client is a .NET console app, published using GitHub Actions as a self-contained binary for each platform (Windows, Linux, macOS).

The client performs the actual computations. After gathering initial configuration details (stored in `./gct_config.json`), it periodically requests batches of work from the server to ensure tasks are always available.

Each task requested from the server corresponds to a unique position occurring at depth 4. The client then performs a quick search to depth 2 (cumulatively reaching depth 6) to split the task into subtasks. These subtasks are deduplicated to avoid redundant searches. The client-side subtask result table is also checked to see if a position has already been processed within the session. The remaining positions are then searched to the final depth (`desired_depth - 6`).

Each worker is assigned its own task and searches serially to the final depth, periodically updating the console with progress. Once a worker completes a task, it queues the result for submission to the API before requesting the next task.

**Note:** Choose a number of workers that is less than the number of threads available on your system since they will all run in parallel.

## Search Features
The search algorithm employs several common techniques used in chess engines:

- **Zobrist hashing** for efficient position lookups.
- **PEXT lookup** for sliding piece moves.
- **Legal move generation** using various masks for pinned pieces, attackers, etc.
- **Copy-make technique** for certain non-legal move generations (e.g., en passant moves).
- **Condensed board state** using bitboards for pawns, knights, bishops, rooks, queens, white occupancy, and black occupancy.
- **Pointer-based memory allocation** and **aligned memory** to minimize bounds checking in .NET.
- **Stack-allocated structs** to reduce garbage collection overhead.

There is some inherent inconsistency when using a hash table due to potential collisions. To mitigate this, a unique method is used where the full hash is XORed with the occupancy when checking for collisions. If 12 bits from the original hash are used to look up the relevant entry, these bits are then XORed again with the occupancy so that they perform a different comparison when verifying a match.