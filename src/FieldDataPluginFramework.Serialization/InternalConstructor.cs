using System;
using System.Reflection;

namespace FieldDataPluginFramework.Serialization
{
    public class InternalConstructor<T> where T : class
    {
        public static T Invoke(params object[] args)
        {
            return Activator.CreateInstance(typeof(T), BindingFlags.NonPublic | BindingFlags.Instance, null, args, null) as T;
        }
    }
}
