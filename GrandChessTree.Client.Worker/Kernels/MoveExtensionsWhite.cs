using System.ComponentModel;
using GrandChessTree.Shared;
using GrandChessTree.Shared.Helpers;
using ILGPU.IR.Values;

namespace GrandChessTree.Client.Worker.Kernels
{
    public static class MoveExtensionsWhite
    {
        const ulong BlackKingSideCastleBitboardMaskA = 1UL << 60 ^ 1UL << 62;
        const ulong BlackKingSideCastleBitboardMaskB = 1UL << 63 ^ 1UL << 61;
        const ulong BlackQueenSideCastleBitboardMaskA = 1UL << 60 ^ 1UL << 58;
        const ulong BlackQueenSideCastleBitboardMaskB = 1UL << 56 ^ 1UL << 59;
        const ulong WhiteKingSideCastleBitboardMaskA = 1UL << 4 ^ 1UL << 6;
        const ulong WhiteKingSideCastleBitboardMaskB = 1UL << 7 ^ 1UL << 5;
        const ulong WhiteQueenSideCastleBitboardMaskA = 1UL << 4 ^ 1UL << 2;
        const ulong WhiteQueenSideCastleBitboardMaskB = 1UL << 0 ^ 1UL << 3;

        const byte AllButWhiteQueenSideCastle = 0;
        const byte AllButWhiteKingSideCastle = 0;
        const byte AllButBlackQueenSideCastle = 0;
        const byte AllButBlackKingSideCastle = 0;
        const byte AllButWhiteCastle = 0;
        const byte AllButBlackCastle = 0;
        public static void ApplyMoves(uint move,
           ref ulong pawn, ref ulong knight, ref ulong bishop, ref ulong rook, ref ulong queen,
           ref ulong white, ref ulong black, ref byte whitKing, ref byte blackKing, ref byte castleRights, ref byte EnpassantFile
   )
        {
            var movedPiece = move.GetMovedPiece();
            var fromSquare = move.GetFromSquare();
            var toSquare = move.GetToSquare();
            var moveType = move.GetMoveType();
            var moveMask = (1UL << fromSquare) ^ (1UL << toSquare);
            if (moveType == Constants.None)
            {
                // Normal move
                switch (movedPiece)
                {
                    case Constants.Pawn:
                        pawn ^= moveMask;
                        break;
                    case Constants.Knight:
                        knight ^= moveMask;
                        break;
                    case Constants.Bishop:
                        bishop ^= moveMask;
                        break;
                    case Constants.Rook:
                        if (fromSquare == 0)
                            castleRights = (byte)(castleRights & AllButWhiteQueenSideCastle);
                        else if (fromSquare == 7)
                            castleRights = (byte)(castleRights & AllButWhiteKingSideCastle);
                        rook ^= moveMask;
                        break;
                    case Constants.Queen:
                        queen ^= moveMask;
                        break;
                    case Constants.King:
                        whitKing = toSquare;
                        break;
                }
                white ^= moveMask;
                EnpassantFile = 8; // Reset
            }
            else if (moveType == Constants.CaptureMove)
            {
                // capture move
                var captureMask = (1UL << toSquare);
                knight &= ~captureMask;
                bishop &= ~captureMask;
                rook &= ~captureMask;
                queen &= ~captureMask;
                black ^= captureMask;

                switch (movedPiece)
                {
                    case Constants.Pawn:
                        pawn ^= moveMask;
                        break;
                    case Constants.Knight:
                        knight ^= moveMask;
                        break;
                    case Constants.Bishop:
                        bishop ^= moveMask;
                        break;
                    case Constants.Rook:
                        if (fromSquare == 0)
                            castleRights = (byte)(castleRights & AllButWhiteQueenSideCastle);
                        else if (fromSquare == 7)
                            castleRights = (byte)(castleRights & AllButWhiteKingSideCastle);
                        rook ^= moveMask;
                        break;
                    case Constants.Queen:
                        queen ^= moveMask;
                        break;
                    case Constants.King:
                        whitKing = toSquare;
                        break;
                }
                white ^= moveMask;
                EnpassantFile = 8; // Reset
            }
            else if (moveType == Constants.DoublePush)
            {
                // double push
                pawn ^= moveMask;
                white ^= moveMask;
                EnpassantFile = (byte)(fromSquare % 8);
            }
            else if (moveType == Constants.Castle)
            {
                // castle
                whitKing = toSquare;
                castleRights = (byte)(castleRights & AllButWhiteQueenSideCastle);

                if (toSquare == 6)
                {
                    // White king side castle
                    rook ^= WhiteKingSideCastleBitboardMaskB;
                    white ^= WhiteKingSideCastleBitboardMaskA | WhiteKingSideCastleBitboardMaskB;
                }
                else if (toSquare == 2)
                {
                    // White queen side castle
                    rook ^= WhiteQueenSideCastleBitboardMaskB;
                    white ^= WhiteQueenSideCastleBitboardMaskA | WhiteQueenSideCastleBitboardMaskB;
                }
                EnpassantFile = 8; // Reset
            }
            else if (moveType >= 9)
            {
                // capture promotion
                var captureMask = (1UL << toSquare);
                knight &= ~captureMask;
                bishop &= ~captureMask;
                rook &= ~captureMask;
                queen &= ~captureMask;
                black &= ~captureMask;

                switch (moveType)
                {
                    case Constants.KnightCapturePromotion:
                        knight |= (1UL << toSquare);
                        break;
                    case Constants.BishopCapturePromotion:
                        bishop |= (1UL << toSquare);
                        break;
                    case Constants.RookCapturePromotion:
                        rook |= (1UL << toSquare);
                        break;
                    case Constants.QueenCapturePromotion:
                        queen |= (1UL << toSquare);
                        break;
                }

                pawn &= ~(1UL << fromSquare);
                white ^= moveMask;
                EnpassantFile = 8; // Reset
            }
            else if(moveType >= 5)
            {
                // promotion
                switch (moveType)
                {
                    case Constants.KnightPromotion:
                        knight |= (1UL << toSquare);
                        break;
                    case Constants.BishopPromotion:
                        bishop |= (1UL << toSquare);
                        break;
                    case Constants.RookPromotion:
                        rook |= (1UL << toSquare);
                        break;
                    case Constants.QueenPromotion:
                        queen |= (1UL << toSquare);
                        break;
                }

                pawn &= ~(1UL << fromSquare);
                white ^= moveMask;
                EnpassantFile = 8; // Reset
            }
            else
            {
                // enpassant
                var captureMask = (1UL << toSquare);

                pawn ^= moveMask ^ captureMask;
                white ^= moveMask;
                black ^= captureMask;

                EnpassantFile = 8; // Reset
            }
        }

    }
}
