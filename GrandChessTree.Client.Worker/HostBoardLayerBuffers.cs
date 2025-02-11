using ILGPU;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker
{
    public class HostBoardLayerBuffers : IDisposable
    {
        // 7 * 8 + 4 = 60 bytes each board
        public int BoardCount;

        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> PawnOccupancy;
        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> KnightOccupancy;
        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> BishopOccupancy;
        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> RookOccupancy;
        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> QueenOccupancy;
        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> WhiteOccupancy;
        public readonly MemoryBuffer1D<ulong, Stride1D.Dense> BlackOccupancy;
        public readonly MemoryBuffer1D<uint, Stride1D.Dense> NonOccupancyState;


        public readonly BoardLayerBuffers Buffers;

        public MemoryBuffer1D<int, Stride1D.Dense> L1BoardIndexes;
        public MemoryBuffer1D<int, Stride1D.Dense> L2BoardIndexes;
        public MemoryBuffer1D<int, Stride1D.Dense> L3BoardIndexes;

        public MemoryBuffer1D<int, Stride1D.Dense> L1MoveIndexes;
        public MemoryBuffer1D<int, Stride1D.Dense> L2MoveIndexes;
        public MemoryBuffer1D<int, Stride1D.Dense> L3MoveIndexes;

        public HostBoardLayerBuffers(Accelerator device, int boardCount, int l1Size, int l2Size, int l3Size)
        {
            PawnOccupancy = device.Allocate1D<ulong>(boardCount);
            KnightOccupancy = device.Allocate1D<ulong>(boardCount);
            BishopOccupancy = device.Allocate1D<ulong>(boardCount);
            RookOccupancy = device.Allocate1D<ulong>(boardCount);
            QueenOccupancy = device.Allocate1D<ulong>(boardCount);
            WhiteOccupancy = device.Allocate1D<ulong>(boardCount);
            BlackOccupancy = device.Allocate1D<ulong>(boardCount);
            NonOccupancyState = device.Allocate1D<uint>(boardCount);
            L1BoardIndexes = device.Allocate1D<int>(l1Size);
            L2BoardIndexes = device.Allocate1D<int>(l2Size);
            L3BoardIndexes = device.Allocate1D<int>(l3Size);
            L1MoveIndexes = device.Allocate1D<int>(l1Size);
            L2MoveIndexes = device.Allocate1D<int>(l2Size);
            L3MoveIndexes = device.Allocate1D<int>(l3Size);

            Buffers = new BoardLayerBuffers(PawnOccupancy, KnightOccupancy, BishopOccupancy, RookOccupancy, QueenOccupancy, WhiteOccupancy,
                BlackOccupancy, NonOccupancyState,
                L1BoardIndexes, L2BoardIndexes, L3BoardIndexes, L1MoveIndexes, L2MoveIndexes, L3MoveIndexes);
        }

        public void Dispose()
        {
            PawnOccupancy.Dispose();
            KnightOccupancy.Dispose();
            BishopOccupancy.Dispose();
            RookOccupancy.Dispose();
            QueenOccupancy.Dispose();
            WhiteOccupancy.Dispose();
            BlackOccupancy.Dispose();
            NonOccupancyState.Dispose();
        }
    }
}
