using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace BinaryRecords
{
    public class StackFrame
    {
        private Dictionary<string, (ParameterExpression, int)> _parameters = new();
        
        private List<ParameterExpression> _variables = new();
        private Dictionary<string, ParameterExpression> _variableDictionary = new();
        
        public ParameterExpression CreateParameter(Type type, string name)
        {
            var param = Expression.Parameter(type, name);
            _parameters[name] = (param, _parameters.Count);
            return param;
        }

        public ParameterExpression CreateParameter<T>(string name) 
            => CreateParameter(typeof(T), name);

        public ParameterExpression GetParameter(string name)
            => _parameters[name].Item1;

        private ParameterExpression TrackVariable(ParameterExpression variable)
        {
            if (variable.Name != null)
                _variableDictionary[variable.Name] = variable;
            _variables.Add(variable);
            return variable;
        }

        public ParameterExpression CreateVariable(Type type)
            => TrackVariable(Expression.Variable(type));

        public ParameterExpression CreateVariable<T>()
            => CreateVariable(typeof(T));

        public ParameterExpression CreateVariable(Type type, string name)
            => TrackVariable(Expression.Variable(type, name));

        public ParameterExpression CreateVariable<T>(string name)
            => CreateVariable(typeof(T), name);
        
        public ParameterExpression GetVariable(string name)
            => _variableDictionary[name];

        public ParameterExpression GetOrCreateVariable(Type type, string name)
            => _variableDictionary.TryGetValue(name, out var variable) 
                ? variable 
                : CreateVariable(type, name);
        
        public ParameterExpression GetOrCreateVariable<T>(string name)
            => GetOrCreateVariable(typeof(T), name);

        public IEnumerable<ParameterExpression> Variables => _variables;
        
        public IEnumerable<ParameterExpression> Parameters => _parameters.Values
            .OrderBy(k => k.Item2)
            .Select(k => k.Item1);
    }
}
