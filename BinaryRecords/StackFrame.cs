using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace BinaryRecords
{
    public class StackFrame
    {
        private Dictionary<string, (ParameterExpression, int)> _parameters = new();
        private Dictionary<(Type Type, string Name), ParameterExpression> _variables = new();

        public ParameterExpression CreateParameter(Type type, string name)
        {
            var param = Expression.Parameter(type, name);
            _parameters[name] = (param, _parameters.Count);
            return param;
        }

        public ParameterExpression CreateParameter<T>(string name) => CreateParameter(typeof(T), name);

        public ParameterExpression GetParameter(string name)
        {
            return _parameters[name].Item1;
        }
            
        public ParameterExpression GetOrCreateVariable(Type type)
        {
            if (_variables.TryGetValue((type, string.Empty), out var param))
                return param;

            param = Expression.Variable(type);
            _variables[(type, string.Empty)] = param;
            return param;
        }

        public IEnumerable<ParameterExpression> Variables => _variables.Values;
        public IEnumerable<ParameterExpression> Parameters => _parameters.Values
            .OrderBy(k => k.Item2)
            .Select(k => k.Item1);
    }
}
