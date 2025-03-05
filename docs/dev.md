```
docker-compose up
dotnet run

 dotnet ef migrations add migration-message
dotnet ef database update 
dotnet ef database update --connection

```

```
docker buildx create --use 
docker buildx build --platform linux/amd64,linux/arm64 -t aptacode/grand-chess-tree-worker:latest -t aptacode/grand-chess-tree-worker:0.0.2 -f .\GrandChessTree.Client\Dockerfile --push .
docker buildx imagetools inspect aptacode/grand-chess-tree-worker:latest
```

```
 docker build -t aptacode/grand-chess-tree-api:0.0.14 .   
 docker push aptacode/grand-chess-tree-api:0.0.14

 docker-compose down && docker-compose up -d
```

## Manually update perft jobs row
```
UPDATE public.perft_jobs AS pj
SET 
    completed_fast_tasks = sub.completed_fast_tasks,
    completed_full_tasks = sub.completed_full_tasks,
    full_task_nodes = sub.full_task_nodes,
    fast_task_nodes = sub.fast_task_nodes,
    total_tasks = sub.count,
    verified_tasks = sub.verified_tasks
FROM (
    SELECT 
        SUM(CASE WHEN fast_task_finished_at > 0 THEN 1 ELSE 0 END) AS completed_fast_tasks,
        SUM(CASE WHEN full_task_finished_at > 0 THEN 1 ELSE 0 END) AS completed_full_tasks,
        SUM(full_task_nodes * occurrences) AS full_task_nodes,
        SUM(fast_task_nodes * occurrences) AS fast_task_nodes,
        COUNT(*) AS count,
        SUM(CASE WHEN full_task_nodes = fast_task_nodes THEN 1 ELSE 0 END) AS verified_tasks
    FROM public.perft_tasks_v3
    WHERE depth = 10 AND root_position_id = 1
) AS sub
WHERE pj.id = 2;
```

## Manually update full task contributions
```
INSERT INTO public.perft_contributions (
    account_id, root_position_id, depth, 
    full_task_nodes, completed_full_tasks, 
    completed_fast_tasks, fast_task_nodes
)
SELECT 
    full_task_account_id AS account_id,
    root_position_id,
    depth,
    SUM(full_task_nodes * occurrences) AS full_task_nodes,
    COUNT(*) AS completed_full_tasks,
    0 AS completed_fast_tasks,  -- Default value since fast tasks are not in the query
    0.0 AS fast_task_nodes       -- Default value since fast tasks are not in the query
FROM public.perft_tasks_v3
WHERE depth = <DEPTH> 
  AND root_position_id = <ID> 
  AND full_task_finished_at > 0
GROUP BY full_task_account_id, depth, root_position_id
ON CONFLICT (account_id, root_position_id, depth) 
DO UPDATE SET 
    full_task_nodes = EXCLUDED.full_task_nodes,
    completed_full_tasks = EXCLUDED.completed_full_tasks;
```

## Manually update fast task contributions
```
INSERT INTO public.perft_contributions (
    account_id, root_position_id, depth, 
    fast_task_nodes, completed_fast_tasks, 
    completed_full_tasks, full_task_nodes
)
SELECT 
    fast_task_account_id AS account_id,
    root_position_id,
    depth,
    SUM(fast_task_nodes * occurrences) AS fast_task_nodes,
    COUNT(*) AS completed_fast_tasks,
    0 AS completed_full_tasks,  -- Default value since fast tasks are not in the query
    0.0 AS full_task_nodes       -- Default value since fast tasks are not in the query
FROM public.perft_tasks_v3
WHERE depth = 10 
  AND root_position_id = 1 
  AND fast_task_finished_at > 0
GROUP BY fast_task_account_id, depth, root_position_id
ON CONFLICT (account_id, root_position_id, depth) 
DO UPDATE SET 
    fast_task_nodes = EXCLUDED.fast_task_nodes,
    completed_fast_tasks = EXCLUDED.completed_fast_tasks;
```

## Manually release stalled tasks
```
UPDATE public.perft_tasks_v3
SET full_task_started_at = 0
WHERE depth = 10 and root_position_id = 1 and full_task_finished_at = 0

UPDATE public.perft_tasks_v3
SET fast_task_started_at = 0
WHERE depth = 10 and root_position_id = 1 and fast_task_finished_at = 0

```

## Get corrupt rows
```
SELECT Count(*) from public.perft_tasks_v3
WHERE depth = 10 and root_position_id = 1 and full_task_finished_at > 0 and fast_task_finished_at > 0 and full_task_nodes != fast_task_nodes
```

## Get contributor summary
```
SELECT 
    p.completed_full_tasks AS full_tasks,
    p.completed_fast_tasks AS fast_tasks,
    a.name
FROM public.perft_contributions p
JOIN public.accounts a ON p.account_id = a.id
WHERE p.depth = 10
ORDER BY p.completed_full_tasks DESC
```

## Get perft results as json
```
WITH aggregated AS (
    SELECT 
        SUM(t.full_task_nodes * t.occurrences) AS nodes,
        SUM(t.captures * t.occurrences) AS captures,
        SUM(t.enpassants * t.occurrences) AS enpassants,
        SUM(t.castles * t.occurrences) AS castles,
        SUM(t.promotions * t.occurrences) AS promotions,
        SUM(t.direct_checks * t.occurrences) AS direct_checks,
        SUM(t.single_discovered_checks * t.occurrences) AS single_discovered_checks,
        SUM(t.direct_discovered_checks * t.occurrences) AS direct_discovered_checks,
        SUM(t.double_discovered_checks * t.occurrences) AS double_discovered_checks,
        SUM(t.direct_mates * t.occurrences) AS direct_mates,
        SUM(t.single_discovered_mates * t.occurrences) AS single_discovered_mates,
        SUM(t.direct_discovered_mates * t.occurrences) AS direct_discovered_mates,
        SUM(t.double_discovered_mates * t.occurrences) AS double_discovered_mates,
		MIN(t.full_task_finished_at) as start,
		MAX(t.full_task_finished_at) as end
    FROM public.perft_tasks_v3 t
    WHERE t.root_position_id = 1 AND t.depth = 10
)
SELECT row_to_json(a) 
FROM (
    SELECT *,
        (direct_checks + single_discovered_checks + direct_discovered_checks + double_discovered_checks) AS total_checks,
        (direct_mates + single_discovered_mates + direct_discovered_mates + double_discovered_mates) AS total_mates
    FROM aggregated
) a;

```

### Get Contributors json
```
WITH aggregated AS (
SELECT 
    a.id AS id,
    a.name AS name,
	p.full_task_nodes AS nodes,
    p.completed_full_tasks AS tasks,
	p.fast_task_nodes AS fast_nodes,
    p.completed_fast_tasks AS fast_tasks,
	0 as compute_time
FROM public.perft_contributions p
JOIN public.accounts a ON p.account_id = a.id
WHERE p.depth = 10
ORDER BY p.completed_full_tasks DESC
)
SELECT row_to_json(a) 
FROM (
    SELECT *
    FROM aggregated
) a;
```