using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Tests.Perft_Unique
{
    public class PerftUniqueTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 20)]
        [InlineData(2, 400)]
        [InlineData(3, 5362)]
        [InlineData(4, 72078)]
        [InlineData(5, 822518)]
        [InlineData(6, 9417681)]
        public unsafe void Startpos_PerftUnique(byte depth, int expectedCount)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.StartPosFen);

            // When
            PerftUnique.UniquePositions.Clear();
            PerftUnique.AllocateHashTable(128);
            PerftUnique.PerftRootUnique(ref board, depth, whiteToMove);
            PerftUnique.FreeHashTable();

            // Then
            var actualCount = PerftUnique.UniquePositions.Count;
            Assert.Equal(expectedCount, actualCount);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 48)]
        [InlineData(2, 2038)]
        [InlineData(3, 57548)]
        [InlineData(4, 1374152)]
        public unsafe void Kiwipete_PerftUnique(byte depth, int expectedCount)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.KiwiPeteFen);

            // When
            PerftUnique.UniquePositions.Clear();
            PerftUnique.AllocateHashTable(128);
            PerftUnique.PerftRootUnique(ref board, depth, whiteToMove);
            PerftUnique.FreeHashTable();

            // Then
            var actualCount = PerftUnique.UniquePositions.Count;
            Assert.Equal(expectedCount, actualCount);
        }


        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 46)]
        [InlineData(2, 2079)]
        [InlineData(3, 49377)]
        [InlineData(4, 1164752)]
        public unsafe void Sje_PerftUnique(byte depth, int expectedCount)
        {
            // Given
            var (board, whiteToMove) = FenParser.Parse(Constants.SjeFen);

            // When
            PerftUnique.UniquePositions.Clear();
            PerftUnique.AllocateHashTable(128);
            PerftUnique.PerftRootUnique(ref board, depth, whiteToMove);
            PerftUnique.FreeHashTable();

            // Then
            var actualCount = PerftUnique.UniquePositions.Count;
            Assert.Equal(expectedCount, actualCount);
        }
    }
}