using System.Reflection;
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
    constructor.Invoke(new object[] { game, new Localizer(new Dictionary<string, string>()), new NullLog(), null! });
    foreach (var name in new[] { "TG.Main", "Rewired_Core", "UnityEngine.UI", "Unity.TextMeshPro" })
    {
        var assembly = Assembly.Load(name);
        Console.WriteLine(name + " MVID=" + assembly.ManifestModule.ModuleVersionId);
    }
    Console.WriteLine("Точные типы и поля стартовых привязок найдены. Игровые действия и ввод не проверялись.");
    return 0;
}
catch (Exception error)
{
    var cause = error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error;
    Console.Error.WriteLine(cause.GetType().Name + ": " + cause.Message);
    return 1;
}
