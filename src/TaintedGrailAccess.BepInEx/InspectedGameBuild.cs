using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace TaintedGrailAccess.BepInExHost
{
    /// <summary>Limits all researched game observers to the exact locally inspected build.</summary>
    internal static class InspectedGameBuild
    {
        private const string GameHash = "26c0f468bc6cc2e28380dd8b81dda23c90b316c2b9b128c306599810878af4ca";
        private static readonly Guid GameMvid = new Guid("78793877-83a2-4bfc-970d-974256f05d98");

        public static Assembly RequireAssembly()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "TG.Main");
            if (Application.version != "1.61" || Application.unityVersion != "2020.3.23f1" ||
                assembly.ManifestModule.ModuleVersionId != GameMvid || HashFile(assembly.Location) != GameHash)
                throw new NotSupportedException("Game fingerprint differs; researched observers remain disabled.");
            return assembly;
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
