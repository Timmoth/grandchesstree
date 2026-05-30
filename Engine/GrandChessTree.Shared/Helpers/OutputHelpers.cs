using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace GrandChessTree.Shared.Helpers;

public static class OutputHelpers
{
    private const byte BlackPawn = 1;
    private const byte BlackKnight = 3;
    private const byte BlackRook = 7;
    private const byte BlackBishop = 5;
    private const byte BlackQueen = 9;
    private const byte BlackKing = 11;

    private const byte WhitePawn = 2;
    private const byte WhiteKnight = 4;
    private const byte WhiteBishop = 6;
    private const byte WhiteRook = 8;
    private const byte WhiteQueen = 10;
    private const byte WhiteKing = 12;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetPiece(this ref Board board, int square)
    {
        if (((board.White | board.Black) & (1UL << square)) == 0) return 0;

        return (byte)BitOperations.TrailingZeroCount(
            ((((board.Black & board.Pawn) >> square) & 1UL) << 1) |
            ((((board.White & board.Pawn) >> square) & 1UL) << 2) |
            ((((board.Black & board.Knight) >> square) & 1UL) << 3) |
            ((((board.White & board.Knight) >> square) & 1UL) << 4) |
            ((((board.Black & board.Bishop) >> square) & 1UL) << 5) |
            ((((board.White & board.Bishop) >> square) & 1UL) << 6) |
            ((((board.Black & board.Rook) >> square) & 1UL) << 7) |
            ((((board.White & board.Rook) >> square) & 1UL) << 8) |
            ((((board.Black & board.Queen) >> square) & 1UL) << 9) |
            ((((board.White & board.Queen) >> square) & 1UL) << 10) |
            ((((1ul << board.BlackKingPos) >> square) & 1UL) << 11) |
            ((((1ul << board.WhiteKingPos) >> square) & 1UL) << 12));
    }

    private static string ConvertPosition(this int position)
    {
        var rank = position.GetRankIndex();
        var file = position.GetFileIndex();
        return $"{(char)('a' + file)}{(char)('1' + rank)}";
    }  
    
    public static string ToFen(this Board board, bool whiteToMove, int hmc, int moves)
    {
        var fen = new StringBuilder();

        for (var row = 7; row >= 0; row--)
        {
            var emptyCount = 0;

            for (var col = 0; col < 8; col++)
            {
                var piece = board.GetPiece(row * 8 + col);
                if (piece == 0)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        fen.Append(emptyCount);
                        emptyCount = 0;
                    }

                    fen.Append(piece.PieceToChar());
                }
            }

            if (emptyCount > 0) fen.Append(emptyCount);

            if (row > 0) fen.Append('/');
        }

        fen.Append(' ');
        fen.Append(whiteToMove ? "w" : "b");
        fen.Append(' ');

        if (board.CastleRights == CastleRights.None)
        {
            fen.Append('-');
        }
        else
        {
            if (board.CastleRights.HasFlag(CastleRights.WhiteKingSide)) fen.Append("K");

            if (board.CastleRights.HasFlag(CastleRights.WhiteQueenSide)) fen.Append('Q');

            if (board.CastleRights.HasFlag(CastleRights.BlackKingSide)) fen.Append('k');

            if (board.CastleRights.HasFlag(CastleRights.BlackQueenSide)) fen.Append('q');
        }
        fen.Append(' ');

        if (board.EnPassantFile >= 8)
            fen.Append("-");
        else
        {
            var enpassantTargetSquare = whiteToMove ? 5 * 8 + board.EnPassantFile : 2 * 8 + board.EnPassantFile;
            fen.Append((enpassantTargetSquare).ConvertPosition());
        }
        fen.Append(' ');
        fen.Append(hmc);
        fen.Append(' ');
        fen.Append(moves);

        return fen.ToString();
    }

    public static string ToFenWithoutIllegalEp(this Board board, bool whiteToMove, int hmc, int moves)
    {
        var fen = new StringBuilder();

        for (var row = 7; row >= 0; row--)
        {
            var emptyCount = 0;

            for (var col = 0; col < 8; col++)
            {
                var piece = board.GetPiece(row * 8 + col);
                if (piece == 0)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        fen.Append(emptyCount);
                        emptyCount = 0;
                    }

                    fen.Append(piece.PieceToChar());
                }
            }

            if (emptyCount > 0) fen.Append(emptyCount);

            if (row > 0) fen.Append('/');
        }

        fen.Append(' ');
        fen.Append(whiteToMove ? "w" : "b");
        fen.Append(' ');

        if (board.CastleRights == CastleRights.None)
        {
            fen.Append('-');
        }
        else
        {
            if (board.CastleRights.HasFlag(CastleRights.WhiteKingSide)) fen.Append("K");

            if (board.CastleRights.HasFlag(CastleRights.WhiteQueenSide)) fen.Append('Q');

            if (board.CastleRights.HasFlag(CastleRights.BlackKingSide)) fen.Append('k');

            if (board.CastleRights.HasFlag(CastleRights.BlackQueenSide)) fen.Append('q');
        }
        fen.Append(' ');

        var canEp = board.EnPassantFile < 8 && ((whiteToMove && board.CanWhitePawnEnpassant()) || (!whiteToMove && board.CanBlackPawnEnpassant()));

        if (!canEp)
            fen.Append("-");
        else
        {
            var enpassantTargetSquare = whiteToMove ? 5 * 8 + board.EnPassantFile : 2 * 8 + board.EnPassantFile;
            fen.Append((enpassantTargetSquare).ConvertPosition());
        }
        fen.Append(' ');
        fen.Append(hmc);
        fen.Append(' ');
        fen.Append(moves);

        return fen.ToString();
    }

    private static char PieceToChar(this byte piece)
    {
        return piece switch
        {
            BlackPawn => 'p',
            BlackRook => 'r',
            BlackKnight => 'n',
            BlackBishop => 'b',
            BlackQueen => 'q',
            BlackKing => 'k',
            WhitePawn => 'P',
            WhiteRook => 'R',
            WhiteKnight => 'N',
            WhiteBishop => 'B',
            WhiteQueen => 'Q',
            WhiteKing => 'K',
            _ => '1'
        };
    }

    public static string FormatBigNumber(this float number)
    {
        if (number >= 1000000000000) return (number / 1000000000000D).ToString("0.#") + "t";  // Trillion

        if (number >= 1000000000) return (number / 1000000000D).ToString("0.#") + "b";  // Billion

        if (number >= 1000000) return (number / 1000000D).ToString("0.#") + "m";  // Million

        if (number >= 1000) return (number / 1000D).ToString("0.#") + "k";  // Thousand

        return number.ToString();
    }

    public static string FormatBigNumber(this ulong number)
    {
        if (number >= 1000000000000) return (number / 1000000000000D).ToString("0.#") + "t";  // Trillion

        if (number >= 1000000000) return (number / 1000000000D).ToString("0.#") + "b";  // Billion

        if (number >= 1000000) return (number / 1000000D).ToString("0.#") + "m";  // Million

        if (number >= 1000) return (number / 1000D).ToString("0.#") + "k";  // Thousand

        return number.ToString();
    }
    public static float RoundToSignificantFigures(this double number, int significantFigures)
    {
        if (number == 0)
        {
            return 0;
        }

        var scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(number))) + 1);
        return (float)(scale * Math.Round(number / scale, significantFigures));
    }

    public static float RoundToSignificantFigures(this float number, int significantFigures)
    {
        if (number == 0)
        {
            return 0;
        }

        var scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(number))) + 1);
        return (float)(scale * Math.Round(number / scale, significantFigures));
    }
}