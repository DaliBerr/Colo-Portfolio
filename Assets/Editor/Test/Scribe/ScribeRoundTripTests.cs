using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lonize.Scribe;
using NUnit.Framework;

namespace Colo.Portfolio.Tests
{
    /// <summary>
    /// 保存 → 写成 JSON → 读回，检查对象、引用和多态条目都能还原。
    /// 每个用例都走真实的流和 JSON 文本，而不是停留在内存里的中间结构。
    /// </summary>
    public sealed class ScribeRoundTripTests
    {
        public enum Facing { North, East, South, West }

        // —— 测试用的存档对象：每个类型只写一个 ExposeData，保存和读取共用 ——

        public sealed class Machine : IExposable, ILoadReferenceable
        {
            public string Id;
            public string DefName;
            public int Level;
            public float Progress;
            public bool Powered;
            public long BuiltAtTick;
            public Facing Facing;
            public Machine OutputTarget;   // 引用：只存对方的 Id，读完后统一回填

            public string GetSaveId() => Id;

            public void ExposeData()
            {
                Scribe_Refs.Scribe_Meta.HandleSaveId(this, "id", ref Id);
                Scribe_Values.Look("def", ref DefName);
                Scribe_Values.Look("level", ref Level, 1);
                Scribe_Values.Look("progress", ref Progress);
                Scribe_Values.Look("powered", ref Powered);
                Scribe_Values.Look("builtAt", ref BuiltAtTick);
                Scribe_Values.LookEnum("facing", ref Facing);
                Scribe_Refs.Look("output", () => OutputTarget, v => OutputTarget = v);
            }
        }

        public sealed class Factory : IExposable
        {
            public string Name;
            public List<Machine> Machines = new();
            public List<Machine> PoweredOrder = new();  // 引用列表，指向 Machines 里的对象
            public List<int> Tags = new();
            public List<string> Notes = new();

            public void ExposeData()
            {
                Scribe_Values.Look("name", ref Name);
                Scribe_Collections.LookDeep("machines", ref Machines);
                Scribe_Refs.LookList("poweredOrder", ref PoweredOrder);
                Scribe_Collections.Look("tags", ref Tags);
                Scribe_Collections.Look("notes", ref Notes);
            }
        }

        public sealed class World : IExposable
        {
            public int Seed;
            public Factory Main;

            public void ExposeData()
            {
                Scribe_Values.Look("seed", ref Seed);
                Scribe_Deep.Look("main", ref Main);
            }
        }

        // —— 多态条目：存档根是一串不同类型的条目，读档时按 TypeId 白名单创建 ——

        public sealed class SaveCounter : ISaveItem
        {
            public string TypeId => "Counter";
            public int Value;
            public void ExposeData() => Scribe_Values.Look("value", ref Value);
        }

        public sealed class SaveWorld : ISaveItem
        {
            public string TypeId => "World";
            public World World;
            public void ExposeData() => Scribe_Deep.Look("world", ref World);
        }

        public sealed class SaveRoot : IExposable
        {
            public List<ISaveItem> Items = new();
            public void ExposeData() => Scribe_Polymorph.LookList("items", ref Items);
        }

        [SetUp]
        public void SetUp()
        {
            Scribe.Abort(); // Scribe 是静态上下文，先确保上一个用例没有残留
            ScribeBootstrap.InitializeDefaults();
            PolymorphRegistry.Register<SaveCounter>("Counter");
            PolymorphRegistry.Register<SaveWorld>("World");
        }

        [TearDown]
        public void TearDown() => Scribe.Abort();

        private static string Save<T>(T root, int version = 3) where T : class, IExposable, new()
        {
            using var stream = new MemoryStream();
            Scribe.InitSaving(stream, version);
            ScribeRefs.Clear();
            Scribe.Look(ref root);
            Scribe.FinalizeWriting();
            // 存档文件以 UTF-8 BOM 开头；读档器会自己去掉，这里为了让测试能用 JObject 改写文本，先去掉。
            return Encoding.UTF8.GetString(stream.ToArray()).TrimStart('﻿');
        }

        private static T Load<T>(string json) where T : class, IExposable, new()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Scribe.InitLoading(stream);
            ScribeRefs.Clear();
            T loaded = null;
            Scribe.Look(ref loaded);
            ScribeRefs.ResolveAll();
            Scribe.FinalizeLoading();
            return loaded;
        }

        private static World BuildWorld()
        {
            var smelter = new Machine { Id = "m1", DefName = "Smelter", Level = 3, Progress = 0.625f, Powered = true, BuiltAtTick = 5_000_000_000L, Facing = Facing.West };
            var belt = new Machine { Id = "m2", DefName = "Belt", Level = 1, Progress = 0f, Powered = false, Facing = Facing.South };
            smelter.OutputTarget = belt;
            belt.OutputTarget = smelter; // 互相引用：成环也能存
            return new World
            {
                Seed = 20251225,
                Main = new Factory
                {
                    Name = "一号工厂",
                    Machines = { smelter, belt },
                    PoweredOrder = { belt, smelter },
                    Tags = { 7, 42 },
                    Notes = { "北侧扩建", "" },
                },
            };
        }

        [Test]
        public void Values_RoundTripThroughJsonText()
        {
            var loaded = Load<World>(Save(BuildWorld()));

            Assert.That(loaded.Seed, Is.EqualTo(20251225));
            var m = loaded.Main.Machines[0];
            Assert.That(m.DefName, Is.EqualTo("Smelter"));
            Assert.That(m.Level, Is.EqualTo(3));
            Assert.That(m.Progress, Is.EqualTo(0.625f));
            Assert.That(m.Powered, Is.True);
            Assert.That(m.BuiltAtTick, Is.EqualTo(5_000_000_000L), "long 超出 int 范围也要原样还原。");
            Assert.That(m.Facing, Is.EqualTo(Facing.West));
            Assert.That(loaded.Main.Name, Is.EqualTo("一号工厂"));
            Assert.That(loaded.Main.Tags, Is.EqualTo(new[] { 7, 42 }));
            Assert.That(loaded.Main.Notes, Is.EqualTo(new[] { "北侧扩建", "" }));
        }

        [Test]
        public void References_ResolveToTheSameLoadedObjects_EvenWhenCyclic()
        {
            var loaded = Load<World>(Save(BuildWorld()));
            var smelter = loaded.Main.Machines[0];
            var belt = loaded.Main.Machines[1];

            Assert.That(smelter.OutputTarget, Is.SameAs(belt), "引用应指向读回的那个对象，而不是新建一份副本。");
            Assert.That(belt.OutputTarget, Is.SameAs(smelter));
            Assert.That(loaded.Main.PoweredOrder[0], Is.SameAs(belt));
            Assert.That(loaded.Main.PoweredOrder[1], Is.SameAs(smelter));
        }

        [Test]
        public void MissingFields_FallBackToDefaults_SoOldSavesStillLoad()
        {
            // 模拟旧版本存档：机器节点里只有 id 和 def，没有后来才加的字段。
            var json = Save(BuildWorld());
            var stripped = Newtonsoft.Json.Linq.JObject.Parse(json);
            var machineFields = stripped.SelectToken("Root.Fields.__root.Node.Fields.root.Node.Fields.main.Node.Fields.machines.Value[0].Fields");
            Assert.That(machineFields, Is.Not.Null, "存档结构与预期不符，测试需要更新路径。");
            foreach (var key in new[] { "level", "progress", "powered", "builtAt", "facing", "output" })
                ((Newtonsoft.Json.Linq.JObject)machineFields).Remove(key);

            var loaded = Load<World>(stripped.ToString());
            var m = loaded.Main.Machines[0];
            Assert.That(m.DefName, Is.EqualTo("Smelter"));
            Assert.That(m.Level, Is.EqualTo(1), "缺字段时用 Look 里声明的默认值。");
            Assert.That(m.Progress, Is.EqualTo(0f));
            Assert.That(m.Facing, Is.EqualTo(Facing.North));
            Assert.That(m.OutputTarget, Is.Null);
        }

        [Test]
        public void PolymorphicItems_AreCreatedFromWhitelist_UnknownTypesBecomeNull()
        {
            var root = new SaveRoot { Items = { new SaveCounter { Value = 9 }, new SaveWorld { World = BuildWorld() } } };
            var json = Save(root);

            var loaded = Load<SaveRoot>(json);
            Assert.That(loaded.Items[0], Is.TypeOf<SaveCounter>());
            Assert.That(((SaveCounter)loaded.Items[0]).Value, Is.EqualTo(9));
            var world = ((SaveWorld)loaded.Items[1]).World;
            Assert.That(world.Main.Machines[0].OutputTarget, Is.SameAs(world.Main.Machines[1]));

            // 存档里写了一个没注册的类型：不做反射构造，读成 null，由上层判断存档是否损坏。
            var tampered = json.Replace("\"Counter\"", "\"System.IO.FileInfo\"");
            var reloaded = Load<SaveRoot>(tampered);
            Assert.That(reloaded.Items[0], Is.Null);
            Assert.That(reloaded.Items[1], Is.TypeOf<SaveWorld>());
        }

        [Test]
        public void FileVersion_IsWrittenAndReadBack()
        {
            var json = Save(new SaveRoot(), version: 7);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Scribe.InitLoading(stream);
            Assert.That(Scribe.FileVersion, Is.EqualTo(7));
            Scribe.FinalizeLoading();
        }

        [Test]
        public void NonJsonData_IsRejected_AndScribeReturnsToInactive()
        {
            using var stream = new MemoryStream(new byte[] { 0x54, 0x4C, 0x56, 0x01, 0x00 }); // 旧二进制 TLV 存档的开头
            Assert.Throws<InvalidDataException>(() => Scribe.InitLoading(stream));
            Assert.That(Scribe.mode, Is.EqualTo(ScribeMode.Inactive), "读取失败后要能立即开始下一次保存或读取。");
        }

        [Test]
        public void Abort_AfterFailedSave_WritesNothing_AndAllowsNextSave()
        {
            using var stream = new MemoryStream();
            Scribe.InitSaving(stream, 3);
            var root = new FailingRoot();
            Assert.Throws<InvalidOperationException>(() => Scribe.Look(ref root));
            Scribe.Abort();

            Assert.That(stream.Length, Is.EqualTo(0), "失败的保存不能把半份文档写进流里。");
            Assert.That(Scribe.mode, Is.EqualTo(ScribeMode.Inactive));
            Assert.DoesNotThrow(() => Save(new SaveRoot()));
        }

        public sealed class FailingRoot : IExposable
        {
            public void ExposeData()
            {
                int ok = 1;
                Scribe_Values.Look("ok", ref ok);
                throw new InvalidOperationException("模拟某个系统在保存途中出错");
            }
        }

        public sealed class KeyBindings : IExposable
        {
            public Dictionary<string, Facing> ByValues = new();
            public Dictionary<string, Facing> ByCodec = new();

            public void ExposeData()
            {
                Scribe_Values.LookDictStrEnumInt32("byValues", ref ByValues);
                Scribe.Scribe_Generic.Look("byCodec", ref ByCodec);
            }
        }

        /// <summary>
        /// 回归用例：游戏用 LookDictStrEnumInt32 保存按键绑定。原实现读档时只认内存里的 Dictionary，
        /// 不认 JSON 读回的 JObject，结果整张表退回默认值。作品集整理时发现并修正。
        /// </summary>
        [Test]
        public void EnumDictionary_RoundTripsThroughBothApis()
        {
            CodecRegistry.Register(new DictStrEnumInt32Codec<Facing>());
            var saved = new KeyBindings
            {
                ByValues = { ["rotate"] = Facing.East, ["flip"] = Facing.West },
                ByCodec = { ["rotate"] = Facing.South },
            };

            var loaded = Load<KeyBindings>(Save(saved));

            Assert.That(loaded.ByValues, Is.EquivalentTo(saved.ByValues));
            Assert.That(loaded.ByCodec, Is.EquivalentTo(saved.ByCodec));
        }

        [Test]
        public void GenericLook_RejectsReferenceAndDeepTypes()
        {
            using var stream = new MemoryStream();
            Scribe.InitSaving(stream, 3);
            var machine = new Machine { Id = "x" };
            Assert.Throws<InvalidOperationException>(() => Scribe.Scribe_Generic.Look("m", ref machine),
                "引用类和可展开对象必须走 Scribe_Refs / Scribe_Deep，避免被当成值复制一份。");
        }
    }
}
