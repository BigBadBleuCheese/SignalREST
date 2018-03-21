using Microsoft.AspNet.SignalR.Hubs;
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
            foreach (var methodNameAndInfo in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => Tuple.Create(m.GetCustomAttribute<HubMethodNameAttribute>()?.MethodName ?? m.Name, m)).Where(t => !bannedMethodNames.Contains(t.Item1)))
            {
                Dictionary<int, Tuple<Type, Type[], FastMethodInfo>> methodOverloads;
                if (!methodNames.TryGetValue(methodNameAndInfo.Item1, out methodOverloads))
                {
                    methodOverloads = new Dictionary<int, Tuple<Type, Type[], FastMethodInfo>>();
                    methodNames.Add(methodNameAndInfo.Item1, methodOverloads);
                }
                var parameters = methodNameAndInfo.Item2.GetParameters();
                var arity = parameters.Length;
                if (methodOverloads.ContainsKey(arity))
                    throw new InvalidOperationException("Cannot have more than one method overload (case insensitive) with the same arity");
                FastMethodInfo fastMethodInfo;
                try
                {
                    fastMethodInfo = new FastMethodInfo(methodNameAndInfo.Item2);
                }
                catch
                {
                    if (methodOverloads.Count == 0)
                        methodNames.Remove(methodNameAndInfo.Item1);
                    continue;
                }
                methodOverloads.Add(arity, Tuple.Create(methodNameAndInfo.Item2.ReturnType, parameters.Select(parameter => parameter.ParameterType).ToArray(), fastMethodInfo));
            }
            MethodNames = methodNames.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<int, Tuple<Type, Type[], FastMethodInfo>>)kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        public Type Type { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, Tuple<Type, Type[], FastMethodInfo>>> MethodNames { get; }
    }
}
