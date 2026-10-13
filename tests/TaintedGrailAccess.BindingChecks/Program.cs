using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using TaintedGrailAccess.Runtime;

// Optional metadata check with explicit local paths. No Unity object creation,
// game start, Harmony installation, native input or UI actions.
if (args.Length != 3)
{
    Console.Error.WriteLine("Укажите три каталога: Managed игры, собранный пакет мода, BepInEx/core.");
    return 2;
}
try
{
    var directories = args.Select(Path.GetFullPath).ToArray();
    if (directories.Any(path => !Directory.Exists(path))) throw new DirectoryNotFoundException("Каталог проверки отсутствует.");
    AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
        }
        return null;
    };
    var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directories[0], "TG.Main.dll"));
    var host = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directories[1], "TaintedGrailAccess.BepInEx.dll"));
    var bindings = host.GetType("TaintedGrailAccess.BepInExHost.TitleUiBindings", true)!;
    var constructor = bindings.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
    var instance = constructor.Invoke(new object[] { game, new Localizer(new Dictionary<string, string>()), new NullLog(), null! });
    // The victory summary's VCurrenciesUI shows these eight entries from Realm.Currencies.
    // Reading a shown sprite must resolve its StatType.DisplayName rather than its abbreviation.
    // This checks metadata coverage only; translation and speech still require the running game.
    var currencyStats = game.GetType("Awaken.TG.Main.Realms.Realm+Stats", true)!;
    var mappedSprites = (FieldInfo[])bindings.GetField("_combatSpriteFields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    var missingCurrencies = new[] { "Wealth", "WyrdShards", "RunestoneDust", "Blood", "Tallow", "CardShreds", "GoldenTokens", "JournalPages" }
        .Where(name => !mappedSprites.Contains(currencyStats.GetField(name, BindingFlags.Public | BindingFlags.Static)!)).ToArray();
    if (missingCurrencies.Length != 0) throw new InvalidOperationException("Не названы значки ресурсов результата боя: " + string.Join(", ", missingCurrencies));
    Console.WriteLine("Все восемь значков ресурсов результата боя связаны с игровыми названиями.");
    // Every real class uses RuneUI/VRuneSkill. Empty is a placeholder, not a tenth class.
    // Check names without running static initializers or unlocking the user's classes.
    var runeNames = game.GetType("Awaken.TG.Main.Classes.RuneUnlocking+RuneName", true)!;
    var expectedClasses = new[] { "Archer", "Sharpshooter", "Flipper", "Wyrdhunter", "Pathfinder", "Berserk", "Summoner", "BloodMage", "Necromancer" };
    var actualClasses = runeNames.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(field => field.FieldType == runeNames && field.Name != "Empty").Select(field => field.Name).OrderBy(name => name).ToArray();
    if (!actualClasses.SequenceEqual(expectedClasses.OrderBy(name => name))) throw new InvalidOperationException("Перечень классов отличается от исследованных девяти вариантов.");
    Console.WriteLine("Подтверждены девять классов и общие привязки ultimate, порогов, уровней прислужников и просмотра карт.");
    var movementKeys = game.GetType("Awaken.TG.Main.Heroes.VHeroKeys", true)!;
    foreach (var name in new[] { "Horizontal", "Vertical", "CameraRotation" })
    {
        var getter = movementKeys.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetGetMethod(true)!;
        if (getter.ReturnType != typeof(float) || getter.GetParameters().Length != 0) throw new InvalidOperationException("Изменился прямой путь ввода движения.");
    }
    // Check the compiled adapter, not just the portable fake screen. A generic path estimate
    // uses different constraints from Hero.Seeker and must never block the original action.
    var movementAction = bindings.GetMethod("MoveToLocation", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var estimate = bindings.GetMethod("InspectRoute", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var pending = new Stack<MethodBase>(new[] { movementAction }); var inspected = new HashSet<MethodBase>();
    while (pending.TryPop(out var method))
    {
        if (!inspected.Add(method)) continue;
        foreach (var called in CalledMethods(method))
        {
            if (called == estimate) throw new InvalidOperationException("Действие подхода вызывает предварительную проверку пути вместо решения игры.");
            if (called.Module == host.ManifestModule) pending.Push(called);
        }
    }
    Console.WriteLine("Собранное действие подхода не вызывает предварительную проверку пути; решение остаётся игре.");
    Console.WriteLine("Точные привязки M4 и три прямых пути опроса движения найдены; исходные действия не вызывались.");
    foreach (var name in new[] { "TG.Main", "Rewired_Core", "UnityEngine.UI", "Unity.TextMeshPro", "AstarPathfindingProject" })
    {
        var assembly = Assembly.Load(name);
        Console.WriteLine(name + " MVID=" + assembly.ManifestModule.ModuleVersionId);
    }
    Console.WriteLine("Точные типы, поля и сигнатуры меню, первого пути и боя M3 найдены. Игровые действия и ввод не проверялись.");
    return 0;
}
catch (Exception error)
{
    var cause = error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error;
    Console.Error.WriteLine(cause.GetType().Name + ": " + cause.Message);
    return 1;
}

static IEnumerable<MethodBase> CalledMethods(MethodBase method)
{
    var bytes = method.GetMethodBody()?.GetILAsByteArray();
    if (bytes == null) yield break;
    var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => unchecked((ushort)opcode.Value));
    for (int offset = 0; offset < bytes.Length;)
    {
        ushort value = bytes[offset++];
        if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
        var opcode = opcodes[value];
        if (opcode.OperandType == OperandType.InlineMethod)
            yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)!;
        offset += opcode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
            _ => 4
        };
    }
}
