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

        /// <summary>Input bindings also require the inspected toolkit and input dependencies.</summary>
        public static void RequireInputAssemblies()
        {
            RequireHash("Rewired_Core", "cf3c91bed960e0b45e67dc67fb6df6eca7d05a37b803c788bbf2c151d1bde842", "d3f4044c-7648-45c9-b50d-8d7e6d871796");
            RequireHash("UnityEngine.UI", "69aea3e824546d94063dd22ee998125847d65171ab4a210b3f08ef392aabfc97", "d8562c56-a489-4d6f-bb2b-224105bd17d4");
            RequireHash("Unity.TextMeshPro", "b892fcec2b444d6c2623ba456319e71697e9387bd04bee993843f5e26c8f9db9", "5ec98d0f-fd50-4ec7-ac26-58db85bcd6ab");
        }
        private static void RequireHash(string name, string expected, string mvid)
        {
            var assembly = Assembly.Load(name);
            if (HashFile(assembly.Location) != expected || assembly.ManifestModule.ModuleVersionId != new Guid(mvid))
                throw new NotSupportedException(name + " fingerprint differs; title input remains disabled.");
        }
        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
