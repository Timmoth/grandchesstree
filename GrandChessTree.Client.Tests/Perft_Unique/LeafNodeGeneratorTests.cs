using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Tests.Perft_Unique
{
    public class LeafNodeGeneratorTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 20)]
        [InlineData(2, 400)]
        [InlineData(3, 5362)]
        [InlineData(4, 72078)]
        public unsafe void Startpos_PerftUnique(byte depth, int expectedCount)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.StartPosFen);

            // When
            var leafNodes = LeafNodeGenerator.GenerateLeafNodes(ref board, depth, whiteToMove);

            // Then
            var actualCount = leafNodes.Count;
            Assert.Equal(expectedCount, actualCount);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 48)]
        [InlineData(2, 2038)]
        [InlineData(3, 57548)]
        public unsafe void Kiwipete_PerftUnique(byte depth, int expectedCount)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.KiwiPeteFen);

            // When
            var leafNodes = LeafNodeGenerator.GenerateLeafNodes(ref board, depth, whiteToMove);

            // Then
            var actualCount = leafNodes.Count;
            Assert.Equal(expectedCount, actualCount);
        }


        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 46)]
        [InlineData(2, 2079)]
        [InlineData(3, 49377)]
        public unsafe void Sje_PerftUnique(byte depth, int expectedCount)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.SjeFen);

            // When
            var leafNodes = LeafNodeGenerator.GenerateLeafNodes(ref board, depth, whiteToMove);

            // Then
            var actualCount = leafNodes.Count;
            Assert.Equal(expectedCount, actualCount);
        }
    }
}