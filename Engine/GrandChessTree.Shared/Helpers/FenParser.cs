using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared.Helpers;

public static class FenParser
{
    public static (Board, bool whiteToMove) Parse(string fen)
    {
        Board board = default;
        var parts = fen.Split(' ');

        var rows = parts[0].Split('/');
        var turn = parts[1];
        var castleRights = parts[2];
        var enPassantTarget = parts[3];

        var index = 0;
        for (var i = rows.Length - 1; i >= 0; i--)
        {
            var row = rows[i];
            foreach (var c in row)
                if (char.IsDigit(c))
                {
                    index += (int)char.GetNumericValue(c);
                }
                else
                {
                    if (char.IsUpper(c))
                    {
                        board.White |= 1UL << index;
                    }
                    else
                    {
                        board.Black |= 1UL << index;
                    }

                    switch (char.ToLower(c))
                    {
                        case 'p':
                            board.Pawn |= 1UL << index;
                            break;
                        case 'n':
                            board.Knight |= 1UL << index;
                            break;
                        case 'b':
                            board.Bishop |= 1UL << index;
                            break;
                        case 'r':
                            board.Rook |= 1UL << index;
                            break;
                        case 'q':
                            board.Queen |= 1UL << index;
                            break;
                        case 'k':
                            if (char.IsUpper(c))
                            {
                                board.WhiteKingPos = (byte)index;
                            }
                            else
                            {
                                board.BlackKingPos = (byte)index;
                            }
                            break;
                        default:
                            throw new Exception($"invalid piece '{c}'");
                    }

                    index++;
                }
        }

        board.CastleRights = ParseCastleRights(castleRights);

        if (enPassantTarget == "-")
            board.EnPassantFile = 8;
        else
            board.EnPassantFile = (byte)"abcdefgh".IndexOf(enPassantTarget[0]);

        board.Hash = Zobrist.CalculateZobristKey(ref board, turn == "w");
        return (board, turn == "w");
    }


    private static CastleRights ParseCastleRights(string input)
    {
        var castleRights = CastleRights.None;

        foreach (var right in input)
            if (right == 'K')
                castleRights |= CastleRights.WhiteKingSide;
            else if (right == 'Q')
                castleRights |= CastleRights.WhiteQueenSide;
            else if (right == 'k')
                castleRights |= CastleRights.BlackKingSide;
            else if (right == 'q') castleRights |= CastleRights.BlackQueenSide;

        return castleRights;
    }
}