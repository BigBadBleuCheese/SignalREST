using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SignalRest
{
    internal class ReflectedHub
    {
        public ReflectedHub(Type type)
        {
            Type = type;
            var methodNames = new Dictionary<string, Dictionary<int, Tuple<Type, Type[], FastMethodInfo>>>(StringComparer.OrdinalIgnoreCase);
            var bannedMethodNames = new HashSet<string>(new string[]
            {
                nameof(Hub.OnConnected),
                nameof(Hub.OnReconnected),
                nameof(Hub.OnDisconnected)
            }, StringComparer.OrdinalIgnoreCase);
            foreach (var methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !bannedMethodNames.Contains(m.Name)))
            {
                Dictionary<int, Tuple<Type, Type[], FastMethodInfo>> methodOverloads;
                if (!methodNames.TryGetValue(methodInfo.Name, out methodOverloads))
                {
                    methodOverloads = new Dictionary<int, Tuple<Type, Type[], FastMethodInfo>>();
                    methodNames.Add(methodInfo.Name, methodOverloads);
                }
                var parameters = methodInfo.GetParameters();
                var arity = parameters.Length;
                if (methodOverloads.ContainsKey(arity))
                    throw new InvalidOperationException("Cannot have more than one method overload (case insensitive) with the same arity");
                methodOverloads.Add(arity, Tuple.Create(methodInfo.ReturnType, parameters.Select(parameter => parameter.ParameterType).ToArray(), new FastMethodInfo(methodInfo)));
            }
            MethodNames = methodNames.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, Tuple<Type, Type[], FastMethodInfo>>)kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        public Type Type { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, Tuple<Type, Type[], FastMethodInfo>>> MethodNames { get; }
    }
}
