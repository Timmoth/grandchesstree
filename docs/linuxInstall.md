### Linux install instructions
- Get in touch, you'll need an apikey in order to connect to the server. The easiest way is to [join the discord group](https://discord.gg/cTu3aeCZVe)
- Download the [latest client](https://github.com/Timmoth/grandchesstree/releases) (windows, linux, mac are all supported (including ARM builds!))
```
    wget https://github.com/Timmoth/grandchesstree/releases/download/GrandChessTree-0.0.5/GrandChessTree_linux_x64
    chmod 755 GrandChessTree_linux_x64
    ./GrandChessTree_linux_x64
```
- Run the client and answer the questions with the following:
```
  api_url: https://api.grandchesstree.com/
  api_key: <your_api_key>
  workers: <number_of_threads>
  worker_id: 0
```
- That's it! 

**Note that you should pick a number of workers that corrosponds to less then the number of threads in your system since they will all be running in parrallel. **

**You can close the program at any time, but do note that any progress on incomplete tasks will be lost.**

**To exit the program correctly use the following**
```
    qg+ENTER
```

If you want to run from source (for apple silicon you may need to)
```
git clone https://github.com/Timmoth/grandchesstree
cd GrandChessTree.Client
# x86
dotnet run -c Release --no-launch-profile 
# ARM
dotnet run -c Release --no-launch-profile --property:DefineConstants="ARM"
```

[Alternatively you can also run the worker in docker](https://hub.docker.com/r/aptacode/grand-chess-tree-worker)

```
docker run -e api_key="<your_api_key>" -e api_url="https://api.grandchesstree.com/" -e workers=4 -e depth=12 aptacode/grand-chess-tree-worker
```

### Making sense of the output
- worker: the index of the worker
- nps: the number of nodes per second the worker is able to process
- nodes: the number of nodes that have been processed for this task so far
- sub_tasks: completed / total sub tasks for the workers currently assigned tasks
- tasks: the number of full tasks that the worker has completed
- fen: the position the worker is currently processing

The last three lines show:

- The number of subtasks that have been completed and the percentage of them that were duplicates of work you've already done
- The number of tasks you've submitted this session, and the number of tasks that you've completed but are waiting to be submitted
- Computed stats - this shows the amount and rate of nodes you're processing accross all of your workers (excluding caching)
- Effective stats - this shows the amount and rate of nodes that your sending to the server, it will be considerably higher then the rate your system can directly run the search since it includes cached nodes.

```
| worker | nps    | nodes  | sub_tasks | tasks | fen                                                             |
|--------|--------|--------|-----------|-------|-----------------------------------------------------------------|
| 0      | 179.3m | 23.8b  | 262/417   | 16    | rnbqkbnr/1ppppp1p/6p1/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1   |
| 1      | 166.5m | 42.7b  | 304/586   | 12    | rnbqkbnr/1ppp1ppp/4p3/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1   |
| 2      | 144.6m | 92.6b  | 469/565   | 11    | 1nbqkbnr/1ppppppp/r7/p7/8/P4N2/1PPPPPPP/RNBQKB1R w KQk - 0 1    |
| 3      | 193.3m | 13.4b  | 76/398    | 16    | rnbqkb1r/p1pppppp/1p5n/8/1P6/P7/2PPPPPP/RNBQKBNR w KQkq - 0 1   |
| 4      | 180.3m | 21.6b  | 244/380   | 13    | rnbqkbnr/1pppppp1/7p/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1    |
| 5      | 162.7m | 49.8b  | 210/550   | 15    | rnbqkbnr/1pp1pppp/8/p2p4/8/P6N/1PPPPPPP/RNBQKB1R w KQkq d6 0 1  |
| 6      | 164.1m | 375.6b | 612/752   | 12    | rnbqkbnr/1pp1pppp/8/p2p4/8/P2P4/1PP1PPPP/RNBQKBNR w KQkq d6 0 1 |
| 7      | 159.2m | 19.4b  | 164/418   | 14    | rnbqkbnr/1pppppp1/8/p6p/8/P6N/1PPPPPPP/RNBQKB1R w KQkq h6 0 1   |
| 8      | 180m   | 56.9b  | 380/530   | 18    | rnbqkbnr/1pp1pppp/3p4/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1   |
| 9      | 180.9m | 354.6b | 596/803   | 13    | rnbqkbnr/1ppp1ppp/8/p3p3/8/P2P4/1PP1PPPP/RNBQKBNR w KQkq e6 0 1 |
| 10     | 169.4m | 174.7b | 549/618   | 13    | rnbqkbnr/1ppp1ppp/8/p3p3/6P1/P7/1PPPPP1P/RNBQKBNR w KQkq e6 0 1 |
| 11     | 165.6m | 39.3b  | 236/588   | 16    | rnbqkbnr/1ppp1ppp/8/p3p3/8/P6N/1PPPPPPP/RNBQKB1R w KQkq e6 0 1  |
| 12     | 159.1m | 104.3b | 358/579   | 14    | rnbqkbnr/1pp1pppp/8/p2p4/7P/P7/1PPPPPP1/RNBQKBNR w KQkq d6 0 1  |
| 13     | 170.4m | 14.1b  | 75/436    | 14    | rnbqkb1r/p1pppppp/1p3n2/8/1P6/P7/2PPPPPP/RNBQKBNR w KQkq - 0 1  |
| 14     | 150.5m | 18.3b  | 245/417   | 17    | r1bqkbnr/1ppppppp/n7/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1    |
| 15     | 158.5m | 30.9b  | 314/379   | 16    | rnbqkbnr/1pppp1pp/5p2/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1   |
| 16     | 162.7m | 13.6b  | 224/436   | 11    | r1bqkbnr/1ppppppp/2n5/p7/8/P6N/1PPPPPPP/RNBQKB1R w KQkq - 0 1   |
| 17     | 154m   | 21.5b  | 185/418   | 11    | rnbqkbnr/1ppppp1p/8/p5p1/8/P6N/1PPPPPPP/RNBQKB1R w KQkq g6 0 1  |
| 18     | 199.1m | 105.7b | 525/558   | 13    | rnbqkbnr/1pp1pppp/3p4/p7/7P/P7/1PPPPPP1/RNBQKBNR w KQkq - 0 1   |
| 19     | 174.7m | 4.8b   | 42/422    | 11    | rnbqkbnr/p1pppppp/8/1p6/8/P1P5/1P1PPPPP/RNBQKBNR w KQkq - 0 1   |
| 20     | 196.6m | 28.9b  | 190/397   | 16    | rnbqkbnr/1pppp1pp/8/p4p2/8/P6N/1PPPPPPP/RNBQKB1R w KQkq f6 0 1  |
| 21     | 170.9m | 14.1b  | 72/436    | 15    | r1bqkbnr/p1pppppp/1pn5/8/1P6/P7/2PPPPPP/RNBQKBNR w KQkq - 0 1   |
| 22     | 160.1m | 4.6b   | 42/510    | 12    | rn1qkbnr/pbpppppp/1p6/8/1P6/P7/2PPPPPP/RNBQKBNR w KQkq - 0 1    |
| 23     | 176.7m | 5b     | 30/432    | 3     | rn1qkbnr/p1pppppp/bp6/8/1P6/P7/2PPPPPP/RNBQKBNR w KQkq - 0 1    |

completed 159k subtasks (29% cache hits), submitted 322 tasks (0 pending)
[computed stats] 39.3t nodes at 4.1bnps
[effective stats] 129.6t nodes at 13.3bnps
                                                                                                                                     
```

## Running the toolkit

```
git clone https://github.com/Timmoth/grandchesstree
cd GrandChessTree.Toolkit
# x86
dotnet run -c Release --no-launch-profile 
# ARM
dotnet run -c Release --no-launch-profile --property:DefineConstants="ARM"

----The Grand Chess Tree Toolkit----
perft_test:5:512:10

```
The command to run a perft_test locally is:

```
perft_test:depth:hash_mb:iterations
```

- depth: the depth to search, anything over 7 is going to take a while...
- hash_mb: the size in MB to allocate to the hash table
- iterations: the number of test iterations to run