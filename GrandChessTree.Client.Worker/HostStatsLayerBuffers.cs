using ILGPU;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker
{
    public class HostStatsLayerBuffers : IDisposable
    {
        // 13 bytes each board

        public int BoardCount;

        public MemoryBuffer1D<byte, Stride1D.Dense> Nodes;
        public MemoryBuffer1D<byte, Stride1D.Dense> Captures;
        public MemoryBuffer1D<byte, Stride1D.Dense> Enpassant;
        public MemoryBuffer1D<byte, Stride1D.Dense> Castles;
        public MemoryBuffer1D<byte, Stride1D.Dense> Promotions;
        public MemoryBuffer1D<byte, Stride1D.Dense> DirectCheck;
        public MemoryBuffer1D<byte, Stride1D.Dense> SingleDiscoveredCheck;
        public MemoryBuffer1D<byte, Stride1D.Dense> DirectDiscoveredCheck;
        public MemoryBuffer1D<byte, Stride1D.Dense> DoubleDiscoveredCheck;
        public MemoryBuffer1D<byte, Stride1D.Dense> DirectCheckmate;
        public MemoryBuffer1D<byte, Stride1D.Dense> SingleDiscoveredCheckmate;
        public MemoryBuffer1D<byte, Stride1D.Dense> DirectDiscoverdCheckmate;
        public MemoryBuffer1D<byte, Stride1D.Dense> DoubleDiscoverdCheckmate;
        public readonly StatsLayerBuffers Buffers;

        public HostStatsLayerBuffers(Accelerator device, int boardCount)
        {
            Nodes = device.Allocate1D<byte>(boardCount);
            Captures = device.Allocate1D<byte>(boardCount);
            Enpassant = device.Allocate1D<byte>(boardCount);
            Castles = device.Allocate1D<byte>(boardCount);
            Promotions = device.Allocate1D<byte>(boardCount);
            DirectCheck = device.Allocate1D<byte>(boardCount);
            SingleDiscoveredCheck = device.Allocate1D<byte>(boardCount);
            DirectDiscoveredCheck = device.Allocate1D<byte>(boardCount);
            DoubleDiscoveredCheck = device.Allocate1D<byte>(boardCount);
            DirectCheckmate = device.Allocate1D<byte>(boardCount);
            SingleDiscoveredCheckmate = device.Allocate1D<byte>(boardCount);
            DirectDiscoverdCheckmate = device.Allocate1D<byte>(boardCount);
            DoubleDiscoverdCheckmate = device.Allocate1D<byte>(boardCount);

            Buffers = new StatsLayerBuffers(Nodes,
                Captures,
                Enpassant,
                Castles,
                Promotions,
                DirectCheck,
                SingleDiscoveredCheck,
                DirectDiscoveredCheck,
                DoubleDiscoveredCheck,
                DirectCheckmate,
                SingleDiscoveredCheckmate,
                DirectDiscoverdCheckmate,
                DoubleDiscoverdCheckmate);
        }

        public void Dispose()
        {
            Nodes.Dispose();
            Captures.Dispose();
            Enpassant.Dispose();
            Castles.Dispose();
            Promotions.Dispose();
            DirectCheck.Dispose();
            SingleDiscoveredCheck.Dispose();
            DirectDiscoveredCheck.Dispose();
            DoubleDiscoveredCheck.Dispose();
            DirectCheckmate.Dispose();
            SingleDiscoveredCheckmate.Dispose();
            DirectDiscoverdCheckmate.Dispose();
            DoubleDiscoverdCheckmate.Dispose();
        }
    }
}
