namespace MoveGen.App;

public static class Bitboards
{
    public const ulong FileA = 0x0101010101010101UL;
    public const ulong FileH = 0x8080808080808080UL;
    public const ulong NotA  = ~FileA;
    public const ulong NotH  = ~FileH;

    public const ulong Rank1 = 0x00000000000000FFUL;
    public const ulong Rank2 = 0x000000000000FF00UL;
    public const ulong Rank3 = 0x0000000000FF0000UL;
    public const ulong Rank4 = 0x00000000FF000000UL;
    public const ulong Rank5 = 0x000000FF00000000UL;
    public const ulong Rank6 = 0x0000FF0000000000UL;
    public const ulong Rank7 = 0x00FF000000000000UL;
    public const ulong Rank8 = 0xFF00000000000000UL;

    public static ulong ShiftN (ulong b) => b << 8;
    public static ulong ShiftS (ulong b) => b >> 8;
    public static ulong ShiftE (ulong b) => (b << 1) & NotA;
    public static ulong ShiftW (ulong b) => (b >> 1) & NotH;
    public static ulong ShiftNE(ulong b) => (b << 9) & NotA;
    public static ulong ShiftNW(ulong b) => (b << 7) & NotH;
    public static ulong ShiftSE(ulong b) => (b >> 7) & NotA;
    public static ulong ShiftSW(ulong b) => (b >> 9) & NotH;
}
