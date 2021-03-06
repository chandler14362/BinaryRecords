namespace BinaryRecords.Buffers
{
    public interface IPoolingStrategy
    {
        /// <summary>
        /// Called whenever a buffer writer needs to resize
        /// </summary>
        /// <param name="size">The current size of the buffer</param>
        /// <param name="neededLength">The needed length of the buffer</param>
        /// <returns>The resized buffer</returns>
        byte[] Resize(int size, int neededLength);

        /// <summary>
        /// Called whenever a buffer writer needs to free pooled data
        /// </summary>
        /// <param name="data">The pooled data</param>
        void Free(byte[] data);
    }
}