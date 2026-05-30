namespace MoveGen.App;

public enum MoveFlag : byte
{
    Quiet                = 0,
    DoublePawnPush       = 1,
    KingsideCastle       = 2,
    QueensideCastle      = 3,
    Capture              = 4,
    EnPassant            = 5,
    PromoteKnight        = 8,
    PromoteBishop        = 9,
    PromoteRook          = 10,
    PromoteQueen         = 11,
    PromoCaptureKnight   = 12,
    PromoCaptureBishop   = 13,
    PromoCaptureRook     = 14,
    PromoCaptureQueen    = 15,
}

public readonly struct Move : IEquatable<Move>
{
    private readonly ushort _value;

    public Move(int from, int to, MoveFlag flag)
    {
        _value = (ushort)(((int)flag << 12) | ((from & 0x3F) << 6) | (to & 0x3F));
    }

    public int      To    =>             _value         & 0x3F;
    public int      From  =>            (_value >>  6)  & 0x3F;
    public MoveFlag Flag  => (MoveFlag)((_value >> 12)  & 0x0F);

    public bool IsCapture   => ((int)Flag & 0b0100) != 0;
    public bool IsPromotion => ((int)Flag & 0b1000) != 0;
    public bool IsEnPassant => Flag == MoveFlag.EnPassant;
    public bool IsCastle    => Flag == MoveFlag.KingsideCastle
                            || Flag == MoveFlag.QueensideCastle;

    /// 0=Knight, 1=Bishop, 2=Rook, 3=Queen. Only valid when IsPromotion.
    public int PromotionPieceIndex => (int)Flag & 0b0011;

    public bool Equals(Move other) => _value == other._value;
    public override bool Equals(object? o) => o is Move m && Equals(m);
    public override int GetHashCode() => _value;
    public static bool operator ==(Move a, Move b) => a._value == b._value;
    public static bool operator !=(Move a, Move b) => a._value != b._value;
}
