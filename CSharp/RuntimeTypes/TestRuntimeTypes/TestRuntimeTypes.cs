namespace TestRuntimeTypes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RuntimeTypes;

    [TestClass]
    public class TestRuntimeTypes
    {
        [TestMethod]
        public void TestLargeTuple()
        {
            var tupleType = RuntimeTypeBuilder.MakeDynamicType(
                Enumerable.Range(1, 50).Select(_ => typeof(int)).ToArray());

        }

        [TestMethod]
        public void NestedTuples()
        {
            var inner3 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"id3", typeof(int) },
                    {"names3", typeof(List<string>) },
                });

            var inner2 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"id2", typeof(long) },
                    {"nested2", inner3 },
                });

            var inner1 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"id1", typeof(int?) },
                    {"nested1", inner2 },
                    {"alsonested1", inner3 },
                });

            var outer = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"verynested", inner1 },
                    {"nested", inner2 },
                    {"lessnested", inner3 },
                });

            Assert.IsNotNull(Activator.CreateInstance(outer), "Can create instance of nested type.");

        }

        [TestMethod]
        public void TestTypeReuse()
        {
            var type1 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"foo", typeof(List<string>) },
                    {"bar", typeof(int) },
                    {"baz", typeof(Tuple<int, int?, string>) },
                });

            var type2 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"foo", typeof(List<string>) },
                    {"bar", typeof(int) },
                    {"baz", typeof(Tuple<int, int?, string>) },
                });

            Assert.AreSame(type1, type2, "Same type definition must result in same types.");
        }

        [TestMethod]
        public void DifferentFieldOrderMatters()
        {
            var type1 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"foo", typeof(List<string>) },
                    {"bar", typeof(int) },
                    {"baz", typeof(Tuple<int, int?, string>) },
                });

            var type2 = RuntimeTypeBuilder.MakeDynamicType(new Dictionary<string, Type>
                {
                    {"foo", typeof(List<string>) },
                    {"baz", typeof(Tuple<int, int?, string>) },
                    {"bar", typeof(int) },
                });

            Assert.AreNotSame(type1, type2, "Types with different order of fields must be different.");
        }

        [TestMethod]
        public void CanCreateThousandsOfTypes()
        {
            var howManyTypesToCreate = 10000;

            Func<int, Dictionary<string, Type>> fnTypeDef =
                i => new Dictionary<string, Type> { { "type" + i + "field", typeof(int) } };

            // Create first type to get assembly in which they are created.
            var type0 = RuntimeTypeBuilder.MakeDynamicType(fnTypeDef(0));

            var weHaveTypes = type0.Assembly.GetTypes().Length;

            // create 999 more types
            for (int i = 1; i < howManyTypesToCreate; i++)
            {
                RuntimeTypeBuilder.MakeDynamicType(fnTypeDef(i));
            }

            var nowHaveTypes = type0.Assembly.GetTypes().Length;

            Assert.AreEqual(nowHaveTypes, weHaveTypes + howManyTypesToCreate - 1,
                "There are " + (howManyTypesToCreate - 1) + " more types now.");
        }
    }
}
