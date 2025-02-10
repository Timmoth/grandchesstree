using ILGPU;
using ILGPU.Runtime;

namespace GrandChessTree.Client.Worker
{
    public struct TotalStatsLayerBuffers
    {
        // 13 * 64 bytes each board

        public ArrayView1D<ulong, Stride1D.Dense> Nodes;
        public ArrayView1D<ulong, Stride1D.Dense> Captures;
        public ArrayView1D<ulong, Stride1D.Dense> Enpassant;
        public ArrayView1D<ulong, Stride1D.Dense> Castles;
        public ArrayView1D<ulong, Stride1D.Dense> Promotions;
        public ArrayView1D<ulong, Stride1D.Dense> DirectCheck;
        public ArrayView1D<ulong, Stride1D.Dense> SingleDiscoveredCheck;
        public ArrayView1D<ulong, Stride1D.Dense> DirectDiscoveredCheck;
        public ArrayView1D<ulong, Stride1D.Dense> DoubleDiscoveredCheck;
        public ArrayView1D<ulong, Stride1D.Dense> DirectCheckmate;
        public ArrayView1D<ulong, Stride1D.Dense> SingleDiscoveredCheckmate;
        public ArrayView1D<ulong, Stride1D.Dense> DirectDiscoverdCheckmate;
        public ArrayView1D<ulong, Stride1D.Dense> DoubleDiscoverdCheckmate;

        public TotalStatsLayerBuffers(
            ArrayView1D<ulong, Stride1D.Dense> nodes,
            ArrayView1D<ulong, Stride1D.Dense> captures,
            ArrayView1D<ulong, Stride1D.Dense> enpassant,
            ArrayView1D<ulong, Stride1D.Dense> castles,
            ArrayView1D<ulong, Stride1D.Dense> promotions,
            ArrayView1D<ulong, Stride1D.Dense> directCheck,
            ArrayView1D<ulong, Stride1D.Dense> singleDiscoveredCheck,
            ArrayView1D<ulong, Stride1D.Dense> directDiscoveredCheck,
            ArrayView1D<ulong, Stride1D.Dense> doubleDiscoveredCheck,
            ArrayView1D<ulong, Stride1D.Dense> directCheckmate,
            ArrayView1D<ulong, Stride1D.Dense> singleDiscoveredCheckmate,
            ArrayView1D<ulong, Stride1D.Dense> directDiscoverdCheckmate,
            ArrayView1D<ulong, Stride1D.Dense> doubleDiscoverdCheckmate)
        {
            Nodes = nodes;
            Captures = captures;
            Enpassant = enpassant;
            Castles = castles;
            Promotions = promotions;
            DirectCheck = directCheck;
            SingleDiscoveredCheck = singleDiscoveredCheck;
            DirectDiscoveredCheck = directDiscoveredCheck;
            DoubleDiscoveredCheck = doubleDiscoveredCheck;
            DirectCheckmate = directCheckmate;
            SingleDiscoveredCheckmate = singleDiscoveredCheckmate;
            DirectDiscoverdCheckmate = directDiscoverdCheckmate;
            DoubleDiscoverdCheckmate = doubleDiscoverdCheckmate;
        }

        internal void MemSetZero()
        {
            Nodes.MemSetToZero();
            Captures.MemSetToZero();
            Enpassant.MemSetToZero();
            Castles.MemSetToZero();
            Promotions.MemSetToZero();
            DirectCheck.MemSetToZero();
            SingleDiscoveredCheck.MemSetToZero();
            DirectDiscoveredCheck.MemSetToZero();
            DoubleDiscoveredCheck.MemSetToZero();
            DirectCheckmate.MemSetToZero();
            SingleDiscoveredCheckmate.MemSetToZero();
            DirectDiscoverdCheckmate.MemSetToZero();
            DoubleDiscoverdCheckmate.MemSetToZero();
        }
    }
}
