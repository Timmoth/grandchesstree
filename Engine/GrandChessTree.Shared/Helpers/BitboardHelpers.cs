using System.Numerics;
using System.Runtime.CompilerServices;

namespace GrandChessTree.Shared.Helpers;

public static class BitboardHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopLSB(this ref ulong b)
    {
        var i = BitOperations.TrailingZeroCount(b);
        b &= b - 1;

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftUp(this ulong board)
    {
        return board << 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftDown(this ulong board)
    {
        return board >> 8;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftUpRight(this ulong board)
    {
        // Combined shift up (<< 8) and right (<< 1) with a mask to prevent overflow on the left side.
        return (board << 9) & Constants.NotAFile;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftUpLeft(this ulong board)
    {
        // Combined shift up (<< 8) and left (>> 1) with a mask to prevent overflow on the right side.
        return (board << 7) & Constants.NotHFile;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftDownRight(this ulong board)
    {
        // Combined shift down (>> 8) and right (<< 1) with a mask to prevent overflow on the left side.
        return (board >> 7) & Constants.NotAFile;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ShiftDownLeft(this ulong board)
    {
        // Combined shift down (>> 8) and left (>> 1) with a mask to prevent overflow on the right side.
        return (board >> 9) & Constants.NotHFile;
    }
    
}