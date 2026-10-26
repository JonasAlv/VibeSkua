## VibeSkua v2.0.9

This release introduces the native Skua Cosmetics Plugin for client-side appearance customization and outfit management.

Special thanks to **Lord Exelot**, **bogalj**, and everyone else who contributed their work to the cosmetics tooling.

### What's Changed

### Skua Cosmetics Plugin
The `Skua.Plugin.Cosmetics` plugin enables client-side character customization without owning items or altering server state:
- **Comprehensive Item Catalog**: Browsable database of over 67,000 weapons, armors, helms, capes, and pets with instant search and background catalog updates.
- **Client-Side Equipment Spoofing**: Hooks Flash MovieClip rendering to preview and equip any cosmetic item locally with zero network footprint.
- **Full 6-Channel Color Customization**: Complete character recoloring across Base, Trim, Accessory, Eye, Skin, and Hair channels with real-time Flash synchronization.
- **Target Look Replication**: Inspect and clone any player's visual loadout, color palette, and pet state in a single click.
- **Outfit Presets**: Save, name, and switch between custom visual outfits with safe set management.
- **Authentic Reset to Real Gear**: Instantly revert visual equipment and all six color channels back to genuine server-equipped inventory items.
- **Modern Dark UI**: Elevated card design with responsive accent borders and streamlined category filtering chips.

---

## VibeSkua v2.0.7

This release introduces the Army Queue automation suite for sequential multi-squad execution, a universal auto-relogin watchdog engine with smart in-game intent recognition, granular Discord webhook health ping controls, and account management safety confirmations.

### What's Changed

### 1. Army Queue Automation Suite
- **Sequential Multi-Squad Execution**: Define and run unlimited squads sequentially. Once a squad finishes, the next squad launches automatically.
- **Queue Flow Controls**: Added Pause Queue (holds before next squad), Resume Queue, and Stop Queue controls.
- **Per-Member Role Scripts**: Assign distinct scripts (Support, DPS, Taunter) per account or apply squad default scripts.
- **Integrated Script Repository Browser**: Integrated "Search Scripts" window directly into the Army Queue for instant script browsing and assignment.
- **Isolated Pre-Configured Script Options**: Configure script parameters individually per squad and account with isolated `.cfg` profiles running silently without dialog prompts.
- **Uncapped Execution**: Removed arbitrary 30-minute timeout limits so long farming chains and boss rotations run uninterrupted.

### 2. Universal Auto-Relogin Watchdog & Supervisor Engine
- **Single Authoritative Supervisor**: Replaced fragmented reconnect checks with a high-frequency (10–50ms) watchdog tracking in-game status via `Flash.IsWorldLoaded && Player.LoggedIn`.
- **Combat Death Immunity**: Tracking evaluates logged-in state rather than combat status, eliminating accidental relogins while awaiting respawn after dying in combat.
- **Hardware-Level Event Bridging**: Hooked Flash-side `SFSEvent.onConnectionLost` to trigger recovery the exact millisecond the socket drops.
- **In-Game Intentional Action Recognition**: Distinguishes intentional in-game actions ("Change Server" and "Log Out") from accidental drops via Flash UI button inspection (`btnLogout`, `btnChange`) and egress packet detection (`%xt%zm%eLO%`, `%xt%zm%eSL%`).
- **Dynamic Watchdog Re-Arming**: Disarms relogin while browsing the server list and automatically re-arms when entering the game world.
- **Server Forced Logout Bug Fix**: Fixed an issue where AQW server `%xt%cmd%-1%logout%` packets were unconditionally treated as normal user logouts and disabled auto-relogin.
- **Nuclear Flash Modal Cleanup**: Comprehensive purge of `mcLogin.ModalStack`, `mcO.ModalStack`, `ui.mcPopup`, `MsgBox`, and kick/ban alerts (`mcLogin.warning`).
- **Optimized TCP Ghost Session Backoff**: Replaced legacy 70s kick delay with a ~20s cooldown under `SafeRelogin` and sub-second recovery when disabled.
- **Fatal Account Lockout Protection**: Detects permanent ban or account disabled messages to halt relogin loops and protect player IPs.

### 3. Discord Webhook & Health Monitoring
- **Toggleable Health Pings**: Added `Webhook: Enable Health Pings` setting to control periodic status embeds.
- **Adjustable Interval**: Configurable ping interval with minimum 1-minute validation (default: 10 mins).
- **Channel Isolation**: Personal Discord webhook channels only receive health pings when explicitly enabled.

### 4. Account Manager Safety & Context Menus
- **Deletion Confirmations**: Added confirmation dialogs before removing individual accounts, batch selections, or account groups.
- **Context Menu Integration**: Added "Remove Selected" directly to the account grid right-click context menu.

---

## VibeSkua v2.0.6

This release introduces script pinning, playlist reordering in the Script Scheduler, native Skua Nexus webhook alerts with live statistics reporting, and major performance optimizations for script search and options dialogs.

### What's Changed
- **Script Pinning**: Pin your favorite scripts to keep them anchored at the top of the Script Repository, with a dedicated "Pinned" category filter.
- **Script Search Performance**: Eliminated first-launch lag and window freezes using asynchronous cache loading and background processing.
- **Script Scheduler Queue Reordering**: Added Up and Down controls to easily reorder queued scripts in playlists.
- **Skua Nexus Webhook Integration**: Added native Skua Nexus webhook support alongside Discord, delivering automated dual alerts, character information, equipment/quest snapshots, and live bot statistics.
- **Script Options Account Disambiguation**: Window titles now display the active account name (`Script Options - [Account]`) for multi-client clarity.
- **Script Options Performance**: Resolved popup lag and freezes by fixing a startup race condition and streamlining option editor controls.
- **Account Manager Bulk Selection**: Added a "Select All" button to quickly toggle all accounts for batch operations.
- **Webhook Input UI Fix**: Constrained the webhook input box to prevent window overflow and ensure the "Set" button stays visible.
- **Script Loader Credits**: Updated community issue report contacts to `@Tato2/@lonewolf_12`.

---

## VibeSkua v2.0.5

Updates webhook reporting for live dashboard snapshots.

### What's Changed
- **Webhook Statistics & Account Snapshots**: Added automated character data, equipment, quest, and live bot statistics reporting.

---

## VibeSkua v2.0.4 (Hotfix)

This hotfix resolves a Manager crash when starting accounts with a script selected.

### What's Changed
- **Account Launch Stability**: Fixed a crash where launching accounts while auto-start or a script was selected caused the Manager to crash during background settings synchronization.
- **In-Place Account Refresh**: Settings updates from running client instances now smoothly sync with the Manager without disrupting account checkmarks or active launch queues.

---

## VibeSkua v2.0.3

This release resolves the initial login connection freeze, introduces a rich account context menu in the Manager, enables real-time cross-process sync between clients and Manager, and delivers redesigned theme-aware account tag badges.

---

### What's Changed

### 1. Multi-Client Tab Bar & Group Assignment
- Restored Tab Context Menu: Restored right-click context menu on client tabs to quickly add/remove running accounts from groups.
- Login Validation: Blocks adding unauthenticated placeholder accounts (e.g. "User 1") to groups, displaying a prompt to log in first.
- Settings Persistence: Group additions from client tabs now automatically register the account under ManagedAccounts in Skua.settings.json.

---

### 2. Account Manager Context Menu & Real-Time Sync
- Account Context Menu: Added right-click menu to accounts in both List and Grid views (Start, Start with Script, Add/Remove Group, Edit Tags, Open Folder, Delete).
- Cross-Process Live Sync: Added Windows named event (Global\VibeSkua.Settings.Changed) and FileSystemWatcher so changes made in client tabs reflect instantly in the Manager without restarting.
- Instant In-Place Refresh: Eliminated the need to switch tabs inside the Manager to see updated accounts or group memberships.

---

### 3. Account Tag Chips Redesign & Dynamic Theming
- Visible by Default: Enabled ShowTags = true by default so tags are visible without opening the edit dialog.
- Live Theme Synchronization: Bound tag chip brushes to the active theme; colors now update immediately when switching themes in Options or Manager.
- Redesigned Badge Styling: Revamped tag chips into modern translucent pills with soft 1px accent borders, hover highlights, and an integrated mini TagOutline icon.

---

### 4. Fix for Initial Login "Connecting to Server" Screen Freeze
- Bypassed Flash 3-Second Port 843 Timeout: Flash Player was timing out for 3 seconds trying to reach TCP port 843 before falling back to port 5588. Implemented Security.loadPolicyFile("xmlsocket://<ip>:<port>") in AS3 and preloaded all server socket policies in parallel as soon as the server list appears, eliminating the 3-second freeze.
- Removed Artificial Latency: Replaced a 1,000ms timer loop delay in connectToServer with immediate execution, reduced 1,000ms thread sleeps in ScriptServers.cs to 100ms, and skipped blocking HTTP calls when servers are already cached.
- Guarded Handshake COM Calls: Guarded Stats.GetSpace() at loginResponse with if (Flash.IsWorldLoaded) to prevent COM calls into uninitialized world objects, and added null-checks in AS3 _getObjectA to prevent runtime #1009 errors.

---

### Installation & Updates

- **Automatic Update (Existing Users)**: If you installed VibeSkua using `VibeSkua-win-Setup.exe`, the update will download automatically in the background and apply upon restarting the application.
- **VibeSkua-win-Setup.exe (New Installs)**: Recommended 1-click Windows installer with automatic desktop shortcuts and background auto-updates.
- **VibeSkua-win-Portable.zip**: Zero-install standalone archive. Extract to any directory and launch `VibeSkua.Manager.exe` or `VibeSkua.exe`.

---

### Credits

- **auqw/skua** — Original framework & botting API.
- **ArrowDev (BlackArrow on Discord)** — Early Avalonia port foundation.
- **l0newolf12** — Butler V5 Avalonia port foundation.
- **NinjaXz** — Complete Avalonia overhaul, army controls, stability engineering, and release pipeline.
