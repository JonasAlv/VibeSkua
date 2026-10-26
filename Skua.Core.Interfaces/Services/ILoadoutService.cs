using System;
using Skua.Core.Models.Items;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Skua.Core.Interfaces;

public interface ILoadoutService
{
    event Action? LoadoutsChanged;
    bool IsLoggedIn { get; }
    /// <summary>
    /// Account whose loadouts are shown and edited; null for the one logged in here.
    /// </summary>
    string? Profile { get; set; }
    List<LoadoutProfile> Loadouts { get; }
    bool SaveLoadout(LoadoutProfile loadout);
    /// <summary>
    /// Deletes a specified loadout profile.
    /// </summary>
    /// <param name="loadout">The loadout profile to delete.</param>
    void DeleteLoadout(LoadoutProfile loadout);
    void Refresh();
    /// <param name="enhanceProfile">"Base|WeaponForge|WeaponAwe|Cape|Helm" class profile applied instead of the loadout's saved enhancements, or null.</param>
    Task<List<string>> EquipLoadoutAsync(LoadoutProfile loadout, string? enhanceProfile = null);
    LoadoutProfile CreateFromCurrentEquipped(string name);
}
