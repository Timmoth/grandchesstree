using System.Runtime.CompilerServices;
using GrandChessTree.Shared.Helpers;

namespace GrandChessTree.Shared.Moves
{

    public static class MoveExtensions
    {
        public static string ConvertPosition(this int position)
        {
            var rank = position.GetRankIndex();
            var file = position.GetFileIndex();
            return $"{(char)('a' + file)}{(char)('1' + rank)}";
        }

        public static string ToUciMoveName(this uint move)
        {
            var promotion = "";

            var moveType = move.GetMoveType();

            if (moveType == Constants.RookPromotion || moveType == Constants.RookCapturePromotion)
            {
                promotion += "r";
            }
            else if (moveType == Constants.KnightPromotion || moveType == Constants.KnightCapturePromotion)
            {
                promotion += "n";
            }
            else if (moveType == Constants.BishopPromotion || moveType == Constants.BishopCapturePromotion)
            {
                promotion += "b";
            }
            else if (moveType == Constants.QueenPromotion || moveType == Constants.QueenCapturePromotion)
            {
                promotion += "q";
            }

            return
                $"{((int)move.GetFromSquare()).ConvertPosition()}{((int)move.GetToSquare()).ConvertPosition()}{promotion}";
        }

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
            int fromSquare,
            int toSquare)
        {
            return (uint)(Constants.King |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (Constants.Castle << 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeCaptureMove(
            int movedPiece,
            int fromSquare,
            int toSquare)
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
            int fromSquare,
            int toSquare,
            int moveType)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (moveType << 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodePromotionMove(
            int fromSquare,
            int toSquare,
            int moveType)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (moveType << 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeWhiteEnpassantMove(
            int fromSquare,
            int toSquare)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          (toSquare << 10) |
                          (Constants.EnPassant << 16));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeBlackEnpassantMove(
            int fromSquare,
            int toSquare)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          (toSquare << 10) |
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
                          (toSquare << 10)) |
                           (Constants.NormalMove << 16);

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeDoublePush(
    int fromSquare,
    int toSquare)
        {
            return (uint)(Constants.Pawn |
                          (fromSquare << 4) |
                          (toSquare << 10)) |
                           (Constants.DoublePush << 16);

        }
    }
}
