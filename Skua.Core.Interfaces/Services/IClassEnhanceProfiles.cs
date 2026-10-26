namespace Skua.Core.Interfaces;

/// <summary>
/// Per-class enhance profiles of the CoreBots Options "Class Enhance" tab.
/// </summary>
public interface IClassEnhanceProfiles
{
    /// <returns>The "Base|WeaponForge|WeaponAwe|Cape|Helm" profile saved for <paramref name="className"/> on <paramref name="account"/>.</returns>
    string Resolve(string account, string className);

    /// <summary>
    /// Opens CoreBots Options on the Class Enhance tab of <paramref name="account"/>, with <paramref name="className"/> selected.
    /// </summary>
    void Edit(string account, string className);
}
