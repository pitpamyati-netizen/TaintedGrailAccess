using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TaintedGrailAccess.Runtime;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>
    /// Bounded metadata inspection. Reads only loaded assemblies, never loads another
    /// game DLL; invokes no game getter/method and serializes no runtime object values.
    /// </summary>
    internal static class SignatureInventory
    {
        private const int MaxAssemblies = 2;
        private const int MaxTypes = 12000;
        private const int MaxMembersPerType = 120;
        private const int MaxCharacters = 4 * 1024 * 1024;
        private static readonly HashSet<string> AllowedAssemblies =
            new HashSet<string>(StringComparer.Ordinal) { "TG.Main", "Assembly-CSharp" };

        public static void Write(string path, IRuntimeLog log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var text = new StringBuilder();
            text.AppendLine("Tainted Grail Access: local diagnostic signature inventory");
            text.AppendLine("UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            text.AppendLine("Metadata only. No method bodies, field values, getters, scene objects or save data.");
            text.AppendLine("Bounds: 2 allowlisted assemblies; 12000 types; 120 members per type; 4 Mi characters.");
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => AllowedAssemblies.Contains(a.GetName().Name))
                .OrderBy(a => a.GetName().Name, StringComparer.Ordinal).Take(MaxAssemblies).ToArray();
            var typeCount = 0;
            var truncated = false;
            foreach (var assembly in assemblies)
            {
                text.AppendLine("ASSEMBLY " + assembly.GetName().FullName);
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types.Where(t => t != null).ToArray();
                    text.AppendLine("NOTE partial type load; missing types are omitted.");
                }
                foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    if (typeCount >= MaxTypes || text.Length >= MaxCharacters) { truncated = true; break; }
                    typeCount++;
                    text.AppendLine("TYPE " + type.FullName);
                    try
                    {
                        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                        var members = type.GetMembers(flags).OrderBy(m => m.MemberType)
                            .ThenBy(m => m.Name, StringComparer.Ordinal).ToArray();
                        foreach (var member in members.Take(MaxMembersPerType))
                        {
                            if (text.Length >= MaxCharacters) { truncated = true; break; }
                            var signature = Describe(member);
                            if (signature.Length > 2000) signature = signature.Substring(0, 2000) + " [signature truncated]";
                            text.Append("  ").AppendLine(signature);
                        }
                        if (members.Length > MaxMembersPerType) text.AppendLine("  [member limit reached]");
                    }
                    catch (Exception exception) { text.AppendLine("  [metadata error: " + exception.GetType().Name + "]"); }
                }
                if (truncated) break;
            }
            if (assemblies.Length == 0) text.AppendLine("No allowlisted game assembly is loaded. Verify backend and actual game assembly names locally.");
            if (truncated) text.AppendLine("[inventory limit reached; this is not an exhaustive catalog]");
            // Bounded final serialization; no unbounded dump even with a pathological type name.
            if (text.Length > MaxCharacters) text.Length = MaxCharacters;
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            log.Write("diagnostics.inventory", "Wrote metadata for " + assemblies.Length + " assemblies, " + typeCount + " types to " + path);
        }

        private static string Describe(MemberInfo member)
        {
            if (member is MethodBase method)
            {
                var result = method is MethodInfo info ? TypeName(info.ReturnType) + " " : "";
                return (method.IsStatic ? "static " : "instance ") + result + method.Name + "(" +
                    string.Join(", ", method.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name)) + ")";
            }
            if (member is FieldInfo field) return "field " + TypeName(field.FieldType) + " " + field.Name;
            if (member is PropertyInfo property) return "property " + TypeName(property.PropertyType) + " " + property.Name;
            if (member is EventInfo eventInfo) return "event " + TypeName(eventInfo.EventHandlerType) + " " + eventInfo.Name;
            return member.MemberType + " " + member.Name;
        }
        private static string TypeName(Type type) => type == null ? "?" : type.FullName ?? type.Name;
    }
}
