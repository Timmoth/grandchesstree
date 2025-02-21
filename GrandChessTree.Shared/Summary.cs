
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GrandChessTree.Shared;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Summary
{
    public ulong Nodes;
    public ulong Captures;
    public ulong Enpassant;
    public ulong Castles;
    public ulong Promotions;
    public ulong DirectCheck;
    public ulong SingleDiscoveredCheck;
    public ulong DirectDiscoveredCheck;
    public ulong DoubleDiscoveredCheck;
    public ulong DirectCheckmate;
    public ulong SingleDiscoveredCheckmate;
    public ulong DirectDiscoverdCheckmate;
    public ulong DoubleDiscoverdCheckmate;
    public ulong FullHash;
    public byte Depth;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Accumulate(ref Summary summary)
    {
        Nodes += summary.Nodes;
        Captures += summary.Captures;
        Enpassant += summary.Enpassant;
        Castles += summary.Castles;
        Promotions += summary.Promotions;
        DirectCheck += summary.DirectCheck;
        SingleDiscoveredCheck += summary.SingleDiscoveredCheck;
        DirectDiscoveredCheck += summary.DirectDiscoveredCheck;
        DoubleDiscoveredCheck += summary.DoubleDiscoveredCheck;
        DirectCheckmate += summary.DirectCheckmate;
        SingleDiscoveredCheckmate += summary.SingleDiscoveredCheckmate;
        DirectDiscoverdCheckmate += summary.DirectDiscoverdCheckmate;
        DoubleDiscoverdCheckmate += summary.DoubleDiscoverdCheckmate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Accumulate(ref Summary summary, ulong occurrences)
    {
        Nodes += summary.Nodes * occurrences;
        Captures += summary.Captures * occurrences;
        Enpassant += summary.Enpassant * occurrences;
        Castles += summary.Castles * occurrences;
        Promotions += summary.Promotions * occurrences;
        DirectCheck += summary.DirectCheck * occurrences;
        SingleDiscoveredCheck += summary.SingleDiscoveredCheck * occurrences;
        DirectDiscoveredCheck += summary.DirectDiscoveredCheck * occurrences;
        DoubleDiscoveredCheck += summary.DoubleDiscoveredCheck * occurrences;
        DirectCheckmate += summary.DirectCheckmate * occurrences;
        SingleDiscoveredCheckmate += summary.SingleDiscoveredCheckmate * occurrences;
        DirectDiscoverdCheckmate += summary.DirectDiscoverdCheckmate * occurrences;
        DoubleDiscoverdCheckmate += summary.DoubleDiscoverdCheckmate * occurrences;
    }

    public void Print()
    {
        Console.WriteLine($"nodes:{Nodes}");
        Console.WriteLine($"captures:{Captures}");
        Console.WriteLine($"enpassants:{Enpassant}");
        Console.WriteLine($"castles:{Castles}");
        Console.WriteLine($"promotions:{Promotions}");

        Console.WriteLine($"direct_checks:{DirectCheck}");
        Console.WriteLine($"single_discovered_checks:{SingleDiscoveredCheck}");
        Console.WriteLine($"direct_discovered_checks:{DirectDiscoveredCheck}");
        Console.WriteLine($"double_discovered_check:{DoubleDiscoveredCheck}");
        Console.WriteLine($"total_checks:{DirectCheck + SingleDiscoveredCheck + DirectDiscoveredCheck + DoubleDiscoveredCheck}");

        Console.WriteLine($"direct_mates:{DirectCheckmate}");
        Console.WriteLine($"single_discovered_mates:{SingleDiscoveredCheckmate}");
        Console.WriteLine($"direct_discoverd_mates:{DirectDiscoverdCheckmate}");
        Console.WriteLine($"double_discoverd_mates:{DoubleDiscoverdCheckmate}");
        Console.WriteLine($"total_mates:{DirectCheckmate + SingleDiscoveredCheckmate + DirectDiscoverdCheckmate + DoubleDiscoverdCheckmate}");

    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    internal void AddCapture()
    {
        Captures++;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    internal void AddCastle()
    {
        Castles++;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    internal void AddEnpassant()
    {
        Enpassant++;
        Captures++;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    internal void AddPromotion()
    {
        Promotions+=4;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    internal void AddPromotionCapture()
    {
        Promotions+=4;
        Captures+=4;
    }
}