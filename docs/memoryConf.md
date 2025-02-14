### Memory Configuration

- Clone the repo
```
git clone https://github.com/Timmoth/grandchesstree
```
- CD into shared folder and open Perft.cs
```
cd GrandChessTree.Shared
vim Perft.cd 
```
- To edit the memory allocation per worker thread edit it the following line (Line 36) 

```
    public static Summary* AllocateHashTable(int sizeInMb = 512)
```
- Change the value of 512 to as much ram you want to allocate per thread

**Please note do not over allocate as the program will OOM**

- CD into client and run the application

```
cd ../GrandChessTree.Client
# x86
dotnet run -c Release --no-launch-profile 
# ARM
dotnet run -c Release --no-launch-profile --property:DefineConstants="ARM"
```

```
  api_url: https://api.grandchesstree.com/
  api_key: <your_api_key>
  workers: <number_of_threads>
  desired_depth: 12
```
- That's it! 