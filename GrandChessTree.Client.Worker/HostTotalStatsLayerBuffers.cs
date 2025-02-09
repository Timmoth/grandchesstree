using ILGPU;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker
{
    public class HostTotalStatsLayerBuffers : IDisposable
    {
        // 13 bytes each board

        public int BoardCount;

        public MemoryBuffer1D<ulong, Stride1D.Dense> Nodes;
        public MemoryBuffer1D<ulong, Stride1D.Dense> Captures;
        public MemoryBuffer1D<ulong, Stride1D.Dense> Enpassant;
        public MemoryBuffer1D<ulong, Stride1D.Dense> Castles;
        public MemoryBuffer1D<ulong, Stride1D.Dense> Promotions;
        public MemoryBuffer1D<ulong, Stride1D.Dense> DirectCheck;
        public MemoryBuffer1D<ulong, Stride1D.Dense> SingleDiscoveredCheck;
        public MemoryBuffer1D<ulong, Stride1D.Dense> DirectDiscoveredCheck;
        public MemoryBuffer1D<ulong, Stride1D.Dense> DoubleDiscoveredCheck;
        public MemoryBuffer1D<ulong, Stride1D.Dense> DirectCheckmate;
        public MemoryBuffer1D<ulong, Stride1D.Dense> SingleDiscoveredCheckmate;
        public MemoryBuffer1D<ulong, Stride1D.Dense> DirectDiscoverdCheckmate;
        public MemoryBuffer1D<ulong, Stride1D.Dense> DoubleDiscoverdCheckmate;
        public readonly TotalStatsLayerBuffers Buffers;

        public HostTotalStatsLayerBuffers(Accelerator device, int boardCount)
        {
            Nodes = device.Allocate1D<ulong>(boardCount);
            Captures = device.Allocate1D<ulong>(boardCount);
            Enpassant = device.Allocate1D<ulong>(boardCount);
            Castles = device.Allocate1D<ulong>(boardCount);
            Promotions = device.Allocate1D<ulong>(boardCount);
            DirectCheck = device.Allocate1D<ulong>(boardCount);
            SingleDiscoveredCheck = device.Allocate1D<ulong>(boardCount);
            DirectDiscoveredCheck = device.Allocate1D<ulong>(boardCount);
            DoubleDiscoveredCheck = device.Allocate1D<ulong>(boardCount);
            DirectCheckmate = device.Allocate1D<ulong>(boardCount);
            SingleDiscoveredCheckmate = device.Allocate1D<ulong>(boardCount);
            DirectDiscoverdCheckmate = device.Allocate1D<ulong>(boardCount);
            DoubleDiscoverdCheckmate = device.Allocate1D<ulong>(boardCount);

            Buffers = new TotalStatsLayerBuffers(Nodes,
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
