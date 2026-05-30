using System.Runtime.CompilerServices;

namespace GrandChessTree.Shared.Helpers;

public static class BoardExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void CloneTo(this ref Board board, ref Board copy)
    {
        fixed (Board* sourcePtr = &board)
        fixed (Board* destPtr = &copy)
        {
            // Copy the memory block from source to destination
            Buffer.MemoryCopy(sourcePtr, destPtr, sizeof(Board), sizeof(Board));
        }
    }
}