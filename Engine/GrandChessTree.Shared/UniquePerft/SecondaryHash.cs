using System.Numerics;
using System.Runtime.CompilerServices;

namespace GrandChessTree.Shared;

// Independent 64-bit hash of a chess position, computed at leaf time.
// Combined with the primary Zobrist hash, gives a 128-bit composite key
// honest enough to dedupe ~10^12 positions without silent collisions.
public static class SecondaryHash
{
    private static readonly ulong[] PieceSquare = new ulong[12 * 64];
    private static readonly ulong[] Castle = new ulong[16];
    private static readonly ulong[] EpFile = new ulong[9]; // 0..7 file, 8 = none
    private static readonly ulong BlackToMove;

    static SecondaryHash()
    {
        ulong state = 0xC0FFEE12345678ABul; // independent seed from primary Zobrist
        for (int i = 0; i < PieceSquare.Length; i++) PieceSquare[i] = Next(ref state);
        for (int i = 0; i < Castle.Length; i++) Castle[i] = Next(ref state);
        for (int i = 0; i < EpFile.Length; i++) EpFile[i] = Next(ref state);
        BlackToMove = Next(ref state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Next(ref ulong s)
    {
        s ^= s << 13;
        s ^= s >> 7;
        s ^= s << 17;
        return s;
    }

    // pieceIdx encoding:
    //   white: pawn=0, knight=1, bishop=2, rook=3, queen=4, king=5
    //   black: pawn=6, knight=7, bishop=8, rook=9, queen=10, king=11
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(ref ulong h, ulong bb, int pieceIdx)
    {
        int baseIdx = pieceIdx * 64;
        while (bb != 0)
        {
            int sq = BitOperations.TrailingZeroCount(bb);
            bb &= bb - 1;
            h ^= PieceSquare[baseIdx + sq];
        }
    }

    public static ulong Compute(ref Board b, bool whiteToMove, bool epAvailable)
    {
        ulong h = 0;
        ulong w = b.White, bl = b.Black;
        Mix(ref h, w & b.Pawn, 0);
        Mix(ref h, w & b.Knight, 1);
        Mix(ref h, w & b.Bishop, 2);
        Mix(ref h, w & b.Rook, 3);
        Mix(ref h, w & b.Queen, 4);
        h ^= PieceSquare[5 * 64 + b.WhiteKingPos];
        Mix(ref h, bl & b.Pawn, 6);
        Mix(ref h, bl & b.Knight, 7);
        Mix(ref h, bl & b.Bishop, 8);
        Mix(ref h, bl & b.Rook, 9);
        Mix(ref h, bl & b.Queen, 10);
        h ^= PieceSquare[11 * 64 + b.BlackKingPos];

        h ^= Castle[(int)b.CastleRights & 0xF];
        h ^= EpFile[epAvailable && b.EnPassantFile < 8 ? b.EnPassantFile : 8];
        if (!whiteToMove) h ^= BlackToMove;
        return h;
    }
}
