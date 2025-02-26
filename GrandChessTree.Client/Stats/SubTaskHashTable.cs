using System.Collections.Concurrent;
using GrandChessTree.Shared;

namespace GrandChessTree.Client.Stats
{
    public class SubTaskHashTable
    {
        private readonly ConcurrentDictionary<(ulong hash, int depth), Summary> _dict;
        private readonly ConcurrentQueue<(ulong hash, int depth)> _keysQueue;
        private readonly int _capacity;

        public SubTaskHashTable(int capacity)
        {
            _capacity = capacity;
            _dict = new ConcurrentDictionary<(ulong hash, int depth), Summary>();
            _keysQueue = new ConcurrentQueue<(ulong hash, int depth)>();
        }

        public void Add(ulong hash, int depth, Summary value)
        {
            if (_dict.TryAdd((hash, depth), value))
            {
                _keysQueue.Enqueue((hash, depth));

                if (_dict.Count > _capacity && _keysQueue.TryDequeue(out var oldestKey))
                {
                    _dict.TryRemove(oldestKey, out _);
                }
            }
        }

        public bool TryGetValue(ulong hash, int depth, out Summary value) => _dict.TryGetValue((hash, depth), out value);
    }
}
