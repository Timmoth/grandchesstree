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
        public readonly MemoryBuffer1D<byte, Stride1D.Dense> CastleRights;
        public readonly MemoryBuffer1D<byte, Stride1D.Dense> EnPassantFile;
        public readonly MemoryBuffer1D<byte, Stride1D.Dense> WhiteKingPos;
        public readonly MemoryBuffer1D<byte, Stride1D.Dense> BlackKingPos;

        public readonly BoardLayerBuffers Buffers;

        public HostBoardLayerBuffers(Accelerator device, int boardCount)
        {
            PawnOccupancy = device.Allocate1D<ulong>(boardCount);
            KnightOccupancy = device.Allocate1D<ulong>(boardCount);
            BishopOccupancy = device.Allocate1D<ulong>(boardCount);
            RookOccupancy = device.Allocate1D<ulong>(boardCount);
            QueenOccupancy = device.Allocate1D<ulong>(boardCount);
            WhiteOccupancy = device.Allocate1D<ulong>(boardCount);
            BlackOccupancy = device.Allocate1D<ulong>(boardCount);
            CastleRights = device.Allocate1D<byte>(boardCount);
            EnPassantFile = device.Allocate1D<byte>(boardCount);
            WhiteKingPos = device.Allocate1D<byte>(boardCount);
            BlackKingPos = device.Allocate1D<byte>(boardCount);

            Buffers = new BoardLayerBuffers(PawnOccupancy, KnightOccupancy, BishopOccupancy, RookOccupancy, QueenOccupancy, WhiteOccupancy, BlackOccupancy, CastleRights, EnPassantFile, WhiteKingPos, BlackKingPos);
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
            CastleRights.Dispose();
            EnPassantFile.Dispose();
            WhiteKingPos.Dispose();
            BlackKingPos.Dispose();
        }
    }
}
