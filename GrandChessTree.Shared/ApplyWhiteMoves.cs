using GrandChessTree.Shared.Helpers;
using GrandChessTree.Shared.Moves;
using GrandChessTree.Shared.Precomputed;

namespace GrandChessTree.Shared;

public partial struct Board
{
    public unsafe void ApplyWhiteMove(uint move)
    {
        var movedPiece = move.GetMovedPiece();
        var moveType = move.GetMoveType();
        var fromSquare = move.GetFromSquare();
        var toSquare = move.GetToSquare();

        if (moveType == Constants.NormalMove)
        {
            switch (movedPiece)
            {
                case Constants.Pawn:
                    WhitePawn_Move(fromSquare, toSquare);
                    break;
                case Constants.Knight:
                    WhiteKnight_Move(fromSquare, toSquare);
                    break;
                case Constants.Bishop:
                    WhiteBishop_Move(fromSquare, toSquare);
                    break;
                case Constants.Rook:
                    WhiteRook_Move(fromSquare, toSquare);
                    break;
                case Constants.Queen:
                    WhiteQueen_Move(fromSquare, toSquare);
                    break;
                case Constants.King:
                    WhiteKing_Move(fromSquare, toSquare);
                    break;
            }
        }
        else if (moveType == Constants.CaptureMove)
        {
            switch (movedPiece)
            {
                case Constants.Pawn:
                    WhitePawn_Capture(fromSquare, toSquare);
                    break;
                case Constants.Knight:
                    WhiteKnight_Capture(fromSquare, toSquare);
                    break;
                case Constants.Bishop:
                    WhiteBishop_Capture(fromSquare, toSquare);
                    break;
                case Constants.Rook:
                    WhiteRook_Capture(fromSquare, toSquare);
                    break;
                case Constants.Queen:
                    WhiteQueen_Capture(fromSquare, toSquare);
                    break;
                case Constants.King:
                    WhiteKing_Capture(fromSquare, toSquare);
                    break;
            }
        }
        else if (moveType == Constants.Castle)
        {
            if (toSquare == 6)
            {
                WhiteKing_KingSideCastle();
            }
            else
            {
                WhiteKing_QueenSideCastle();
            }
        }
        else if (moveType == Constants.DoublePush)
        {
            WhitePawn_DoublePush(fromSquare, toSquare);
        }
        else if (moveType == Constants.EnPassant)
        {
            WhitePawn_Enpassant(fromSquare, toSquare);
        }
        else if (moveType == Constants.KnightPromotion)
        {
            WhitePawn_KnightPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.BishopPromotion)
        {
            WhitePawn_BishopPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.RookPromotion)
        {
            WhitePawn_RookPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.QueenPromotion)
        {
            WhitePawn_QueenPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.KnightCapturePromotion)
        {
            WhitePawn_Capture_KnightPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.BishopCapturePromotion)
        {
            WhitePawn_Capture_BishopPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.RookCapturePromotion)
        {
            WhitePawn_Capture_RookPromotion(fromSquare, toSquare);
        }
        else if (moveType == Constants.QueenCapturePromotion)
        {
            WhitePawn_Capture_QueenPromotion(fromSquare, toSquare);
        }
    }


    public unsafe void WhitePawn_Enpassant(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        var captureSquare = 1UL << (fromSquare.GetRankIndex() * 8 + EnPassantFile);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                *(Zobrist.PiecesArray + Zobrist.BlackPawn + (fromSquare.GetRankIndex() * 8 + EnPassantFile)) ^ Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= moveMask ^ captureSquare;
        White ^= moveMask;
        Black ^= captureSquare;

        
        EnPassantFile = 8;
    }

    public unsafe void WhitePawn_Capture_KnightPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Knight |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);
        Black &= ~captureMask;

        EnPassantFile = 8;
    }

    public unsafe void WhitePawn_Capture_BishopPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Bishop |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);
        Black &= ~captureMask;

        EnPassantFile = 8;
    }

    public unsafe void WhitePawn_Capture_RookPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Rook |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);
        Black &= ~captureMask;

        EnPassantFile = 8;
    }

    public unsafe void WhitePawn_Capture_QueenPromotion(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;
        if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        Pawn ^= 1UL << fromSquare;
        Queen |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);
        Black &= ~captureMask;

        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_Capture(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;

        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            zobristOffset = Zobrist.BlackPawn;
        }
        else if ((Knight  & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= moveMask;
        White ^= moveMask;
        Black &= ~captureMask;
        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_KnightPromotion(int fromSquare, int toSquare)
    {
        ulong fromBit = 1UL << fromSquare;
        ulong toBit = 1UL << toSquare;
        ulong moveMask = fromBit | toBit;

        Pawn ^= fromBit;   // Remove piece from 'fromSquare'
        Knight |= toBit;   // Place piece at 'toSquare'
        White ^= moveMask; // Update occupancy

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_BishopPromotion(int fromSquare, int toSquare)
    {
        Pawn ^= 1UL << fromSquare;
        Bishop |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_RookPromotion(int fromSquare, int toSquare)
    {
        Pawn ^= 1UL << fromSquare;
        Rook |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_QueenPromotion(int fromSquare, int toSquare)
    {
        Pawn ^= 1UL << fromSquare;
        Queen |= 1UL << toSquare;
        White ^= (1UL << fromSquare) | (1UL << toSquare);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= moveMask;
        White ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhitePawn_DoublePush(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Pawn ^= moveMask;
        White ^= moveMask;
        byte enPassantFile = (byte)(fromSquare % 8);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhitePawn + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhitePawn + toSquare) ^
                 *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + enPassantFile)^
                Zobrist.SideToMove;
        EnPassantFile = enPassantFile;
    }


    internal unsafe void WhiteKnight_Capture(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;

        if ((Pawn & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            zobristOffset = Zobrist.BlackPawn;
        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Knight ^= moveMask;
        White ^= moveMask;
        Black &= ~captureMask;
        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKnight + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void WhiteKnight_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Knight ^= moveMask;
        White ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKnight + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKnight + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhiteBishop_Capture(int fromSquare, int toSquare)
    {
        var captureMask = (1UL << toSquare);
        int zobristOffset;

        if ((Pawn & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            zobristOffset = Zobrist.BlackPawn;
        }
        else if ((Knight & captureMask) != 0)
        {
            Knight &= ~captureMask;
            zobristOffset = Zobrist.BlackKnight;
        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            zobristOffset = Zobrist.BlackBishop;
        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            zobristOffset = Zobrist.BlackRook;
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            zobristOffset = Zobrist.BlackQueen;
        }
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Bishop ^= moveMask;
        White ^= moveMask;
        Black &= ~captureMask;
        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteBishop + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                *(Zobrist.PiecesArray + zobristOffset + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        EnPassantFile = 8;
    }

    internal unsafe void WhiteBishop_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Bishop ^= moveMask;
        White ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteBishop + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteBishop + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhiteRook_Capture(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);

        var prevCastleRights = CastleRights;
        if (fromSquare == 0)
            CastleRights &= ~CastleRights.WhiteQueenSide;
        else if (fromSquare == 7)
            CastleRights &= ~CastleRights.WhiteKingSide;

        var captureMask = (1UL << toSquare);
        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Knight  & captureMask) != 0)
        {
            Knight &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Rook & captureMask) != 0)
        {
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));

            Rook &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        }
        else
        {
            Queen &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteRook + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }

        Rook ^= moveMask;
        White ^= moveMask;
        Black &= ~captureMask;

        
        EnPassantFile = 8;
    }

    internal unsafe void WhiteRook_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Rook ^= moveMask;
        White ^= moveMask;

        var prevCastleRights = CastleRights;
        CastleRights &= ~(((fromSquare == 0) ? CastleRights.WhiteQueenSide : 0) |
       ((fromSquare == 7) ? CastleRights.WhiteKingSide : 0));

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteRook + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        
        EnPassantFile = 8;
    }

    internal unsafe void WhiteQueen_Capture(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        var captureMask = (1UL << toSquare);

        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);
        }
        else if ((Knight  & captureMask) != 0)
        {
            Knight &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        }
        else if ((Rook & captureMask) != 0)
        {
            Rook &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);
            var prevCastleRights = CastleRights;
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));
            Hash ^= *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteQueen + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);
        }

        Queen ^= moveMask;
        White ^= moveMask;
        Black &= ~captureMask;

        
        EnPassantFile = 8;
    }

    internal unsafe void WhiteQueen_Move(int fromSquare, int toSquare)
    {
        var moveMask = (1UL << fromSquare) | (1UL << toSquare);
        Queen ^= moveMask;
        White ^= moveMask;

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteQueen + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteQueen + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8);

        
        EnPassantFile = 8;
    }

    internal unsafe void WhiteKing_Capture(int fromSquare, int toSquare)
    {
        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide);

        var captureMask = (1UL << toSquare);

        if ((Pawn  & captureMask) != 0)
        {
            Pawn &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackPawn + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Knight  & captureMask) != 0)
        {
            Knight &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackKnight + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        }
        else if ((Bishop & captureMask) != 0)
        {
            Bishop &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackBishop + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else if ((Rook & captureMask) != 0)
        {
            CastleRights &= ~(((toSquare == 56) ? CastleRights.BlackQueenSide : 0) |
                               ((toSquare == 63) ? CastleRights.BlackKingSide : 0));

            Rook &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackRook + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }
        else
        {
            Queen &= ~captureMask;
            Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + fromSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.WhiteKing + toSquare) ^
                    *(Zobrist.PiecesArray + Zobrist.BlackQueen + toSquare) ^
                    Zobrist.SideToMove ^
                    *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                    *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));
        }

        White ^= (1UL << fromSquare) | (1UL << toSquare);
        Black &= ~captureMask;

        
        EnPassantFile = 8;
        WhiteKingPos = (byte)toSquare;
    }

    internal unsafe void WhiteKing_Move(int fromSquare, int toSquare)
    {
        White ^= (1UL << fromSquare) | (1UL << toSquare);

        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + fromSquare) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKing + toSquare) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));


        EnPassantFile = 8;
        WhiteKingPos = (byte)toSquare;
    }

    internal unsafe void WhiteKing_KingSideCastle()
    {
        Rook ^= (1UL << 7) | (1UL << 5);
        White ^= (1UL << 4) | (1UL << 7);
        White |= (1UL << 6) | (1UL << 5);

        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + 4) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKing + 6) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + 7) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + 5) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        EnPassantFile = 8;
        WhiteKingPos = 6;
    }

    internal unsafe void WhiteKing_QueenSideCastle()
    {
        Rook ^= (1UL << 0) | (1UL << 3);
        White ^= (1UL << 4) | (1UL << 0);
        White |= (1UL << 2) | (1UL << 3);

        var prevCastleRights = CastleRights;
        CastleRights &= ~(CastleRights.WhiteKingSide | CastleRights.WhiteQueenSide);

        Hash ^= *(Zobrist.PiecesArray + Zobrist.WhiteKing + 4) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteKing + 2) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + 0) ^
                *(Zobrist.PiecesArray + Zobrist.WhiteRook + 3) ^
                Zobrist.SideToMove ^
                *(Zobrist.DeltaEnpassant + EnPassantFile * 9 + 8) ^
                *(Zobrist.DeltaCastleRights + (int)(prevCastleRights ^ CastleRights));

        EnPassantFile = 8;
        WhiteKingPos = 2;
    }
}