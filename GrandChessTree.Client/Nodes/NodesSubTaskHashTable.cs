using System.Collections.Concurrent;
using GrandChessTree.Shared;

namespace GrandChessTree.Client.Stats
{
    public class NodesSubTaskHashTable
    {
        private readonly ConcurrentDictionary<(string fen, int depth), ulong> _dict;
        private readonly ConcurrentQueue<(string fen, int depth)> _keysQueue;
        private readonly int _capacity;

        public NodesSubTaskHashTable(int capacity)
        {
            _capacity = capacity;
            _dict = new ConcurrentDictionary<(string fen, int depth), ulong>();
            _keysQueue = new ConcurrentQueue<(string fen, int depth)>();
        }

        public void Add(string fen, int depth, ulong value)
        {
            if (_dict.TryAdd((fen, depth), value))
            {
                _keysQueue.Enqueue((fen, depth));

                if (_dict.Count > _capacity && _keysQueue.TryDequeue(out var oldestKey))
                {
                    _dict.TryRemove(oldestKey, out _);
                }
            }
        }

        public bool TryGetValue(string fen, int depth, out ulong value) => _dict.TryGetValue((fen, depth), out value);
    }
}
