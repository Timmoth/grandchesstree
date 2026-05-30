using System.Numerics;
using MoveGen.App;
using Xunit;

namespace MoveGen.Tests;

public class Part6Tests
{
    static int Sq(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    // For each (square, occupancy), the magic and classical functions must agree.
    // We sample a handful of representative occupancies per square.

    static IEnumerable<ulong> SampleOccupancies(int sq)
    {
        yield return 0UL;
        yield return ulong.MaxValue & ~(1UL << sq);

        var rng = new Random(sq * 73 + 17);
        for (int i = 0; i < 10; i++)
            yield return (ulong)rng.NextInt64();
    }

    [Fact]
    public void Magic_rook_matches_classical_for_all_squares_and_sample_occupancies()
    {
        for (int sq = 0; sq < 64; sq++)
            foreach (ulong occ in SampleOccupancies(sq))
                Assert.Equal(
                    Attacks.RookAttacks(sq, occ),
                    Magic.RookAttacks (sq, occ));
    }

    [Fact]
    public void Magic_bishop_matches_classical_for_all_squares_and_sample_occupancies()
    {
        for (int sq = 0; sq < 64; sq++)
            foreach (ulong occ in SampleOccupancies(sq))
                Assert.Equal(
                    Attacks.BishopAttacks(sq, occ),
                    Magic.BishopAttacks (sq, occ));
    }

    [Fact]
    public void Magic_queen_equals_rook_or_bishop()
    {
        var rng = new Random(42);
        for (int sq = 0; sq < 64; sq++)
        {
            ulong occ = (ulong)rng.NextInt64();
            Assert.Equal(
                Magic.RookAttacks(sq, occ) | Magic.BishopAttacks(sq, occ),
                Magic.QueenAttacks(sq, occ));
        }
    }

    [Fact]
    public void Magic_rook_on_d4_empty_board_attacks_14_squares()
    {
        Assert.Equal(14, BitOperations.PopCount(Magic.RookAttacks(Sq("d4"), 0UL)));
    }

    [Fact]
    public void Magic_bishop_on_d4_empty_board_attacks_13_squares()
    {
        Assert.Equal(13, BitOperations.PopCount(Magic.BishopAttacks(Sq("d4"), 0UL)));
    }
}
