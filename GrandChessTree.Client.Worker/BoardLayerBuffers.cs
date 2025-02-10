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
        public ArrayView1D<byte, Stride1D.Dense> CastleRights;
        public ArrayView1D<byte, Stride1D.Dense> EnPassantFile;
        public ArrayView1D<byte, Stride1D.Dense> WhiteKingPos;
        public ArrayView1D<byte, Stride1D.Dense> BlackKingPos;
        public ArrayView1D<int, Stride1D.Dense> PositionIndexes;

        public BoardLayerBuffers(
            ArrayView1D<ulong, Stride1D.Dense> pawnOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> knightOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> bishopOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> rookOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> queenOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> whiteOccupancy,
            ArrayView1D<ulong, Stride1D.Dense> blackOccupancy,
            ArrayView1D<byte, Stride1D.Dense> castleRights,
            ArrayView1D<byte, Stride1D.Dense> enPassantFile,
            ArrayView1D<byte, Stride1D.Dense> whiteKingPos,
            ArrayView1D<byte, Stride1D.Dense> blackKingPos,
            ArrayView1D<int, Stride1D.Dense> positionIndexes)
        {
            PawnOccupancy = pawnOccupancy;
            KnightOccupancy = knightOccupancy;
            BishopOccupancy = bishopOccupancy;
            RookOccupancy = rookOccupancy;
            QueenOccupancy = queenOccupancy;
            WhiteOccupancy = whiteOccupancy;
            BlackOccupancy = blackOccupancy;
            CastleRights = castleRights;
            EnPassantFile = enPassantFile;
            WhiteKingPos = whiteKingPos;
            BlackKingPos = blackKingPos;
            PositionIndexes = positionIndexes;
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
            CastleRights.MemSetToZero();
            EnPassantFile.MemSetToZero();
            WhiteKingPos.MemSetToZero();
            BlackKingPos.MemSetToZero();
            PositionIndexes.MemSetToZero();
        }
    }
}
