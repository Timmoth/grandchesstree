namespace GrandChessTree.Shared.Helpers
{
    public static class Rng
    {
        // http://vigna.di.unimi.it/ftp/papers/xorshift.pdf
        public const ulong DefaultRandomSeed = 1070372;

        public static ulong Next(ref ulong seed)
        {
            var s = seed;
            s ^= s >> 12;
            s ^= s << 25;
            s ^= s >> 27;
            seed = s;
            return s * 2685821657736338717L;
        }

        public static ulong[] GenerateBatch(int length)
        {
            ulong[] batch = new ulong[length];

            var seed = DefaultRandomSeed;
            for (int i = 0; i < length; i++)
            {
                batch[i] = Next(ref seed);
            }

            return batch;
        }
    }
}
