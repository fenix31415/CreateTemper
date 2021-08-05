using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Threading;

namespace CreateTemper
{
    public class Eager<T>
    {
        private TaskCompletionSource<T> _source;
        private Thread _t;

        public Eager(Func<T> f)
        {
            _source = new TaskCompletionSource<T>();
            _t = new Thread(() =>
            {
                try
                {
                    var result = f();
                    _source.SetResult(result);
                }
                catch (Exception e)
                {
                    _source.SetException(e);
                }
            });
            _t.Start();
        }
        public T Value => _source.Task.Result;

        public static Eager<T> Create(Func<T> f)
        {
            return new(f);
        }
    }
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "YourPatcher.esp")
                .Run(args);
        }

        // CraftingSmithingArmorTable [KYWD:000ADB78]  -- Temper Armor
        // CraftingSmithingSharpeningWheel [KYWD:00088108]  -- Temper Weapon
        // CraftingSmithingForge [KYWD:00088105] -- Recipe Armor, Weapon
        // CraftingSmithingSkyforge [KYWD:000F46CE] -- Recipe Armor (skyforge)
        private static readonly FormKey TemperArmo = FormKey.Factory("0ADB78:Skyrim.esm");
        private static readonly FormKey TemperWeap = FormKey.Factory("088108:Skyrim.esm");
        private static readonly FormKey RecipeArWe = FormKey.Factory("088105:Skyrim.esm");
        private static readonly FormKey RecipeASky = FormKey.Factory("0F46CE:Skyrim.esm");

        private static readonly FormKey[] ingredients = new FormKey[] {
            FormKey.Factory("03ADA3:Skyrim.esm"),
            FormKey.Factory("03ADA4:Skyrim.esm"),
            FormKey.Factory("05AD9D:Skyrim.esm"),
            FormKey.Factory("05AD9E:Skyrim.esm"),
            FormKey.Factory("05ADA1:Skyrim.esm"),
            FormKey.Factory("05AD9F:Skyrim.esm"),
            FormKey.Factory("05ADA0:Skyrim.esm"),
            FormKey.Factory("05ACE3:Skyrim.esm"),
            FormKey.Factory("05AD99:Skyrim.esm"),
            FormKey.Factory("05AD93:Skyrim.esm"),
            FormKey.Factory("0DB8A2:Skyrim.esm"),
            FormKey.Factory("05ACE5:Skyrim.esm"),
            FormKey.Factory("05ACE4:Skyrim.esm"),
            FormKey.Factory("0DB5D2:Skyrim.esm"),
            FormKey.Factory("0800E4:Skyrim.esm")
        };

        private static void AddTemper(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, string edid, FormKey weap, IConstructibleObjectGetter craft)
        {
            if (craft.Items == null)
            {
                Console.WriteLine("Empty craft:" + craft + ", skipin...");
                return;
            }
            int ind = craft.Items.Select(x => ingredients.IndexOf(x.Item.Item.FormKey))
                .Where(x => x != -1).DefaultIfEmpty(ingredients.Length).Min();
            if (ind == ingredients.Length)
            {
                Console.WriteLine("Strange craft:" + craft + ", skipin...");
                return;
            }

            ConstructibleObject? cobj = null;
            int suff = 0;
            while (cobj == null)
            {
                try
                {
                    cobj = state.PatchMod.ConstructibleObjects.AddNew(edid + (suff == 0 ? "" : ("_" + suff)) + "_Temper");
                }
                catch (System.Data.ConstraintException)
                {
                    Console.WriteLine($"'{edid}' is duplicate, adding suffix");
                    ++suff;
                    continue;
                }
            }
            
            var item = ingredients[ind];
            cobj.Items ??= new ExtendedList<ContainerEntry>();
            cobj.Items.Add(new ContainerEntry()
            {
                Item = new ContainerItem()
                {
                    Item = new FormLink<IItemGetter>(item),
                    Count = (craft.Items.Where(x => x.Item.Item.FormKey == item).First().Item.Count + 1) / 2
                }
            });

            cobj.CreatedObjectCount = 1;
            cobj.CreatedObject.SetTo(weap);
            cobj.WorkbenchKeyword.SetTo(TemperWeap);
        }
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var mod = ModKey.FromNameAndExtension("WeapArmorTest.esp");
            if (!state.LoadOrder.TryGetValue(mod, out var listing) || listing.Mod == null)
            {
                Console.WriteLine("Mod not found, skipin...");
                return;
            }

            var _armorWeaponRecipes = new Eager<List<(IConstructibleObjectGetter Cobj, IConstructibleGetter? Constructable)>>(() =>
            {
                return listing.Mod.ToImmutableLinkCache().PriorityOrder.WinningOverrides<IConstructibleObjectGetter>()
                    .AsParallel()
                    .Select(p => (p, p.CreatedObject.TryResolve(state.LinkCache)))
                    .Where(p => p.Item2 != null)
                    .ToList();
            });
            foreach (var (cobj, resolved) in _armorWeaponRecipes.Value)
            {
                var edid = cobj.EditorID;
                if (edid == null)
                {
                    Console.WriteLine("empty edid: " + cobj + ", skipin...");
                    continue;
                }
                if (resolved == null)
                {
                    Console.WriteLine("empty created object: " + cobj + ", skipin...");
                    continue;
                }

                edid = edid[0..^7];
                if (resolved is IWeaponGetter w)
                {
                    AddTemper(state, edid, w.FormKey, cobj);
                }
                else if (resolved is IArmorGetter a)
                {
                    AddTemper(state, edid, a.FormKey, cobj);
                }
            }
        }
    }
}
