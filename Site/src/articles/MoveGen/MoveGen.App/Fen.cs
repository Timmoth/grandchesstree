using System.Text;

namespace MoveGen.App;

public static class Fen
{
    public const string Initial  = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -";

    public static Position Parse(string fen)
    {
        var pos = new Position();
        Array.Fill(pos.Squares, Piece.None);

        string[] f = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 4) throw new FormatException("FEN needs at least 4 fields");

        string[] ranks = f[0].Split('/');
        if (ranks.Length != 8) throw new FormatException("FEN needs 8 ranks");

        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i;
            int file = 0;
            foreach (char c in ranks[i])
            {
                if (char.IsDigit(c)) { file += c - '0'; continue; }
                int sq = rank * 8 + file++;
                Piece p = PieceFromChar(c);
                pos.Squares[sq] = p;
                pos.Pieces[(int)p] |= 1UL << sq;
            }
        }

        pos.WhiteToMove = f[1] == "w";

        pos.Castling = CastlingRights.None;
        if (f[2] != "-")
            foreach (char c in f[2])
                pos.Castling |= c switch
                {
                    'K' => CastlingRights.WhiteKingside,
                    'Q' => CastlingRights.WhiteQueenside,
                    'k' => CastlingRights.BlackKingside,
                    'q' => CastlingRights.BlackQueenside,
                    _   => CastlingRights.None,
                };

        pos.EpSquare = f[3] == "-" ? -1 : SquareFromName(f[3]);

        pos.HalfmoveClock  = f.Length > 4 ? int.Parse(f[4]) : 0;
        pos.FullmoveNumber = f.Length > 5 ? int.Parse(f[5]) : 1;

        for (int p = (int)Piece.WhitePawn; p <= (int)Piece.WhiteKing; p++)
            pos.WhiteOccupied |= pos.Pieces[p];
        for (int p = (int)Piece.BlackPawn; p <= (int)Piece.BlackKing; p++)
            pos.BlackOccupied |= pos.Pieces[p];
        pos.AllOccupied = pos.WhiteOccupied | pos.BlackOccupied;

        return pos;
    }

    public static string Write(Position pos)
    {
        var sb = new StringBuilder();

        for (int rank = 7; rank >= 0; rank--)
        {
            int empties = 0;
            for (int file = 0; file < 8; file++)
            {
                Piece p = pos.Squares[rank * 8 + file];
                if (p == Piece.None) { empties++; continue; }
                if (empties > 0) { sb.Append(empties); empties = 0; }
                sb.Append(CharOf(p));
            }
            if (empties > 0) sb.Append(empties);
            if (rank > 0) sb.Append('/');
        }

        sb.Append(' ').Append(pos.WhiteToMove ? 'w' : 'b');
        sb.Append(' ').Append(CastlingString(pos.Castling));
        sb.Append(' ').Append(pos.EpSquare < 0 ? "-" : SquareName(pos.EpSquare));
        sb.Append(' ').Append(pos.HalfmoveClock);
        sb.Append(' ').Append(pos.FullmoveNumber);

        return sb.ToString();
    }

    static Piece PieceFromChar(char c) => c switch
    {
        'P' => Piece.WhitePawn,  'N' => Piece.WhiteKnight, 'B' => Piece.WhiteBishop,
        'R' => Piece.WhiteRook,  'Q' => Piece.WhiteQueen,  'K' => Piece.WhiteKing,
        'p' => Piece.BlackPawn,  'n' => Piece.BlackKnight, 'b' => Piece.BlackBishop,
        'r' => Piece.BlackRook,  'q' => Piece.BlackQueen,  'k' => Piece.BlackKing,
        _   => throw new FormatException($"Unexpected piece char '{c}'"),
    };

    static int SquareFromName(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    public static char CharOf(Piece p) => "PNBRQKpnbrqk."[(int)p];

    public static string SquareName(int sq) =>
        $"{(char)('a' + (sq & 7))}{(char)('1' + (sq >> 3))}";

    public static string CastlingString(CastlingRights c)
    {
        if (c == CastlingRights.None) return "-";
        var s = new StringBuilder(4);
        if ((c & CastlingRights.WhiteKingside)  != 0) s.Append('K');
        if ((c & CastlingRights.WhiteQueenside) != 0) s.Append('Q');
        if ((c & CastlingRights.BlackKingside)  != 0) s.Append('k');
        if ((c & CastlingRights.BlackQueenside) != 0) s.Append('q');
        return s.ToString();
    }
}
