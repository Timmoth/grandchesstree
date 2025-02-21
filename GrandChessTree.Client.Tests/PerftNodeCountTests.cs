using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared;

namespace GrandChessTree.Client.Tests
{
    public class PerftNodeCountTests
    {
        // Epds from:
        // https://github.com/ChrisWhittington/Chess-EPDs
        // https://github.com/AndyGrant/Ethereal

        // setting to 5 or above will take > 5 mins
        const int maxPerftDepth = 2;

        public static IEnumerable<object[]> GetChrisWhittingtonPerftDotEpdTestCases()
        {
            var filePath = "perft.epd";
            if (!File.Exists(filePath))
                yield break;

            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(';').Select(p => p.Trim()).ToArray();
                if (parts.Length < 2) continue;

                string fen = parts[0];

                foreach (var depthInfo in parts.Skip(1))
                {
                    var depthParts = depthInfo.Split(' ');
                    if (depthParts.Length != 2 || !depthParts[0].StartsWith("D")) continue;

                    if (int.TryParse(depthParts[0].Substring(1), out int depth) &&
                        ulong.TryParse(depthParts[1], out ulong expected))
                    {
                        if (depth >= maxPerftDepth)
                        {
                            continue;
                        }
                        yield return new object[] { fen, depth, expected };
                    }
                }
            }
        }

        public static IEnumerable<object[]> GetChrisWhittingtonPerftMarcelDotEpdTestCases()
        {
            var filePath = "perft-marcel.epd";
            if (!File.Exists(filePath))
                yield break;

            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(';').Select(p => p.Trim()).ToArray();
                if (parts.Length < 2) continue;

                string fen = parts[0];

                foreach (var depthInfo in parts.Skip(1))
                {
                    var depthParts = depthInfo.Split(' ');
                    if (depthParts.Length != 2 || !depthParts[0].StartsWith("D")) continue;

                    if (int.TryParse(depthParts[0].Substring(1), out int depth) &&
                        ulong.TryParse(depthParts[1], out ulong expected))
                    {
                        if (depth >= maxPerftDepth)
                        {
                            continue;
                        }
                        yield return new object[] { fen, depth, expected };
                    }
                }
            }
        }

        public static IEnumerable<object[]> GetAndyGrantPerftEtherealDotEpdTestCases()
        {
            var filePath = "perft-ethereal.epd";
            if (!File.Exists(filePath))
                yield break;

            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(';').Select(p => p.Trim()).ToArray();
                if (parts.Length < 2) continue;

                string fen = parts[0];

                foreach (var depthInfo in parts.Skip(1))
                {
                    var depthParts = depthInfo.Split(' ');
                    if (depthParts.Length != 2 || !depthParts[0].StartsWith("D")) continue;

                    if (int.TryParse(depthParts[0].Substring(1), out int depth) &&
                        ulong.TryParse(depthParts[1], out ulong expected))
                    {
                        if (depth >= maxPerftDepth)
                        {
                            continue;
                        }

                        yield return new object[] { fen, depth, expected };
                    }
                }
            }
        }


        [Theory]
        [MemberData(nameof(GetChrisWhittingtonPerftDotEpdTestCases))]
        [MemberData(nameof(GetChrisWhittingtonPerftMarcelDotEpdTestCases))]
        [MemberData(nameof(GetAndyGrantPerftEtherealDotEpdTestCases))]
        public void PerftBulkReturnsCorrectNodeCount(string fen, int depth, ulong expected)
        {
            // Given
            PerftBulk.AllocateHashTable(128);
            var (board, whiteToMove) = FenParser.Parse(fen);

            // When

            var actual = PerftBulk.PerftRootBulk(ref board, depth, whiteToMove);

            // Then
            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(GetChrisWhittingtonPerftDotEpdTestCases))]
        [MemberData(nameof(GetChrisWhittingtonPerftMarcelDotEpdTestCases))]
        [MemberData(nameof(GetAndyGrantPerftEtherealDotEpdTestCases))]
        public void PerftStatsReturnsCorrectNodeCount(string fen, int depth, ulong expected)
        {
            // Given
            Perft.AllocateHashTable(128);
            var (board, whiteToMove) = FenParser.Parse(fen);
            Summary summary = default;

            // When
            Perft.PerftRoot(ref board, ref summary, depth, whiteToMove);

            // Then
            Assert.Equal(expected, summary.Nodes);
        }

    }

}
