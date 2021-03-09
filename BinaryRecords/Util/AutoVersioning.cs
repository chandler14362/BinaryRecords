using System.Linq.Expressions;

namespace BinaryRecords.Util
{
    public sealed class AutoVersioning
    {
        private readonly uint _key;
        private readonly Expression _versionHeaderBuffer;

        private Expression? _startingSize;

        public AutoVersioning(uint key, Expression versionHeaderBuffer)
        {
            _key = key;
            _versionHeaderBuffer = versionHeaderBuffer;
        }
        
        public void StartVersioning(ExpressionBlockBuilder blockBuilder, Expression bufferAccess)
        {
            _startingSize = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(
                _startingSize, 
                BufferWriterExpressions.BufferSize(bufferAccess));
        }

        public void EndVersioning(ExpressionBlockBuilder blockBuilder, Expression bufferAccess)
        {
            blockBuilder += BufferWriterExpressions.WriteUInt32(
                _versionHeaderBuffer,
                Expression.Constant(_key));
            blockBuilder += BufferWriterExpressions.WriteUInt32(
                _versionHeaderBuffer,
                Expression.Subtract(BufferWriterExpressions.BufferSize(bufferAccess), _startingSize!));
        }
    }
}