namespace GrandChessTree.Api.timescale
{
    public static class IdGenerator
    {
        private const string ValidIdCharacters = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static readonly Random RandomInstance = new Random();
        private const int IdLength = 12;

        public static string Generate()
        {
            Span<char> id = stackalloc char[IdLength];
            for (int i = 0; i < IdLength; i++)
            {
                id[i] = ValidIdCharacters[RandomInstance.Next(ValidIdCharacters.Length)];
            }
            return new string(id);
        }
    }
}
