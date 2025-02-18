using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Tests.PerftStats
{
    public class PerftStatsTests
    {
        [Theory]
        [InlineData(0, 1, 0, 0, 0, 0, 0, 0, 0, 0)]
        [InlineData(1, 20, 0, 0, 0, 0, 0, 0, 0, 0)]
        [InlineData(2, 400, 0, 0, 0, 0, 0, 0, 0, 0)]
        [InlineData(3, 8_902, 34, 0, 0, 0, 12, 0, 0, 0)]
        [InlineData(4, 197_281, 1576, 0, 0, 0, 469, 0, 0, 8)]
        [InlineData(5, 4_865_609, 82_719, 258, 0, 0, 27_351, 6, 0, 347)]
        public unsafe void InitialPosition_Perft_Returns_Expected_Summary(byte depth, ulong nodes, ulong captures, ulong enpassant, ulong castles, ulong promotions, ulong checks, ulong discoveryChecks, ulong doubleChecks, ulong checkMates)
        {
            // Given
            Perft.AllocateHashTable(128);
            Summary summary = default;
            var (board, whiteToMove) = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(nodes, summary.Nodes);
            Assert.Equal(captures, summary.Captures);
            Assert.Equal(enpassant, summary.Enpassant);
            Assert.Equal(castles, summary.Castles);
            Assert.Equal(promotions, summary.Promotions);
            Assert.Equal(checks, summary.DirectCheck +
                                 summary.SingleDiscoveredCheck +
                                 summary.DirectDiscoveredCheck +
                                 summary.DoubleDiscoveredCheck +
                                 summary.DirectCheckmate +
                                 summary.SingleDiscoveredCheckmate +
                                 summary.DirectDiscoverdCheckmate +
                                 summary.DoubleDiscoverdCheckmate);
            Assert.Equal(discoveryChecks, summary.SingleDiscoveredCheck);
            Assert.Equal(doubleChecks, summary.DirectDiscoveredCheck + summary.DoubleDiscoveredCheck);
            Assert.Equal(checkMates, summary.DirectCheckmate +
                                     summary.SingleDiscoveredCheckmate +
                                     summary.DirectDiscoverdCheckmate +
                                     summary.DoubleDiscoverdCheckmate);
        }

        [Theory]
        [InlineData(1, 48, 8, 0, 2, 0, 0, 0, 0, 0)]
        [InlineData(2, 2039, 351, 1, 91, 0, 3, 0, 0, 0)]
        [InlineData(3, 97862, 17102, 45, 3162, 0, 993, 0, 0, 1)]
        [InlineData(4, 4_085_603, 757163, 1929, 128013, 15172, 25523, 42, 6, 43)]
        [InlineData(5, 193_690_690, 35043416, 73365, 4993637, 8392, 3309887, 19883, 2637, 30171)]
        public unsafe void Position2_Perft_Returns_Expected_Summary(byte depth, ulong nodes, ulong captures, ulong enpassant, ulong castles, ulong promotions, ulong checks, ulong discoveryChecks, ulong doubleChecks, ulong checkMates)
        {
            // Given
            Perft.AllocateHashTable(128);
            Summary summary = default;
            var (board, whiteToMove) = FenParser.Parse("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -");

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(nodes, summary.Nodes);
            Assert.Equal(captures, summary.Captures);
            Assert.Equal(enpassant, summary.Enpassant);
            Assert.Equal(castles, summary.Castles);
            Assert.Equal(promotions, summary.Promotions);
            Assert.Equal(checks, summary.DirectCheck +
                                 summary.SingleDiscoveredCheck +
                                 summary.DirectDiscoveredCheck +
                                 summary.DoubleDiscoveredCheck +
                                 summary.DirectCheckmate +
                                 summary.SingleDiscoveredCheckmate +
                                 summary.DirectDiscoverdCheckmate +
                                 summary.DoubleDiscoverdCheckmate);
            Assert.Equal(discoveryChecks, summary.SingleDiscoveredCheck);
            Assert.Equal(doubleChecks, summary.DirectDiscoveredCheck + summary.DoubleDiscoveredCheck);
            Assert.Equal(checkMates, summary.DirectCheckmate +
                                     summary.SingleDiscoveredCheckmate +
                                     summary.DirectDiscoverdCheckmate +
                                     summary.DoubleDiscoverdCheckmate);
        }

        [Theory]
        [InlineData(1, 14, 1, 0, 0, 0, 2, 0, 0, 0)]
        [InlineData(2, 191, 14, 0, 0, 0, 10, 0, 0, 0)]
        [InlineData(3, 2812, 209, 2, 0, 0, 267, 3, 0, 0)]
        [InlineData(4, 43238, 3348, 123, 0, 0, 1680, 106, 0, 17)]
        [InlineData(5, 674624, 52051, 1165, 0, 0, 52950, 1292, 3, 0)]
        [InlineData(6, 11_030_083, 940350, 33325, 0, 7552, 452473, 26067, 0, 2733)]
        [InlineData(7, 178633661, 14519036, 294874, 0, 140024, 12797406, 370630, 3612, 87)]
        public unsafe void Position3_Perft_Returns_Expected_Summary(byte depth, ulong nodes, ulong captures, ulong enpassant, ulong castles, ulong promotions, ulong checks, ulong discoveryChecks, ulong doubleChecks, ulong checkMates)
        {
            // Given
            Perft.AllocateHashTable(128);
            Summary summary = default;
            var (board, whiteToMove) = FenParser.Parse("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -");

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(nodes, summary.Nodes);
            Assert.Equal(captures, summary.Captures);
            Assert.Equal(enpassant, summary.Enpassant);
            Assert.Equal(castles, summary.Castles);
            Assert.Equal(promotions, summary.Promotions);
            Assert.Equal(checks, summary.DirectCheck +
                                 summary.SingleDiscoveredCheck +
                                 summary.DirectDiscoveredCheck +
                                 summary.DoubleDiscoveredCheck +
                                 summary.DirectCheckmate +
                                 summary.SingleDiscoveredCheckmate +
                                 summary.DirectDiscoverdCheckmate +
                                 summary.DoubleDiscoverdCheckmate);
            Assert.Equal(discoveryChecks, summary.SingleDiscoveredCheck);
            Assert.Equal(doubleChecks, summary.DirectDiscoveredCheck + summary.DoubleDiscoveredCheck);
            Assert.Equal(checkMates, summary.DirectCheckmate +
                                     summary.SingleDiscoveredCheckmate +
                                     summary.DirectDiscoverdCheckmate +
                                     summary.DoubleDiscoverdCheckmate);
        }

        [Theory]
        [InlineData(1, 6, 0, 0, 0, 0, 0, 0, 0, 0)]
        [InlineData(2, 264, 87, 0, 6, 48, 10, 0, 0, 0)]
        [InlineData(3, 9467, 1021, 4, 0, 120, 38, 2, 0, 22)]
        [InlineData(4, 422333, 131393, 0, 7795, 60032, 15492, 19, 0, 5)]
        [InlineData(5, 15_833_292, 2046173, 6512, 0, 329464, 200568, 11621, 50, 50562)]
        public unsafe void Position4_Perft_Returns_Expected_Summary(byte depth, ulong nodes, ulong captures, ulong enpassant, ulong castles, ulong promotions, ulong checks, ulong discoveryChecks, ulong doubleChecks, ulong checkMates)
        {
            // Given
            Perft.AllocateHashTable(128);
            Summary summary = default;
            var (board, whiteToMove) = FenParser.Parse("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1");

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(nodes, summary.Nodes);
            Assert.Equal(captures, summary.Captures);
            Assert.Equal(enpassant, summary.Enpassant);
            Assert.Equal(castles, summary.Castles);
            Assert.Equal(promotions, summary.Promotions);
            Assert.Equal(checks, summary.DirectCheck +
                                 summary.SingleDiscoveredCheck +
                                 summary.DirectDiscoveredCheck +
                                 summary.DoubleDiscoveredCheck +
                                 summary.DirectCheckmate +
                                 summary.SingleDiscoveredCheckmate +
                                 summary.DirectDiscoverdCheckmate +
                                 summary.DoubleDiscoverdCheckmate);
            Assert.Equal(discoveryChecks, summary.SingleDiscoveredCheck);
            Assert.Equal(doubleChecks, summary.DirectDiscoveredCheck + summary.DoubleDiscoveredCheck);
            Assert.Equal(checkMates, summary.DirectCheckmate +
                                     summary.SingleDiscoveredCheckmate +
                                     summary.DirectDiscoverdCheckmate +
                                     summary.DoubleDiscoverdCheckmate);
        }

        [Theory]
        [InlineData(1, 3, 0, 0, 0, 0)]
        [InlineData(2, 118, 6, 0, 0, 0)]
        [InlineData(3, 3022, 87, 0, 0, 0)]
        [InlineData(4, 113355, 7658, 0, 0, 0)]
        [InlineData(5, 3007414, 131775, 32, 0, 0)]
        [InlineData(6, 109477393, 8886368, 537, 28312, 0)]
        public unsafe void Position5_Perft_Returns_Expected_Summary(byte depth, ulong nodes, ulong captures, ulong enpassant, ulong castles, ulong promotions)
        {
            // Given
            Perft.AllocateHashTable(128);
            Summary summary = default;
            var (board, whiteToMove) = FenParser.Parse("rnbq1b1r/ppppkppp/4pn2/8/1Q6/2PP4/PP2PPPP/RNB1KBNR b KQ - 2 4");

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(nodes, summary.Nodes);
            Assert.Equal(captures, summary.Captures);
            Assert.Equal(enpassant, summary.Enpassant);
            Assert.Equal(castles, summary.Castles);
            Assert.Equal(promotions, summary.Promotions);
        }

        [Theory]
        [InlineData(5, 164_075_551, "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10")]
        [InlineData(5, 89_941_194, "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8")]
        public unsafe void Perft_Returns_Expected_Nodes(byte depth, ulong nodes, string fen)
        {
            // Given
            Perft.AllocateHashTable(128);
            Summary summary = default;
            var (board, whiteToMove) = FenParser.Parse(fen);

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(nodes, summary.Nodes);
        }

    }
}