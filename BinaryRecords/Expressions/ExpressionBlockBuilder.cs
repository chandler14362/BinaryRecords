using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace BinaryRecords.Expressions
{
    public sealed class ExpressionBlockBuilder
    {
        private readonly List<ParameterExpression> _variables = new();
        private readonly Dictionary<string, ParameterExpression> _variableDictionary = new();
        private readonly List<Expression> _expressions = new();

        public void Add(Expression expression) => _expressions.Add(expression);

        public void AddRange(IEnumerable<Expression> expressions) => _expressions.AddRange(expressions);

        public static ExpressionBlockBuilder operator +(ExpressionBlockBuilder builder, Expression expression)
        {
            builder.Add(expression);
            return builder;
        }
        
        public static ExpressionBlockBuilder operator +(ExpressionBlockBuilder builder, IEnumerable<Expression> expressions)
        {
            builder.AddRange(expressions);
            return builder;
        }
        
        private ParameterExpression TrackVariable(ParameterExpression variable)
        {
            if (variable.Name != null)
                _variableDictionary[variable.Name] = variable;
            _variables.Add(variable);
            return variable;
        }

        public ParameterExpression CreateVariable(Type type) => TrackVariable(Expression.Variable(type));

        public ParameterExpression CreateVariable<T>() => CreateVariable(typeof(T));

        public ParameterExpression CreateVariable(Type type, string name) => TrackVariable(Expression.Variable(type, name));

        public ParameterExpression CreateVariable<T>(string name) => CreateVariable(typeof(T), name);
        
        public ParameterExpression GetVariable(string name) => _variableDictionary[name];

        public ParameterExpression GetOrCreateVariable(Type type, string name) => 
            !_variableDictionary.TryGetValue(name, out var variable) 
                ? CreateVariable(type, name) 
                : variable ;
        
        public ParameterExpression GetOrCreateVariable<T>(string name) => GetOrCreateVariable(typeof(T), name);

        public IEnumerable<ParameterExpression> Variables => _variables;

        public BlockExpression Build() => Expression.Block(_variables, _expressions);

        public static implicit operator BlockExpression(ExpressionBlockBuilder builder) => builder.Build();
    }
}
