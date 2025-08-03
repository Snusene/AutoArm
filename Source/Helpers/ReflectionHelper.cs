// AutoArm RimWorld 1.5+ mod - automatic weapon management
// This file: Cached reflection operations for performance
// Prevents redundant reflection lookups across mod systems
// Uses: Type, method, field, and property caching with safe invocation
// Critical: Performance optimization for frequent reflection operations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using AutoArm.Helpers; using AutoArm.Logging;

namespace AutoArm.Helpers
{
    /// <summary>
    /// Centralized reflection caching (fixes #22)
    /// Prevents duplicate reflection lookups and caching
    /// </summary>
    public static class ReflectionHelper
    {
        private static readonly object cacheLock = new object();
        private static Dictionary<string, Type> typeCache = new Dictionary<string, Type>();
        private static Dictionary<string, MethodInfo> methodCache = new Dictionary<string, MethodInfo>();
        private static Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();
        private static Dictionary<string, PropertyInfo> propertyCache = new Dictionary<string, PropertyInfo>();
        private static Dictionary<string, Func<object>> fieldGetterCache = new Dictionary<string, Func<object>>();

        /// <summary>
        /// Get a cached type by full name
        /// </summary>
        public static Type GetCachedType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;

            lock (cacheLock)
            {
                if (!typeCache.TryGetValue(fullTypeName, out Type type))
                {
                    type = GenTypes.AllTypes.FirstOrDefault(t => t.FullName == fullTypeName);
                    typeCache[fullTypeName] = type;
                }
                return type;
            }
        }

        /// <summary>
        /// Get a cached method
        /// </summary>
        public static MethodInfo GetCachedMethod(Type type, string methodName, Type[] parameterTypes = null)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            string key = $"{type.FullName}.{methodName}";
            if (parameterTypes != null)
            {
                key += $"({string.Join(",", parameterTypes.Select(t => t.FullName))})";
            }

            lock (cacheLock)
            {
                if (!methodCache.TryGetValue(key, out MethodInfo method))
                {
                    if (parameterTypes != null)
                    {
                        method = type.GetMethod(methodName, parameterTypes);
                    }
                    else
                    {
                        method = type.GetMethod(methodName);
                    }
                    methodCache[key] = method;
                }
                return method;
            }
        }

        /// <summary>
        /// Get a cached field
        /// </summary>
        public static FieldInfo GetCachedField(Type type, string fieldName, BindingFlags? flags = null)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            string key = $"{type.FullName}.{fieldName}";
            if (flags.HasValue)
            {
                key += $".{flags.Value}";
            }

            lock (cacheLock)
            {
                if (!fieldCache.TryGetValue(key, out FieldInfo field))
                {
                    if (flags.HasValue)
                    {
                        field = type.GetField(fieldName, flags.Value);
                    }
                    else
                    {
                        field = type.GetField(fieldName);
                    }
                    fieldCache[key] = field;
                }
                return field;
            }
        }

        /// <summary>
        /// Get a cached property
        /// </summary>
        public static PropertyInfo GetCachedProperty(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrEmpty(propertyName))
                return null;

            string key = $"{type.FullName}.{propertyName}";

            lock (cacheLock)
            {
                if (!propertyCache.TryGetValue(key, out PropertyInfo property))
                {
                    property = type.GetProperty(propertyName);
                    propertyCache[key] = property;
                }
                return property;
            }
        }

        /// <summary>
        /// Invoke a method safely with caching
        /// </summary>
        public static object InvokeMethod(object instance, Type type, string methodName, object[] parameters = null)
        {
            var method = GetCachedMethod(type, methodName);
            if (method == null)
                return null;

            try
            {
                return method.Invoke(instance, parameters);
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogError($"Failed to invoke {type.Name}.{methodName}", ex);
                }
                return null;
            }
        }

        /// <summary>
        /// Get field value safely with caching
        /// </summary>
        public static object GetFieldValue(object instance, Type type, string fieldName)
        {
            var field = GetCachedField(type, fieldName);
            if (field == null)
                return null;

            try
            {
                return field.GetValue(instance);
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogError($"Failed to get field value {type.Name}.{fieldName}", ex);
                }
                return null;
            }
        }

        /// <summary>
        /// Get property value safely with caching
        /// </summary>
        public static object GetPropertyValue(object instance, Type type, string propertyName)
        {
            var property = GetCachedProperty(type, propertyName);
            if (property == null)
                return null;

            try
            {
                return property.GetValue(instance);
            }
            catch (Exception ex)
            {
                if (AutoArmMod.settings?.debugLogging == true)
                {
                    AutoArmLogger.LogError($"Failed to get property value {type.Name}.{propertyName}", ex);
                }
                return null;
            }
        }

        /// <summary>
        /// Clear all caches
        /// </summary>
        public static void ClearAllCaches()
        {
            lock (cacheLock)
            {
                typeCache.Clear();
                methodCache.Clear();
                fieldCache.Clear();
                propertyCache.Clear();
                fieldGetterCache.Clear();
            }
        }

        /// <summary>
        /// Cache a method by key for later retrieval
        /// </summary>
        public static void CacheMethod(string key, MethodInfo method)
        {
            if (!string.IsNullOrEmpty(key) && method != null)
            {
                lock (cacheLock)
                {
                    methodCache[key] = method;
                }
            }
        }

        /// <summary>
        /// Get a cached method by key
        /// </summary>
        public static MethodInfo GetCachedMethod(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            lock (cacheLock)
            {
                methodCache.TryGetValue(key, out MethodInfo method);
                return method;
            }
        }

        /// <summary>
        /// Cache a field getter function
        /// </summary>
        public static void CacheFieldGetter(string key, Func<object> getter)
        {
            if (!string.IsNullOrEmpty(key) && getter != null)
            {
                lock (cacheLock)
                {
                    fieldGetterCache[key] = getter;
                }
            }
        }

        /// <summary>
        /// Get a cached field value using a getter function
        /// </summary>
        public static T GetCachedFieldValue<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                return default(T);
                
            lock (cacheLock)
            {
                if (!fieldGetterCache.TryGetValue(key, out Func<object> getter))
                    return default(T);

                try
                {
                    var value = getter();
                    if (value is T typedValue)
                        return typedValue;
                    return default(T);
                }
                catch (Exception ex)
                {
                    if (AutoArmMod.settings?.debugLogging == true)
                    {
                        AutoArmLogger.LogError($"Failed to get cached field value for key {key}", ex);
                    }
                    return default(T);
                }
            }
        }
    }
}