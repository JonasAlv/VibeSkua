using Skua.Core.Models;
using System.Diagnostics;

namespace Skua.Core.Scripts;

/// <summary>
/// Keeps Scripts/CoreAdvanced.cs patched with the per-class enhance profile support used by the
/// CoreBots Options "Class Enhance" tab (ClassEnh[class] in the CBO file and its Enhance button).
/// Updating the scripts downloads an unpatched CoreAdvanced.cs again, so this runs before every script compile.
/// Mirrors patches/apply_coreadvanced_patch.py.
/// </summary>
public static class CoreAdvancedPatcher
{
    private const string PatchedMarker = "public void EnhanceEquippedWithProfile(string profile)";
    private const string LockName = "SkuaZZCoreAdvancedPatchLock";

    private static readonly object _lock = new();
    private static DateTime _lastCheckedWrite = DateTime.MinValue;

    public static string CoreAdvancedPath => Path.Combine(ClientFileSources.SkuaScriptsDIR, "CoreAdvanced.cs");

    /// <summary>
    /// Applies the patch when CoreAdvanced.cs exists and does not have it yet.
    /// </summary>
    /// <returns><see langword="true"/> if the file is patched (or did not need it), <see langword="false"/> if it could not be patched.</returns>
    public static bool EnsurePatched()
    {
        string path = CoreAdvancedPath;
        lock (_lock)
        {
            try
            {
                if (!File.Exists(path))
                    return true;

                DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                if (lastWrite == _lastCheckedWrite)
                    return true;

                // Several clients may start scripts at the same time.
                using Mutex mutex = new(false, LockName);
                bool locked = false;
                try
                {
                    try { locked = mutex.WaitOne(5000); } catch (AbandonedMutexException) { locked = true; }

                    string content = File.ReadAllText(path);
                    if (!content.Contains(PatchedMarker))
                    {
                        string? patched = Apply(content);
                        if (patched == null)
                        {
                            Trace.WriteLine("CoreAdvancedPatcher: anchors not found in CoreAdvanced.cs, class enhance profiles are unavailable.");
                            return false;
                        }

                        string temp = path + ".patching";
                        File.WriteAllText(temp, patched);
                        File.Move(temp, path, true);
                    }

                    _lastCheckedWrite = File.GetLastWriteTimeUtc(path);
                    return true;
                }
                finally
                {
                    if (locked)
                    {
                        try { mutex.ReleaseMutex(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"CoreAdvancedPatcher: {ex.Message}");
                return false;
            }
        }
    }

    /// <returns>The patched content, or null if CoreAdvanced.cs changed and the anchors are gone.</returns>
    public static string? Apply(string content)
    {
        // The snippets use LF; match the file's line endings.
        string nl = content.Contains("\r\n") ? "\r\n" : "\n";
        string Fix(string snippet) => snippet.Replace("\r\n", "\n").Replace("\n", nl);

        string oldCall = Fix(OldCall), anchor = Fix(Anchor), oldEnhance = Fix(OldEnhance);
        if (Count(content, oldCall) != 1 || Count(content, anchor) != 1 || Count(content, oldEnhance) != 1)
            return null;

        return content
            .Replace(oldCall, Fix(NewCall))
            .Replace(oldEnhance, Fix(NewEnhance))
            .Replace(anchor, Fix(ProfileMethods) + anchor);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private const string OldCall =
        "        // If the item doesnt exist in the forge enh lib, or the player doesn't have the Forge enh unlocked, use Awe enh instead\n" +
        "        if (!ForgeEnhancementLibrary())\n" +
        "            AweEnhancementLibrary();\n";

    private const string NewCall =
        "        // A custom profile from the CoreBots Options \"Class Enhance\" tab replaces the built-in library\n" +
        "        string? classProfile = ClassEnhanceProfile(className, ref type, ref cSpecial, ref hSpecial, ref wSpecial);\n" +
        "        if (classProfile == null)\n" +
        "        {\n" +
        "            // If the item doesnt exist in the forge enh lib, or the player doesn't have the Forge enh unlocked, use Awe enh instead\n" +
        "            if (!ForgeEnhancementLibrary())\n" +
        "                AweEnhancementLibrary();\n" +
        "        }\n";

    private const string Anchor =
        "    public void SmartEnhance(string? className, bool ForceEnh = false)\n";

    private const string OldEnhance =
        "        EnhanceEquipped((EnhancementType)type, cSpecial, hSpecial, wSpecial);\n";

    private const string NewEnhance =
        "        if (classProfile != null)\n" +
        "            EnhanceEquippedWithProfile(classProfile);\n" +
        "        else\n" +
        "            EnhanceEquipped((EnhancementType)type, cSpecial, hSpecial, wSpecial);\n";

    private const string ProfileMethods =
        "    /// <summary>\n" +
        "    /// Resolves a \"Base|WeaponForge|WeaponAwe|Cape|Helm\" profile (CoreBots Options \"Class Enhance\" tab).\n" +
        "    /// Uses only the chosen specials: weapon takes the Forge one if unlocked, else the Awe one if unlocked;\n" +
        "    /// any special that isn't unlocked becomes None (base enhancement only). \"Keep\" leaves that item untouched.\n" +
        "    /// </summary>\n" +
        "    public bool ResolveEnhanceProfile(string profile, out EnhancementType type, out CapeSpecial cape, out HelmSpecial helm, out WeaponSpecial weapon)\n" +
        "    {\n" +
        "        cape = CapeSpecial.None;\n" +
        "        helm = HelmSpecial.None;\n" +
        "        weapon = WeaponSpecial.None;\n" +
        "        string[] parts = profile.Split('|');\n" +
        "        if (parts.Length != 5 || !Enum.TryParse(parts[0], out type))\n" +
        "        {\n" +
        "            type = EnhancementType.Lucky;\n" +
        "            return false;\n" +
        "        }\n" +
        "\n" +
        "        if (Enum.TryParse(parts[1], out WeaponSpecial forge) && WeaponUnlocked(forge))\n" +
        "            weapon = forge;\n" +
        "        else if (Enum.TryParse(parts[2], out WeaponSpecial awe) && awe != WeaponSpecial.None && uAwe())\n" +
        "            weapon = awe;\n" +
        "        if (Enum.TryParse(parts[3], out CapeSpecial c) && CapeUnlocked(c))\n" +
        "            cape = c;\n" +
        "        if (Enum.TryParse(parts[4], out HelmSpecial h) && HelmUnlocked(h))\n" +
        "            helm = h;\n" +
        "        return true;\n" +
        "\n" +
        "        bool WeaponUnlocked(WeaponSpecial s) => s switch\n" +
        "        {\n" +
        "            WeaponSpecial.Forge => uForgeWeapon(),\n" +
        "            WeaponSpecial.Lacerate => uLacerate(),\n" +
        "            WeaponSpecial.Smite => uSmite(),\n" +
        "            WeaponSpecial.Valiance => uValiance(),\n" +
        "            WeaponSpecial.Arcanas_Concerto => uArcanasConcerto(),\n" +
        "            WeaponSpecial.Acheron => uAcheron(),\n" +
        "            WeaponSpecial.Elysium => uElysium(),\n" +
        "            WeaponSpecial.Praxis => uPraxis(),\n" +
        "            WeaponSpecial.Dauntless => uDauntless(),\n" +
        "            WeaponSpecial.Ravenous => uRavenous(),\n" +
        "            _ => false\n" +
        "        };\n" +
        "\n" +
        "        bool CapeUnlocked(CapeSpecial s) => s switch\n" +
        "        {\n" +
        "            CapeSpecial.Forge => uForgeCape(),\n" +
        "            CapeSpecial.Absolution => uAbsolution(),\n" +
        "            CapeSpecial.Avarice => uAvarice(),\n" +
        "            CapeSpecial.Vainglory => uVainglory(),\n" +
        "            CapeSpecial.Penitence => uPenitence(),\n" +
        "            CapeSpecial.Lament => uLament(),\n" +
        "            _ => false\n" +
        "        };\n" +
        "\n" +
        "        bool HelmUnlocked(HelmSpecial s) => s switch\n" +
        "        {\n" +
        "            HelmSpecial.Forge => uForgeHelm(),\n" +
        "            HelmSpecial.Vim => uVim(),\n" +
        "            HelmSpecial.Examen => uExamen(),\n" +
        "            HelmSpecial.Anima => uAnima(),\n" +
        "            HelmSpecial.Pneuma => uPneuma(),\n" +
        "            HelmSpecial.Hearty => uHearty(),\n" +
        "            _ => false\n" +
        "        };\n" +
        "    }\n" +
        "\n" +
        "    /// <summary>\n" +
        "    /// Enhances the equipped items with a profile (see <see cref=\"ResolveEnhanceProfile\"/>), ignoring \"Disable AutoEnhance\".\n" +
        "    /// Used by the \"Enhance\" button of the CoreBots Options \"Class Enhance\" tab.\n" +
        "    /// </summary>\n" +
        "    public void EnhanceEquippedWithProfile(string profile)\n" +
        "    {\n" +
        "        if (!ResolveEnhanceProfile(profile, out EnhancementType type, out CapeSpecial cape, out HelmSpecial helm, out WeaponSpecial weapon))\n" +
        "        {\n" +
        "            Core.Logger($\"Invalid enhance profile: {profile}\");\n" +
        "            return;\n" +
        "        }\n" +
        "\n" +
        "        string[] parts = profile.Split('|');\n" +
        "        bool keepWeapon = parts[1] == \"Keep\", keepCape = parts[3] == \"Keep\", keepHelm = parts[4] == \"Keep\";\n" +
        "        Core.Logger($\"Enhancing equipped items: {type}, weapon {(keepWeapon ? \"Keep\" : weapon)}, cape {(keepCape ? \"Keep\" : cape)}, helm {(keepHelm ? \"Keep\" : helm)}\");\n" +
        "        try\n" +
        "        {\n" +
        "            AutoEnhance(Bot.Inventory.Items.FindAll(i => i.Equipped && EnhanceableCatagories.Contains(i.Category)\n" +
        "                && !(keepWeapon && i.ItemGroup == \"Weapon\")\n" +
        "                && !(keepCape && i.Category == ItemCategory.Cape)\n" +
        "                && !(keepHelm && i.Category == ItemCategory.Helm)), type, cape, helm, weapon);\n" +
        "        }\n" +
        "        catch (Exception e)\n" +
        "        {\n" +
        "            AdvCrash(e);\n" +
        "        }\n" +
        "    }\n" +
        "\n" +
        "    /// <returns>The class profile, or null when the class has no custom profile.</returns>\n" +
        "    private string? ClassEnhanceProfile(string className, ref EnhancementType? type, ref CapeSpecial cape, ref HelmSpecial helm, ref WeaponSpecial weapon)\n" +
        "    {\n" +
        "        if (!Core.CBOString($\"ClassEnh[{className.ToLower()}]\", out string profile) && !Core.CBOString(\"ClassEnh[*]\", out profile))\n" +
        "            return null;\n" +
        "        if (!ResolveEnhanceProfile(profile, out EnhancementType baseEnh, out cape, out helm, out weapon))\n" +
        "            return null;\n" +
        "\n" +
        "        type = baseEnh;\n" +
        "        Core.Logger($\"Class Enhance profile for {className}: {profile}\");\n" +
        "        return profile;\n" +
        "    }\n" +
        "\n";
}
