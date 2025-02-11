using GrandChessTree.Shared.Precomputed;
using System.ComponentModel;
using ILGPU;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker
{

    public struct BoardLayerBuffers
    {
        public ArrayView1D<ulong, Stride1D.Dense> PawnOccupancy;
        public ArrayView1D<ulong, Stride1D.Dense> KnightOccupancy;
        public ArrayView1D<ulong, Stride1D.Dense> BishopOccupancy;
        public ArrayView1D<ulong, Stride1D.Dense> RookOccupancy;
        public ArrayView1D<ulong, Stride1D.Dense> QueenOccupancy;
        public ArrayView1D<ulong, Stride1D.Dense> WhiteOccupancy;
        public ArrayView1D<ulong, Stride1D.Dense> BlackOccupancy;
        public ArrayView1D<uint, Stride1D.Dense> NonOccupancyState;


        public ArrayView1D<int, Stride1D.Dense> L1BoardIndexes;
        public ArrayView1D<int, Stride1D.Dense> L2BoardIndexes;
        public ArrayView1D<int, Stride1D.Dense> L3BoardIndexes;

        public ArrayView1D<int, Stride1D.Dense> L1MoveIndexes;
        public ArrayView1D<int, Stride1D.Dense> L2MoveIndexes;
        public ArrayView1D<int, Stride1D.Dense> L3MoveIndexes;

        public BoardLayerBuffers(
            ArrayView1D<ulong, Stride1D.Dense> pawnOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> knightOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> bishopOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> rookOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> queenOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> whiteOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> blackOccupancy,
            ArrayView1D<uint, Stride1D.Dense> nonOccupancyState,
            ArrayView1D<int, Stride1D.Dense> l1BoardIndexes,
            ArrayView1D<int, Stride1D.Dense> l2BoardIndexes,
            ArrayView1D<int, Stride1D.Dense> l3BoardIndexes,
            ArrayView1D<int, Stride1D.Dense> l1MoveIndexes,
            ArrayView1D<int, Stride1D.Dense> l2MoveIndexes,
            ArrayView1D<int, Stride1D.Dense> l3MoveIndexes
            )
        {
            PawnOccupancy = pawnOccupancy;
            KnightOccupancy = knightOccupancy;
            BishopOccupancy = bishopOccupancy;
            RookOccupancy = rookOccupancy;
            QueenOccupancy = queenOccupancy;
            WhiteOccupancy = whiteOccupancy;
            BlackOccupancy = blackOccupancy;
            NonOccupancyState = nonOccupancyState;
            L1BoardIndexes = l1BoardIndexes;
            L2BoardIndexes = l2BoardIndexes;
            L3BoardIndexes = l3BoardIndexes;
            L1MoveIndexes = l1MoveIndexes;
            L2MoveIndexes = l2MoveIndexes;
            L3MoveIndexes = l3MoveIndexes;
        }

        internal void MemSetZero()
        {
            PawnOccupancy.MemSetToZero();
            KnightOccupancy.MemSetToZero();
            BishopOccupancy.MemSetToZero();
            RookOccupancy.MemSetToZero();
            QueenOccupancy.MemSetToZero();
            WhiteOccupancy.MemSetToZero();
            BlackOccupancy.MemSetToZero();
            NonOccupancyState.MemSetToZero();
            L1BoardIndexes.MemSetToZero();
            L2BoardIndexes.MemSetToZero();
            L3BoardIndexes.MemSetToZero();
            L1MoveIndexes.MemSetToZero();
            L2MoveIndexes.MemSetToZero();
            L3MoveIndexes.MemSetToZero();
        }

        internal void MemSetZero2()
        {
            L1BoardIndexes.MemSetToZero();
            L2BoardIndexes.MemSetToZero();
            L3BoardIndexes.MemSetToZero();
            L1MoveIndexes.MemSetToZero();
            L2MoveIndexes.MemSetToZero();
            L3MoveIndexes.MemSetToZero();
        }
    }
}
