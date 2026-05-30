namespace GrandChessTree.Shared.Helpers;

public static class Constants
{
    public const int WhiteEnpassantOffset = 5 * 8;
    public const int BlackEnpassantOffset = 2 * 8;

    public const ulong BlackKingSideCastleRookPosition = 1UL << 63;
    public const ulong BlackKingSideCastleEmptyPositions = (1UL << 61) | (1UL << 62);
    public const ulong BlackQueenSideCastleRookPosition = 1UL << 56;
    public const ulong BlackQueenSideCastleEmptyPositions = (1UL << 57) | (1UL << 58) | (1UL << 59);

    public const ulong WhiteKingSideCastleRookPosition = 1UL << 7;
    public const ulong WhiteKingSideCastleEmptyPositions = (1UL << 6) | (1UL << 5);
    public const ulong WhiteQueenSideCastleRookPosition = 1UL;
    public const ulong WhiteQueenSideCastleEmptyPositions = (1UL << 1) | (1UL << 2) | (1UL << 3);
    
    
    
    public const ulong NotAFile = 0xFEFEFEFEFEFEFEFE; // All squares except column 'A'
    public const ulong NotHFile = 0x7F7F7F7F7F7F7F7F; // All squares except column 'H'
    
    public static readonly ulong[] RankMasks =
    {
        0x00000000000000FF, // Rank 0 (1st rank, Black's back rank)
        0x000000000000FF00, // Rank 1
        0x0000000000FF0000, // Rank 2
        0x00000000FF000000, // Rank 3
        0x000000FF00000000, // Rank 4
        0x0000FF0000000000, // Rank 5
        0x00FF000000000000, // Rank 6
        0xFF00000000000000  // Rank 7 (8th rank, White's back rank)
    };

    public const string StartPosFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public const string KiwiPeteFen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -";
    public const string SjeFen = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    public const int StartPosRootPositionId = 0;
    public const int KiwiPeteRootPositionId = 1;
    public const int SjeRootPositionId = 2;



    public const byte None = 0;

    public const byte Pawn = 0;
    public const byte Knight = 1;
    public const byte Bishop = 2;
    public const byte Rook = 3;
    public const byte Queen = 4;
    public const byte King = 5;

    public const byte NormalMove = 0;
    public const byte CaptureMove = 1;
    public const byte Castle = 2;
    public const byte DoublePush = 3;
    public const byte EnPassant = 4;
    public const byte KnightPromotion = 5;
    public const byte BishopPromotion = 6;
    public const byte RookPromotion = 7;
    public const byte QueenPromotion = 8;
    public const byte KnightCapturePromotion = 9;
    public const byte BishopCapturePromotion = 10;
    public const byte RookCapturePromotion = 11;
    public const byte QueenCapturePromotion = 12;

}