```
docker-compose up
dotnet run

 dotnet ef migrations add migration-message
dotnet ef database update 
dotnet ef database update --connection

```

```
docker buildx create --use 
ocker buildx build --platform linux/amd64,linux/arm64 -t aptacode/grand-chess-tree-worker:latest -t aptacode/grand-chess-tree-worker:0.0.2 -f .\GrandChessTree.Client\Dockerfile --push .
docker buildx imagetools inspect aptacode/grand-chess-tree-worker:latest
```

```
 docker build -t aptacode/grand-chess-tree-api:0.0.14 .   
 docker push aptacode/grand-chess-tree-api:0.0.14

 docker-compose down && docker-compose up -d
```