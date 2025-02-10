using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using ILGPU.Runtime;
using ILGPU;

namespace GrandChessTree.Shared.Precomputed;
public struct GpuAttackTable
{
    public ArrayView1D<ulong, Stride1D.Dense> LineBitBoardsStraight;
    public ArrayView1D<ulong, Stride1D.Dense> LineBitBoardsDiagonal;
    public ArrayView1D<ulong, Stride1D.Dense> LineBitBoardsStraightToEdge;
    public ArrayView1D<ulong, Stride1D.Dense> LineBitBoardsDiagonalToEdge;
    public ArrayView1D<ulong, Stride1D.Dense> LineBitBoardsInclusive;
    public ArrayView1D<ulong, Stride1D.Dense> WhitePawnPushTable;
    public ArrayView1D<ulong, Stride1D.Dense> BlackPawnPushTable;

    public ArrayView1D<ulong, Stride1D.Dense> KnightAttackTable;
    public ArrayView1D<ulong, Stride1D.Dense> WhitePawnAttackTable;
    public ArrayView1D<ulong, Stride1D.Dense> BlackPawnAttackTable;
    public ArrayView1D<ulong, Stride1D.Dense> KingAttackTable;

    public ArrayView1D<ulong, Stride1D.Dense> RookMagicAttacks;
    public ArrayView1D<ulong, Stride1D.Dense> RookAttackRays;
    public ArrayView1D<ulong, Stride1D.Dense> RookMagics;
    public ArrayView1D<ulong, Stride1D.Dense> BishopMagicAttacks;
    public ArrayView1D<ulong, Stride1D.Dense> BishopAttackRays;
    public ArrayView1D<ulong, Stride1D.Dense> BishopMagics;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong PextRookAttacks(ulong occupation, int square)
    {
        var movementMask = RookAttackRays[square];
        var magicIndexBits = 64 - IntrinsicMath.PopCount(movementMask);
        return RookMagicAttacks[square * 4096 + (int)(((movementMask & occupation) * RookMagics[square]) >> magicIndexBits)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong PextBishopAttacks(ulong occupation, int square)
    {
        var movementMask = BishopAttackRays[square];
        var magicIndexBits = 64 - IntrinsicMath.PopCount(movementMask);

        return BishopMagicAttacks[square * 512 + (int)(((movementMask & occupation) * BishopMagics[square]) >> magicIndexBits)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong DetectPinsStraight(int kingPos, ulong enemySlidingPieces, ulong occupancy)
    {
        var pinned = 0ul;

        while (enemySlidingPieces != 0)
        {
            var attackerPos = enemySlidingPieces.PopLSB(); // Get one attacker at a time

            // Calculate ray between king and attacker
            var pinRay = LineBitBoardsStraight[kingPos * 64 + attackerPos] & occupancy;

            // Count the number of pieces in the pinRay
            if (IntrinsicMath.PopCount(pinRay) == 1)
            {
                pinned |= pinRay; // The single blocking piece is pinned
            }
        }
        return pinned;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong DetectPinsDiagonal(int kingPos, ulong enemySlidingPieces, ulong occupancy)
    {
        var pinned = 0ul;

        while (enemySlidingPieces != 0)
        {
            var attackerPos = enemySlidingPieces.PopLSB(); // Get one attacker at a time

            // Calculate ray between king and attacker
            var pinRay = LineBitBoardsDiagonal[kingPos * 64 + attackerPos] & occupancy;

            // Count the number of pieces in the pinRay
            if (IntrinsicMath.PopCount(pinRay) == 1)
            {
                pinned |= pinRay; // The single blocking piece is pinned
            }
        }
        return pinned;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong GetRayToEdgeStraight(int from, int to)
    {
        return LineBitBoardsStraightToEdge[from * 64 + to];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong GetRayToEdgeDiagonal(int from, int to)
    {
        return LineBitBoardsDiagonalToEdge[from * 64 + to];
    }
}