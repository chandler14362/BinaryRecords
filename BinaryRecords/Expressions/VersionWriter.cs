using System;
using System.Linq.Expressions;
using BinaryRecords.Enums;
using BinaryRecords.Expressions;

namespace BinaryRecords.Expressions
{
    public sealed class VersionWriter
    {
        private readonly uint _key;
        private readonly Expression _versionHeaderBuffer;

        private Expression? _startingSize;

        public VersionWriter(uint key, Expression versionHeaderBuffer)
        {
            _key = key;
            _versionHeaderBuffer = versionHeaderBuffer;
        }
        
        public void Start(
            ExpressionBlockBuilder blockBuilder, 
            Expression buffer, 
            BitSize bitSize)
        {
            if (bitSize != BitSize.B32)
                throw new NotImplementedException();
            _startingSize = blockBuilder.CreateVariable<int>();
            blockBuilder += Expression.Assign(
                _startingSize, 
                BufferWriterExpressions.Size(buffer));
        }

        public void Stop(
            ExpressionBlockBuilder blockBuilder, 
            Expression buffer,
            BitSize bitSize)
        {
            if (bitSize != BitSize.B32)
                throw new NotImplementedException();
            blockBuilder += BufferWriterExpressions.WriteUInt32(_versionHeaderBuffer, Expression.Constant(_key));
            var sizeValue = Expression.Convert(
                Expression.Subtract(BufferWriterExpressions.Size(buffer), _startingSize!),
                typeof(uint));
            blockBuilder += BufferWriterExpressions.WriteUInt32(_versionHeaderBuffer, sizeValue);
        }
    }
}
