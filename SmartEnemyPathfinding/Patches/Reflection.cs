using System;
using System.Reflection;

namespace SmartEnemyPathfinding.Patches;

internal static class Reflection
{
    public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingFlags, Type[] parameters)
    {
        return type.GetMethod(name, bindingFlags, null, parameters, null);
    }
}
