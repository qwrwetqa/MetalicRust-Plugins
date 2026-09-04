using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Quick Smelt", "MetalicRust", "5.1.7")]
    [Description("Increases the speed of furnace smelting")]
    class QuickSmelt : RustPlugin
    {
        private static QuickSmelt _instance;

        private const string PermissionUse = "quicksmelt.use";

        private static Configuration _config;

        #region Configuration

        private class Configuration
        {
            [JsonProperty(PropertyName = "Use Permission")]
            public bool UsePermission = true;

            [JsonProperty(PropertyName = "Speed Multipliers",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> SpeedMultipliers =
                new Dictionary<string, float>
                {
                    { "global", 1.0f },
                    { "furnace.shortname", 1.0f }
                };

            [JsonProperty(PropertyName = "Fuel Usage Speed Multipliers",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> FuelSpeedMultipliers =
                new Dictionary<string, float>
                {
                    { "global", 1.0f },
                    { "furnace.shortname", 1.0f }
                };

            [JsonProperty(PropertyName = "Fuel Usage Multipliers",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> FuelUsageMultipliers =
                new Dictionary<string, int>
                {
                    { "global", 1 },
                    { "furnace.shortname", 1 }
                };

            [JsonProperty(PropertyName = "Output Multipliers",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, Dictionary<string, float>> OutputMultipliers =
                new Dictionary<string, Dictionary<string, float>>
                {
                    {
                        "global",
                        new Dictionary<string, float>
                        {
                            { "global", 1.0f }
                        }
                    },
                    {
                        "furnace.shortname",
                        new Dictionary<string, float>
                        {
                            { "item.shortname", 1.0f }
                        }
                    }
                };

            [JsonProperty(PropertyName = "Whitelist",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> Whitelist =
                new Dictionary<string, List<string>>
                {
                    {
                        "global",
                        new List<string>
                        {
                            "item.shortname"
                        }
                    },
                    {
                        "furnace.shortname",
                        new List<string>
                        {
                            "item.shortname"
                        }
                    }
                };

            [JsonProperty(PropertyName = "Blacklist",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> Blacklist =
                new Dictionary<string, List<string>>
                {
                    {
                        "global",
                        new List<string>
                        {
                            "item.shortname"
                        }
                    },
                    {
                        "furnace.shortname",
                        new List<string>
                        {
                            "item.shortname"
                        }
                    }
                };

            [JsonProperty(PropertyName =
                "Smelting Frequencies (Smelt items every N smelting ticks)",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> SmeltingFrequencies =
                new Dictionary<string, int>
                {
                    { "global", 1 },
                    { "furnace.shortname", 1 }
                };

            [JsonProperty(PropertyName = "Debug")]
            public bool Debug = false;
        }

        #endregion

        #region Configuration Loading

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();

                if (_config == null)
                    throw new Exception("Configuration is null.");
            }
            catch
            {
                PrintError(
                    "Your configuration file contains an error. " +
                    "Using default configuration values.");

                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            _instance = this;

            permission.RegisterPermission(
                PermissionUse,
                this
            );
        }

        private void OnServerInitialized()
        {
            _instance = this;

            var ovens = UnityEngine.Object.FindObjectsOfType<BaseOven>();

            PrintDebug(
                $"Processing BaseOven(s).. Amount: {ovens.Length}."
            );

            for (var i = 0; i < ovens.Length; i++)
            {
                var oven = ovens[i];

                if (oven == null)
                    continue;

                OnEntitySpawned(oven);
            }

            timer.Once(1f, () =>
            {
                for (var i = 0; i < ovens.Length; i++)
                {
                    var oven = ovens[i];

                    if (oven == null ||
                        oven.IsDestroyed ||
                        !oven.IsOn())
                        continue;

                    if (!CanUse(oven.OwnerID))
                        continue;

                    var component =
                        oven.gameObject.GetComponent<FurnaceController>();

                    if (component == null)
                        component =
                            oven.gameObject.AddComponent<FurnaceController>();

                    component.StartCooking();
                }
            });
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var oven = entity as BaseOven;

            if (oven == null)
                return;

            if (oven.gameObject == null)
                return;

            var component =
                oven.gameObject.GetComponent<FurnaceController>();

            if (component == null)
                oven.gameObject.AddComponent<FurnaceController>();
        }

        private object OnOvenToggle(
            StorageContainer oven,
            BasePlayer player)
        {
            if (oven == null || player == null)
                return null;

            if (oven is BaseFuelLightSource)
                return null;

            if (oven.needsBuildingPrivilegeToUse &&
                !player.CanBuild())
                return null;

            PrintDebug("OnOvenToggle called");

            var component =
                oven.gameObject.GetComponent<FurnaceController>();

            if (component == null)
            {
                component =
                    oven.gameObject.AddComponent<FurnaceController>();
            }

            var canUse =
                CanUse(oven.OwnerID) ||
                CanUse(player.userID);

            if (oven.IsOn())
            {
                component.StopCooking();
            }
            else
            {
                if (canUse)
                {
                    component.StartCooking();
                }
                else
                {
                    PrintDebug(
                        $"No permission ({player.userID})"
                    );

                    return null;
                }
            }

            return false;
        }

        private void Unload()
        {
            var ovens =
                UnityEngine.Object.FindObjectsOfType<BaseOven>();

            PrintDebug(
                $"Processing BaseOven(s).. Amount: {ovens.Length}."
            );

            for (var i = 0; i < ovens.Length; i++)
            {
                var oven = ovens[i];

                if (oven == null)
                    continue;

                var component =
                    oven.gameObject.GetComponent<FurnaceController>();

                if (component != null)
                {
                    if (oven.IsOn())
                    {
                        PrintDebug(
                            "Oven is on. Restarted cooking"
                        );

                        component.StopCooking();
                    }

                    UnityEngine.Object.Destroy(component);
                }
            }

            PrintDebug("Done.");
        }

        #endregion

        #region Permissions

        private bool CanUse(ulong id)
        {
            if (!_config.UsePermission)
                return true;

            return permission.UserHasPermission(
                id.ToString(),
                PermissionUse
            );
        }

        #endregion

        #region Debug

        private static void PrintDebug(string message)
        {
            if (_config == null)
                return;

            if (!_config.Debug)
                return;

            Debug.Log(
                $"DEBUG ({_instance.Name}) > {message}"
            );
        }

        #endregion

        #region Furnace Controller

        public class FurnaceController : FacepunchBehaviour
        {
            private int _ticks;

            private BaseOven _oven;

            private BaseOven Furnace
            {
                get
                {
                    if (_oven == null)
                        _oven = GetComponent<BaseOven>();

                    return _oven;
                }
            }

            private float _speedMultiplier;
            private float _fuelSpeedMultiplier;
            private int _fuelUsageMultiplier;
            private int _smeltingFrequency;

            private Dictionary<string, float> _outputModifiers;

            private List<string> _blacklist;
            private List<string> _whitelist;

            #region Initialization

            private void Awake()
            {
                if (Furnace == null)
                    return;

                float modifierF;
                int modifierI;

                if (!_config.SpeedMultipliers.TryGetValue(
                        Furnace.ShortPrefabName,
                        out modifierF) &&
                    !_config.SpeedMultipliers.TryGetValue(
                        "global",
                        out modifierF))
                {
                    modifierF = 1.0f;
                }

                if (modifierF <= 0f)
                    modifierF = 1.0f;

                _speedMultiplier =
                    0.5f / modifierF;

                if (!_config.FuelSpeedMultipliers.TryGetValue(
                        Furnace.ShortPrefabName,
                        out modifierF) &&
                    !_config.FuelSpeedMultipliers.TryGetValue(
                        "global",
                        out modifierF))
                {
                    modifierF = 1.0f;
                }

                if (modifierF < 0f)
                    modifierF = 0f;

                _fuelSpeedMultiplier =
                    modifierF;

                if (!_config.FuelUsageMultipliers.TryGetValue(
                        Furnace.ShortPrefabName,
                        out modifierI) &&
                    !_config.FuelUsageMultipliers.TryGetValue(
                        "global",
                        out modifierI))
                {
                    modifierI = 1;
                }

                if (modifierI < 1)
                    modifierI = 1;

                _fuelUsageMultiplier =
                    modifierI;

                if (!_config.SmeltingFrequencies.TryGetValue(
                        Furnace.ShortPrefabName,
                        out modifierI) &&
                    !_config.SmeltingFrequencies.TryGetValue(
                        "global",
                        out modifierI))
                {
                    modifierI = 1;
                }

                if (modifierI < 1)
                    modifierI = 1;

                _smeltingFrequency =
                    modifierI;

                if (!_config.OutputMultipliers.TryGetValue(
                        Furnace.ShortPrefabName,
                        out _outputModifiers))
                {
                    _config.OutputMultipliers.TryGetValue(
                        "global",
                        out _outputModifiers
                    );
                }

                if (!_config.Blacklist.TryGetValue(
                        Furnace.ShortPrefabName,
                        out _blacklist))
                {
                    _config.Blacklist.TryGetValue(
                        "global",
                        out _blacklist
                    );
                }

                if (!_config.Whitelist.TryGetValue(
                        Furnace.ShortPrefabName,
                        out _whitelist))
                {
                    _config.Whitelist.TryGetValue(
                        "global",
                        out _whitelist
                    );
                }
            }

            #endregion

            #region Burnable

            private Item FindBurnable()
            {
                if (Furnace == null ||
                    Furnace.inventory == null)
                    return null;

                var burnable =
                    Interface.Call<Item>(
                        "OnFindBurnable",
                        Furnace
                    );

                if (burnable != null)
                    return burnable;

                foreach (var item in Furnace.inventory.itemList)
                {
                    if (item == null)
                        continue;

                    if (!item.IsValid())
                        continue;

                    if (!Furnace.IsBurnableItem(item))
                        continue;

                    return item;
                }

                return null;
            }

            #endregion

            #region Permission / Lists

            private bool? IsAllowed(string shortname)
            {
                if (_blacklist != null &&
                    _blacklist.Contains(shortname))
                    return false;

                if (_whitelist != null &&
                    _whitelist.Contains(shortname))
                    return true;

                return null;
            }

            #endregion

            #region Output

            private float OutputMultiplier(string shortname)
            {
                float modifier;

                if (_outputModifiers == null)
                    return 1.0f;

                if (!_outputModifiers.TryGetValue(
                        shortname,
                        out modifier))
                {
                    if (!_outputModifiers.TryGetValue(
                            "global",
                            out modifier))
                    {
                        modifier = 1.0f;
                    }
                }

                if (modifier < 0f)
                    modifier = 0f;

                PrintDebug(
                    $"{shortname} modifier: {modifier}"
                );

                return modifier;
            }

            #endregion

            #region Cooking

            public void Cook()
            {
                if (Furnace == null ||
                    Furnace.IsDestroyed)
                {
                    StopCooking();
                    return;
                }

                var itemBurnable =
                    FindBurnable();

                if (Interface.CallHook(
                        "OnOvenCook",
                        this,
                        itemBurnable) != null)
                    return;

                if (itemBurnable == null)
                {
                    StopCooking();
                    return;
                }

                SmeltItems();

                if (Furnace.inventory != null)
                {
                    foreach (var itemCooking
                             in Furnace.inventory.itemList)
                    {
                        if (itemCooking == null)
                            continue;

                        if (itemCooking.position >=
                            Furnace._inputSlotIndex &&
                            itemCooking.position <
                            Furnace._inputSlotIndex +
                            Furnace.inputSlots &&
                            !itemCooking.HasFlag(
                                global::Item.Flag.Cooking))
                        {
                            itemCooking.SetFlag(
                                global::Item.Flag.Cooking,
                                true
                            );

                            itemCooking.MarkDirty();
                        }
                    }
                }

                var slot =
                    Furnace.GetSlot(
                        BaseEntity.Slot.FireMod
                    );

                if (slot)
                {
                    slot.SendMessage(
                        "Cook",
                        0.5f,
                        SendMessageOptions.DontRequireReceiver
                    );
                }

                var burnable =
                    itemBurnable.info.GetComponent<ItemModBurnable>();

                if (burnable == null)
                {
                    StopCooking();
                    return;
                }

                itemBurnable.fuel -=
                    0.5f *
                    (Furnace.cookingTemperature / 200f) *
                    _fuelSpeedMultiplier;

                if (!itemBurnable.HasFlag(
                        global::Item.Flag.OnFire))
                {
                    itemBurnable.SetFlag(
                        global::Item.Flag.OnFire,
                        true
                    );

                    itemBurnable.MarkDirty();
                }

                if (itemBurnable.fuel <= 0f)
                {
                    ConsumeFuel(
                        itemBurnable,
                        burnable
                    );
                }

                _ticks++;

                Interface.CallHook(
                    "OnOvenCooked",
                    this,
                    itemBurnable,
                    slot
                );
            }

            #endregion

            #region Fuel

            private void ConsumeFuel(
                Item fuel,
                ItemModBurnable burnable)
            {
                if (fuel == null ||
                    burnable == null ||
                    Furnace == null)
                    return;

                if (Interface.CallHook(
                        "OnFuelConsume",
                        Furnace,
                        fuel,
                        burnable) != null)
                    return;

                if (Furnace.allowByproductCreation &&
                    burnable.byproductItem != null &&
                    Random.Range(0f, 1f) >
                    burnable.byproductChance)
                {
                    var def =
                        burnable.byproductItem;

                    var amount =
                        (int)(
                            burnable.byproductAmount *
                            OutputMultiplier(def.shortname)
                        );

                    if (amount > 0)
                    {
                        var item =
                            ItemManager.Create(
                                def,
                                amount
                            );

                        if (item != null)
                        {
                            if (!item.MoveToContainer(
                                    Furnace.inventory))
                            {
                                StopCooking();

                                item.Drop(
                                    Furnace.inventory.dropPosition,
                                    Furnace.inventory.dropVelocity
                                );
                            }
                        }
                    }
                }

                if (fuel.amount <=
                    _fuelUsageMultiplier)
                {
                    fuel.Remove();
                    return;
                }

                fuel.UseItem(
                    _fuelUsageMultiplier
                );

                fuel.fuel =
                    burnable.fuelAmount;

                fuel.MarkDirty();

                Interface.CallHook(
                    "OnFuelConsumed",
                    Furnace,
                    fuel,
                    burnable
                );
            }

            #endregion

            #region Smelting

            private void SmeltItems()
            {
                if (_smeltingFrequency <= 0)
                    _smeltingFrequency = 1;

                if (_ticks %
                    _smeltingFrequency != 0)
                    return;

                if (Furnace == null ||
                    Furnace.inventory == null)
                    return;

                for (
                    var i = 0;
                    i < Furnace.inventory.itemList.Count;
                    i++)
                {
                    var item =
                        Furnace.inventory.itemList[i];

                    if (item == null ||
                        !item.IsValid())
                        continue;

                    var cookable =
                        item.info.GetComponent<ItemModCookable>();

                    if (cookable == null)
                        continue;

                    var isAllowed =
                        IsAllowed(
                            item.info.shortname
                        );

                    if (isAllowed != null &&
                        !isAllowed.Value)
                        continue;

                    var temperature =
                        item.temperature;

                    /*
                     * Current Rust API no longer has:
                     *
                     * CanBeCookedByAtTemperature()
                     *
                     * Therefore we check lowTemp/highTemp directly.
                     */

                    var temperatureAllowed =
                        temperature >= cookable.lowTemp &&
                        temperature <= cookable.highTemp;

                    if (!temperatureAllowed &&
                        isAllowed == null)
                    {
                        if (!cookable.setCookingFlag ||
                            !item.HasFlag(
                                global::Item.Flag.Cooking))
                            continue;

                        item.SetFlag(
                            global::Item.Flag.Cooking,
                            false
                        );

                        item.MarkDirty();

                        continue;
                    }

                    if (cookable.cookTime > 0 &&
                        (_ticks * 1f /
                         _smeltingFrequency) %
                        cookable.cookTime > 0)
                        continue;

                    if (cookable.setCookingFlag &&
                        !item.HasFlag(
                            global::Item.Flag.Cooking))
                    {
                        item.SetFlag(
                            global::Item.Flag.Cooking,
                            true
                        );

                        item.MarkDirty();
                    }

                    var amountConsumed =
                        (int)Furnace.GetSmeltingSpeed();

                    if (amountConsumed < 1)
                        amountConsumed = 1;

                    amountConsumed =
                        Math.Min(
                            amountConsumed,
                            item.amount
                        );

                    if (amountConsumed <= 0)
                        continue;

                    if (item.amount >
                        amountConsumed)
                    {
                        item.amount -=
                            amountConsumed;

                        item.MarkDirty();
                    }
                    else
                    {
                        item.Remove();
                    }

                    if (cookable.becomeOnCooked == null)
                        continue;

                    var outputAmount =
                        (int)(
                            cookable.amountOfBecome *
                            amountConsumed *
                            OutputMultiplier(
                                cookable.becomeOnCooked.shortname
                            )
                        );

                    if (outputAmount <= 0)
                        continue;

                    var itemProduced =
                        ItemManager.Create(
                            cookable.becomeOnCooked,
                            outputAmount
                        );

                    if (itemProduced == null)
                        continue;

                    if (item.parent != null &&
                        itemProduced.MoveToContainer(
                            item.parent))
                    {
                        continue;
                    }

                    if (Furnace.inventory != null)
                    {
                        itemProduced.Drop(
                            Furnace.inventory.dropPosition,
                            Furnace.inventory.dropVelocity
                        );
                    }
                    else
                    {
                        itemProduced.Remove();
                    }

                    StopCooking();
                }
            }

            #endregion

            #region Start / Stop

            public void StartCooking()
            {
                if (Furnace == null ||
                    Furnace.IsDestroyed)
                    return;

                if (FindBurnable() == null)
                {
                    PrintDebug(
                        "No burnable."
                    );

                    return;
                }

                StopCooking();

                PrintDebug(
                    "Starting cooking.."
                );

                if (Furnace.inventory != null)
                {
                    Furnace.inventory.temperature =
                        Furnace.cookingTemperature;
                }

                Furnace.UpdateAttachmentTemperature();

                PrintDebug(
                    $"Speed Multiplier: {_speedMultiplier}"
                );

                /*
                 * IMPORTANT:
                 * BaseOven.SetFlag() was removed
                 * because the current Rust API
                 * does not expose it on BaseOven.
                 */

                Furnace.InvokeRepeating(
                    Cook,
                    _speedMultiplier,
                    _speedMultiplier
                );
            }

            public void StopCooking()
            {
                if (Furnace == null)
                    return;

                PrintDebug(
                    "Stopping cooking.."
                );

                Furnace.CancelInvoke(
                    Cook
                );

                Furnace.StopCooking();
            }

            #endregion
        }

        #endregion
    }
}