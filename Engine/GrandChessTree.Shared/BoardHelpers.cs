using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;


public partial struct Board
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool IsAttackedByWhiteSliders(int index)
    {
        return (AttackTables.PextBishopAttacks(White | Black, index) &
                (White & (Bishop | Queen))) != 0 ||
               (AttackTables.PextRookAttacks(White | Black, index) & (White & (Rook | Queen))) !=
               0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool IsAttackedByBlackSliders(int index)
    {
        return (AttackTables.PextBishopAttacks(White | Black, index) &
                (Black & (Bishop | Queen))) != 0 ||
               (AttackTables.PextRookAttacks(White | Black, index) & (Black & (Rook | Queen))) !=
               0;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool IsAttackedByWhite(int index)
    {
        return (AttackTables.PextBishopAttacks(White | Black, index) &
                (White & (Bishop | Queen))) != 0 ||
               (AttackTables.PextRookAttacks(White | Black, index) & (White & (Rook | Queen))) !=
               0 ||
               (*(AttackTables.KnightAttackTable + index) & White & Knight) != 0 ||
               (*(AttackTables.BlackPawnAttackTable + index) & White & Pawn) != 0 ||
               (*(AttackTables.KingAttackTable + index) & (1ul << WhiteKingPos)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool IsAttackedByBlack(int index)
    {
        return (AttackTables.PextBishopAttacks(White | Black, index) &
                (Black & (Bishop| Queen))) != 0 ||
               (AttackTables.PextRookAttacks(White | Black, index) & (Black & (Rook | Queen))) !=
               0 ||
               (*(AttackTables.KnightAttackTable + index) & Black & Knight) != 0 ||
               (*(AttackTables.WhitePawnAttackTable + index) & Black & Pawn) != 0 ||
               (*(AttackTables.KingAttackTable + index) & (1ul << BlackKingPos)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong WhiteCheckers()
    {
        return (AttackTables.PextBishopAttacks(White | Black, BlackKingPos) &  (White & (Bishop | Queen))) |
               (AttackTables.PextRookAttacks(White | Black, BlackKingPos) & (White & (Rook | Queen))) |
               (*(AttackTables.KnightAttackTable + BlackKingPos) & White & Knight) |
               (*(AttackTables.BlackPawnAttackTable + BlackKingPos) & White & Pawn);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong BlackCheckers()
    {
        return (AttackTables.PextBishopAttacks(White | Black, WhiteKingPos) &
                (Black & (Bishop| Queen))) |
               (AttackTables.PextRookAttacks(White | Black, WhiteKingPos) & (Black & (Rook | Queen))) |
               (*(AttackTables.KnightAttackTable + WhiteKingPos) & Black & Knight) |
               (*(AttackTables.WhitePawnAttackTable + WhiteKingPos) & Black & Pawn);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong WhiteKingDangerSquares()
    {
        var attackers = 0ul;

        var occupancy = (White | Black) ^ (1ul << WhiteKingPos);

        var positions = Black & Pawn;
        while (positions != 0) attackers |= *(AttackTables.BlackPawnAttackTable + positions.PopLSB());

        positions = Black & Knight;
        while (positions != 0) attackers |= *(AttackTables.KnightAttackTable + positions.PopLSB());

        positions = Black & (Bishop | Queen);
        while (positions != 0) attackers |= AttackTables.PextBishopAttacks(occupancy, positions.PopLSB());

        positions = Black & (Rook | Queen);
        while (positions != 0) attackers |= AttackTables.PextRookAttacks(occupancy, positions.PopLSB());

        attackers |= *(AttackTables.KingAttackTable + BlackKingPos);

        return attackers;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong BlackKingDangerSquares()
    {
        var attackers = 0ul;

        var occupancy = (White | Black) ^ (1ul << BlackKingPos);

        var positions = White & Pawn;
        while (positions != 0) attackers |= *(AttackTables.WhitePawnAttackTable + positions.PopLSB());

        positions = White & Knight;
        while (positions != 0) attackers |= *(AttackTables.KnightAttackTable + positions.PopLSB());

        positions = White & (Bishop | Queen);
        while (positions != 0) attackers |= AttackTables.PextBishopAttacks(occupancy, positions.PopLSB());

        positions = White & (Rook | Queen);
        while (positions != 0) attackers |= AttackTables.PextRookAttacks(occupancy, positions.PopLSB());

        attackers |= *(AttackTables.KingAttackTable + WhiteKingPos);

        return attackers;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong WhiteKingPinnedRay()
    {
        return AttackTables.DetectPinsDiagonal(WhiteKingPos, (Black & (Bishop| Queen)), White | Black) | 
               AttackTables.DetectPinsStraight(WhiteKingPos, (Black & (Rook | Queen)), White | Black);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong BlackKingPinnedRay()
    {
        return AttackTables.DetectPinsDiagonal(BlackKingPos, (White & (Bishop | Queen)), White | Black) | 
               AttackTables.DetectPinsStraight(BlackKingPos, (White & (Rook | Queen)), White | Black);
    }

}