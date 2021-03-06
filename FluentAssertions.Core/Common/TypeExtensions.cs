using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using FluentAssertions.Equivalency;

namespace FluentAssertions.Common
{
    public static class TypeExtensions
    {
        private const BindingFlags PublicMembersFlag =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Determines whether the specified method has been annotated with a specific attribute.
        /// </summary>
        /// <returns>
        ///   <c>true</c> if the specified method has attribute; otherwise, <c>false</c>.
        /// </returns>
        public static bool HasAttribute<TAttribute>(this MemberInfo method) where TAttribute : Attribute
        {
            return (method.GetCustomAttributes(typeof(TAttribute), true).Any());
        }

        public static bool HasMatchingAttribute<TAttribute>(this MemberInfo type, Expression<Func<TAttribute, bool>> isMatchingAttributePredicate)
            where TAttribute : Attribute
        {
            Func<TAttribute, bool> isMatchingAttribute = isMatchingAttributePredicate.Compile();

            return GetCustomAttributes<TAttribute>(type).Any(isMatchingAttribute);
        }

        public static bool IsDecoratedWith<TAttribute>(this MemberInfo type)
            where TAttribute : Attribute
        {
            return GetCustomAttributes<TAttribute>(type).Any();
        }

        private static IEnumerable<TAttribute> GetCustomAttributes<TAttribute>(MemberInfo type)
        {
            return type.GetCustomAttributes(false).OfType<TAttribute>();
        }

        /// <summary>
        /// Determines whether two <see cref="ISelectedMemberInfo"/> objects refer to the same member.
        /// </summary>
        public static bool IsEquivalentTo(this ISelectedMemberInfo property, ISelectedMemberInfo otherProperty)
        {
            return (property.DeclaringType.IsSameOrInherits(otherProperty.DeclaringType) ||
                    otherProperty.DeclaringType.IsSameOrInherits(property.DeclaringType)) &&
                   (property.Name == otherProperty.Name);
        }

        public static bool IsSameOrInherits(this Type actualType, Type expectedType)
        {
            return (actualType == expectedType) ||
                   (expectedType.IsAssignableFrom(actualType));
        }

        public static bool Implements<TInterface>(this Type type)
        {
            return Implements(type, typeof (TInterface));
        }

        /// <summary>
        /// NOTE: This method does not give the expected results with open generics
        /// </summary>
        public static bool Implements(this Type type, Type expectedBaseType)
        {
            return
                expectedBaseType.IsAssignableFrom(type)
                && (type != expectedBaseType);
        }

        internal static Type[] GetClosedGenericInterfaces(Type type, Type openGenericType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == openGenericType)
            {
                return new[] { type };
            }

            Type[] interfaces = type.GetInterfaces();
            return
                interfaces
                    .Where(t => (t.IsGenericType && (t.GetGenericTypeDefinition() == openGenericType)))
                    .ToArray();
        }

        public static bool IsComplexType(this Type type)
        {
            return HasProperties(type) && !AssertionOptions.IsValueType(type);
        }

        private static bool HasProperties(Type type)
        {
            return type.GetProperties(PublicMembersFlag).Any();
        }

        /// <summary>
        /// Finds a member by its case-sensitive name.
        /// </summary>
        /// <returns>
        /// Returns <c>null</c> if no such member exists.
        /// </returns>
        public static ISelectedMemberInfo FindMember(this Type type, string memberName, Type preferredType)
        {
            return SelectedMemberInfo.Create(FindProperty(type, memberName, preferredType)) ??
                   SelectedMemberInfo.Create(FindField(type, memberName, preferredType));
        }

        /// <summary>
        /// Finds the property by a case-sensitive name.
        /// </summary>
        /// <returns>
        /// Returns <c>null</c> if no such property exists.
        /// </returns>
        public static PropertyInfo FindProperty(this Type type, string propertyName, Type preferredType)
        {
            IEnumerable<PropertyInfo> properties =
                type.GetProperties(PublicMembersFlag)
                    .Where(pi => pi.Name == propertyName)
                    .ToList();
            
            return (properties.Count() > 1)
                ? properties.SingleOrDefault(p => p.PropertyType == preferredType)
                : properties.SingleOrDefault();
        }

        /// <summary>
        /// Finds the field by a case-sensitive name.
        /// </summary>
        /// <returns>
        /// Returns <c>null</c> if no such property exists.
        /// </returns>
        public static FieldInfo FindField(this Type type, string fieldName, Type preferredType)
        {
            IEnumerable<FieldInfo> properties =
                type.GetFields(PublicMembersFlag)
                    .Where(pi => pi.Name == fieldName)
                    .ToList();

            return (properties.Count() > 1)
                ? properties.SingleOrDefault(p => p.FieldType == preferredType)
                : properties.SingleOrDefault();
        }

        public static IEnumerable<ISelectedMemberInfo> GetNonPrivateMembers(this Type typeToReflect)
        {
            return
                GetNonPrivateProperties(typeToReflect)
                    .Select(SelectedMemberInfo.Create)
                    .Concat(GetNonPrivateFields(typeToReflect).Select(SelectedMemberInfo.Create))
                    .ToArray();
        }

        public static IEnumerable<PropertyInfo> GetNonPrivateProperties(this Type typeToReflect, IEnumerable<string> filter = null)
        {
            var query =
                from propertyInfo in GetPropertiesFromHierarchy(typeToReflect)
                where HasNonPrivateGetter(propertyInfo)
                where !propertyInfo.IsIndexer()
                where (filter == null) || filter.Contains(propertyInfo.Name)
                select propertyInfo;

            return query.ToArray();
        }

        public static IEnumerable<FieldInfo> GetNonPrivateFields(this Type typeToReflect)
        {
            var query =
                from fieldInfo in GetFieldsFromHierarchy(typeToReflect)
                where !fieldInfo.IsPrivate
                where !fieldInfo.IsFamily
                select fieldInfo;

            return query.ToArray();
        }

        private static IEnumerable<FieldInfo> GetFieldsFromHierarchy(Type typeToReflect)
        {
            return GetMembersFromHierarchy(typeToReflect, GetPublicFields);
        }

        private static IEnumerable<PropertyInfo> GetPropertiesFromHierarchy(Type typeToReflect)
        {
            return GetMembersFromHierarchy(typeToReflect, GetPublicProperties);
        }

        private static IEnumerable<TMemberInfo> GetMembersFromHierarchy<TMemberInfo>(
            Type typeToReflect,
            Func<Type, IEnumerable<TMemberInfo>> getMembers) where TMemberInfo : MemberInfo
        {
            if (IsInterface(typeToReflect))
            {
                var propertyInfos = new List<TMemberInfo>();

                var considered = new List<Type>();
                var queue = new Queue<Type>();
                considered.Add(typeToReflect);
                queue.Enqueue(typeToReflect);

                while (queue.Count > 0)
                {
                    var subType = queue.Dequeue();
                    foreach (var subInterface in GetInterfaces(subType))
                    {
                        if (considered.Contains(subInterface))
                        {
                            continue;
                        }

                        considered.Add(subInterface);
                        queue.Enqueue(subInterface);
                    }

                    IEnumerable<TMemberInfo> typeProperties = getMembers(subType);

                    IEnumerable<TMemberInfo> newPropertyInfos = typeProperties.Where(x => !propertyInfos.Contains(x));

                    propertyInfos.InsertRange(0, newPropertyInfos);
                }

                return propertyInfos.ToArray();
            }
            else
            {
                return getMembers(typeToReflect);
            }
        }

        private static bool IsInterface(Type typeToReflect)
        {
            return typeToReflect.IsInterface;
        }

        private static IEnumerable<Type> GetInterfaces(Type type)
        {
            return type.GetInterfaces();
        }

        private static IEnumerable<PropertyInfo> GetPublicProperties(Type type)
        {
            return type.GetProperties(PublicMembersFlag);
        }

        private static IEnumerable<FieldInfo> GetPublicFields(Type type)
        {
            return type.GetFields(PublicMembersFlag);
        }

        private static bool HasNonPrivateGetter(PropertyInfo propertyInfo)
        {
            MethodInfo getMethod = propertyInfo.GetGetMethod(true);
            return (getMethod != null) && !getMethod.IsPrivate && !getMethod.IsFamily;
        }

        public static MethodInfo GetMethodNamed(this Type type, string methodName)
        {
            return type.GetMethod(methodName);
        }

        internal static bool IsIndexer(this PropertyInfo member)
        {
            return (member.GetIndexParameters().Length != 0);
        }
    }
}