namespace RuntimeTypes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.Serialization;
    using System.Security.Cryptography;
    using System.Text;

    // # On naming new types.
    //
    // Types need to be given a name and minimally it should be unique in the context of
    // namespace it is in. This can be achieved either via some form of identity generator,
    // which guarantees uniqueness in one way or another. For example:
    //
    // ```
    // static int id = 0;
    // public static string MakeName()
    // {
    //     return "MyType_" + System.Threading.Interlocked.Increment(ref id);
    // }
    // ```
    //
    // This approach though sufficient for uniquiness, may not be suitable for the case
    // when types are reused in multiple contexts, need to be reused when have same
    // structure, or have to be created in more deterministic way.
    //
    // Alternative approach is to generate unique name of the type based on field names
    // and field types in the order they are given. This way same type definitions reuse
    // same runtype types across all create type requests for matching definitions.
    // The drawback, however, is that derived unique name is very likely to be too long
    // to be used directly as type iderntifier, and thus must be shortened in a way that
    // reduces a chance for collisions, which should be handled appropriately when occur.
    //
    // RuntimeTypeBuilder uses the later approach hashing with SHA256, and throws
    // InvalidOperationException in case of type hash collision.
    public static class RuntimeTypeBuilder
    {
        private const string DefaultFieldNamePrefix = "Item";
        private const string DefaultTypeNamePrefix = "Dyn";
        private static AssemblyName AssemblyName;
        private static ModuleBuilder ModuleBuilder;
        private static SHA256 HashAlgorithm;

        static RuntimeTypeBuilder()
        {
            AssemblyName = new AssemblyName("__DynamicTypes__");
            ModuleBuilder = AppDomain.CurrentDomain
                .DefineDynamicAssembly(AssemblyName, AssemblyBuilderAccess.Run)
                .DefineDynamicModule(AssemblyName.Name);
            HashAlgorithm = SHA256.Create();
        }

        static string MakeUniqueName(IDictionary<string, Type> fields)
        {
            return string.Join(
                "",
                Enumerable.Concat(
                    fields.Keys,
                    fields.Values.Select(e => e.AssemblyQualifiedName)));
        }

        /// <summary>
        /// Produce new dynamic tuple-like type based on the sequence of field types.
        /// When possible use Tuple<...> instead. Generated types are needed when:
        /// 1. There are special requirements on the generated type; or
        /// 2. there are simply more than 8 items in the tuple.
        /// </summary>
        /// <param name="fieldTypes">Sequence of field types.</param>
        /// <returns>Returns generated type.</returns>
        public static Type MakeDynamicType(ICollection<Type> fieldTypes)
        {
            if (fieldTypes == null)
            {
                throw new ArgumentNullException("fieldTypes");
            }

            if (fieldTypes.Count == 0)
            {
                throw new ArgumentOutOfRangeException("fieldTypes", "fieldTypes must have at least 1 field definition");
            }

            return MakeDynamicType(fieldTypes
                    .Select((t, i) => new { name = DefaultFieldNamePrefix + i, type = t })
                    .ToDictionary(e => e.name, e => e.type));
        }

        public static Type MakeDynamicType(IDictionary<string, Type> fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException("fields");
            }

            if (fields.Count == 0)
            {
                throw new ArgumentOutOfRangeException("fields", "fields must have at least 1 field definition");
            }

            if (fields.Keys.Any(name => string.IsNullOrEmpty(name)))
            {
                throw new ArgumentException("Field name must not be null or empty.", "fields");
            }

            string className = DefaultTypeNamePrefix +
                string.Join("", HashAlgorithm
                    .ComputeHash(Encoding.UTF8.GetBytes(MakeUniqueName(fields)))
                    .Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

            // Check if type with this name has already been created.
            var existingType = ModuleBuilder.GetType(className, false, false);
            if (existingType != null)
            {
                // Make sure we don't have name collision. i.e. the type definition of the
                // new type matches definition of the existing type.
                var newUniqueName = MakeUniqueName(fields);
                var existingUniqueName = MakeUniqueName(existingType
                    .GetFields()
                    .ToDictionary(e => e.Name, e => e.FieldType));
                if (string.Equals(newUniqueName, existingUniqueName, StringComparison.Ordinal))
                {
                    return existingType;
                }
                else
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Type name hash collision in RuntimeTypeBuilder for type definitions '{0}' and '{1}'.",
                            existingUniqueName,
                            newUniqueName));
                }
            }

            var typeAttributes = TypeAttributes.Public
                | TypeAttributes.AutoLayout
                | TypeAttributes.Sealed
                | TypeAttributes.Serializable;
            TypeBuilder typeBuilder = ModuleBuilder.DefineType(
                className, typeAttributes, typeof(ValueType));

            // Add  [DataContract] attribute.
            typeBuilder.SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(DataContractAttribute).GetConstructor(Type.EmptyTypes),
                    new object[0]));

            foreach (var field in fields)
            {
                var fieldBuilder = typeBuilder.DefineField(
                    field.Key, field.Value, FieldAttributes.Public);

                // Add [DataMember] attribute.
                fieldBuilder.SetCustomAttribute(
                    new CustomAttributeBuilder(
                        typeof(DataMemberAttribute).GetConstructor(Type.EmptyTypes),
                        new object[0]));
            }

            return typeBuilder.CreateType();
        }
    }
}
