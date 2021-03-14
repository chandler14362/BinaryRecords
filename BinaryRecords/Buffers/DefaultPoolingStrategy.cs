namespace BinaryRecords.Buffers
{
    // Default pooling strategy is no pooling at all
    internal class DefaultPoolingStrategy : IPoolingStrategy
    {
        public static readonly IPoolingStrategy Instance = new DefaultPoolingStrategy();

        private const int GrowthFactor = 2;

        public byte[] Resize(int size, int neededLength)
        {
            var newLength = size * GrowthFactor;
            while (neededLength > newLength)
                newLength *= GrowthFactor;
            return new byte[newLength];
        }

        public void Free(byte[] data)
        {
        }
    }
}