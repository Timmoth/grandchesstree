namespace MoveGen.App;

public enum Piece : byte
{
    WhitePawn, WhiteKnight, WhiteBishop, WhiteRook, WhiteQueen, WhiteKing,
    BlackPawn, BlackKnight, BlackBishop, BlackRook, BlackQueen, BlackKing,
    None,
}

[System.Flags]
public enum CastlingRights : byte
{
    None           = 0,
    WhiteKingside  = 1,
    WhiteQueenside = 2,
    BlackKingside  = 4,
    BlackQueenside = 8,
}

public struct UndoInfo
{
    public CastlingRights Castling;
    public int            EpSquare;
    public int            HalfmoveClock;
    public Piece          Captured;
}

public sealed partial class Position
{
    public ulong[] Pieces  = new ulong[12];
    public Piece[] Squares = new Piece[64];

    public ulong WhiteOccupied;
    public ulong BlackOccupied;
    public ulong AllOccupied;

    public bool           WhiteToMove;
    public CastlingRights Castling;
    public int            EpSquare;
    public int            HalfmoveClock;
    public int            FullmoveNumber;

    internal readonly UndoInfo[] _undoStack = new UndoInfo[1024];
    internal int _undoTop;

    static readonly CastlingRights[] CastlingRightsMask = BuildCastlingMask();

    static CastlingRights[] BuildCastlingMask()
    {
        var m = new CastlingRights[64];
        for (int i = 0; i < 64; i++)
            m[i] = (CastlingRights)0xF;

        m[0]  &= ~CastlingRights.WhiteQueenside;
        m[7]  &= ~CastlingRights.WhiteKingside;
        m[4]  &= ~(CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside);
        m[56] &= ~CastlingRights.BlackQueenside;
        m[63] &= ~CastlingRights.BlackKingside;
        m[60] &= ~(CastlingRights.BlackKingside | CastlingRights.BlackQueenside);

        return m;
    }
}
