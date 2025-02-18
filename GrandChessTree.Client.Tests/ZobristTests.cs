using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Moves;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Client.Tests
{
    public class ZobristTests
    {
        // http://hgm.nubati.net/book_format.html
        [Theory]
        [InlineData("e2e4", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "823c9b50fd114196", 0, 1)]
        [InlineData("e2e4 d7d5", "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2", "756b94461c50fb0", 0, 2)]
        [InlineData("e2e4 d7d5 e4e5", "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2", "662fafb965db29d4", 0, 2)]
        [InlineData("e2e4 d7d5 e4e5 f7f5", "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3", "22a48b5a8e47ff78", 0, 3)]
        [InlineData("e2e4 d7d5 e4e5 f7f5 e1e2", "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPPKPPP/RNBQ1BNR b kq - 0 3", "652a607ca3f242c1", 0, 3)]
        [InlineData("e2e4 d7d5 e4e5 f7f5 e1e2 e8f7", "rnbq1bnr/ppp1pkpp/8/3pPp2/8/8/PPPPKPPP/RNBQ1BNR w - - 0 4", "fdd303c946bdd9", 0, 4)]
        [InlineData("a2a4 b7b5 h2h4 b5b4 c2c4", "rnbqkbnr/p1pppppp/8/8/PpP4P/8/1P1PPPP1/RNBQKBNR b KQkq c3 0 3", "3c8123ea7b067637", 0, 3)]
        [InlineData("a2a4 b7b5 h2h4 b5b4 c2c4 b4c3 a1a3", "rnbqkbnr/p1pppppp/8/8/P6P/R1p5/1P1PPPP1/1NBQKBNR b Kkq - 0 4", "5c3f9b829b279560", 0, 4)]
        public unsafe void MoveSequenceLeadsToCorrectFenAndHash(string uciMoves, string expectedFen, string expectedHash, int hmc, int ply)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.StartPosFen);

            // When
            var inputMoves = uciMoves.Split(' ');

            var nextMoveIsWhite = whiteToMove;
            Span<uint> moves = stackalloc uint[218];

            for (int i = 0; i < inputMoves.Length; i++)
            {
                var uciMove = inputMoves[i];
                var moveCount = MoveGenerator.GenerateMoves(ref moves, ref board, nextMoveIsWhite);
                uint move = 0;
                for (int j = 0; j < moveCount; j++)
                {
                    if (moves[j].ToUciMoveName() == uciMove)
                    {
                        move = moves[j];
                        break;
                    }
                }

                if (move == 0)
                {
                    var availableMoves = new List<string>();
                    Console.WriteLine($"Couldn't find {uciMove} in available moves:");

                    for (int j = 0; j < moveCount; j++)
                    {
                        var m = $"{moves[j].GetMovedPiece()} - {moves[j].ToUciMoveName()}";
                        availableMoves.Add(m);
                        Console.WriteLine(m);
                    }

                }

                if (nextMoveIsWhite)
                {
                    board.ApplyWhiteMove(move);
                }
                else
                {
                    board.ApplyBlackMove(move);
                }

                nextMoveIsWhite = !nextMoveIsWhite;
            }


            if (board.EnPassantFile < 8 && ((nextMoveIsWhite && !board.CanWhitePawnEnpassant()) || (!nextMoveIsWhite && !board.CanBlackPawnEnpassant())))
            {
                // Is ep move possible? If not remove possibility from hash
                board.Hash ^= Zobrist.EnPassantFile[board.EnPassantFile];
            }

            var actualFen = board.ToFen(nextMoveIsWhite, hmc, ply);
            // Then
            Assert.Equal(expectedFen, actualFen);
            Assert.Equal(expectedHash, board.Hash.ToString("X").ToLower());
        }

        [Theory]
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "463b96181691fc9c")]
        [InlineData("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "823c9b50fd114196")]
        [InlineData("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2", "756b94461c50fb0")]
        [InlineData("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2", "662fafb965db29d4")]
        [InlineData("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3", "22a48b5a8e47ff78")]
        [InlineData("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPPKPPP/RNBQ1BNR b kq - 0 3", "652a607ca3f242c1")]
        [InlineData("rnbq1bnr/ppp1pkpp/8/3pPp2/8/8/PPPPKPPP/RNBQ1BNR w - - 0 4", "fdd303c946bdd9")]
        [InlineData("rnbqkbnr/p1pppppp/8/8/PpP4P/8/1P1PPPP1/RNBQKBNR b KQkq c3 0 3", "3c8123ea7b067637")]
        [InlineData("rnbqkbnr/p1pppppp/8/8/P6P/R1p5/1P1PPPP1/1NBQKBNR b Kkq - 0 4", "5c3f9b829b279560")]
        public void CorrectZobristHash(string fen, string expected)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(fen);

            // When
            var key = Zobrist.CalculateZobristKeyWithoutInvalidEp(ref board, whiteToMove);

            // Then
            Assert.Equal(expected, key.ToString("X").ToLower());
        }
    }
}