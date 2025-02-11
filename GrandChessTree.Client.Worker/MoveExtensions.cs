using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Client.Worker
{
    public static class MoveExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetMovedPiece(this uint move)
        {
            return (byte)(move & 0x0F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetFromSquare(this uint move)
        {
            return (byte)((move >> 4) & 0x3F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetToSquare(this uint move)
        {
            return (byte)((move >> 10) & 0x3F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetMoveType(this uint move)
        {
            return (byte)((move >> 16) & 0x0F);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeCastleMove(
            byte fromSquare,
            byte toSquare)
        {
            return (uint)(Constants.King |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (Constants.Castle << 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeCaptureMove(
            byte movedPiece,
            byte fromSquare,
            byte toSquare)
        {
            return (uint)(movedPiece |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (Constants.CaptureMove << 16));
        }

        // 4 bits - piece
        // 6 bits - from square
        // 6 bits - to square
        // 4 bits - type

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeCapturePromotionMove(
            byte movedPiece,
            byte fromSquare,
            byte toSquare,
            byte moveType)
        {
            return (uint)(movedPiece |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (moveType << 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodePromotionMove(
            byte fromSquare,
            byte toSquare,
            byte moveType)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (moveType << 16));
        }

        private const int whiteEnpassantOffset = 5 * 8;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeWhiteEnpassantMove(
            byte fromSquare,
            byte enpassantFile)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          ((whiteEnpassantOffset + enpassantFile) << 10) |
                          (Constants.EnPassant << 16));
        }

        private const int blackEnpassantOffset = 2 * 8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeBlackEnpassantMove(
            byte fromSquare,
            byte enpassantFile)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          ((blackEnpassantOffset + enpassantFile) << 10) |
                          (Constants.EnPassant << 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeNormalMove(
            int movedPiece,
            int fromSquare,
            int toSquare)
        {
            return (uint)(movedPiece |
                          (fromSquare << 4) |
                          (toSquare << 10))|
                           (Constants.NormalMove << 16);

        }
    }
}
