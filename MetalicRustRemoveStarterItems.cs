using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MetalicRustRemoveStarterItems", "MetalicRust", "3.1.0")]
    [Description("Replaces Rust starter Rock, Torch, and Stone Hatchet with a Metal Hatchet and Pickaxe.")]
    public class MetalicRustRemoveStarterItems : RustPlugin
    {
        private const float FirstCheckDelay = 0.5f;
        private const float SecondCheckDelay = 1.5f;
        private const float ThirdCheckDelay = 3.0f;

        // Слот 0 = железный (metal) топор
        // Слот 1 = кирка
        private const int HatchetSlot = 0;
        private const int PickaxeSlot = 1;

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null)
                return;

            ScheduleReplacement(player);
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null)
                return;

            // После рестарта игрок может загрузиться уже со стартовыми предметами.
            ScheduleReplacement(player);
        }

        private void ScheduleReplacement(BasePlayer player)
        {
            timer.Once(FirstCheckDelay, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                ReplaceStarterItems(player);
            });

            // Даём IQKits время закончить выдачу своего набора.
            timer.Once(SecondCheckDelay, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                ReplaceStarterItems(player);
            });

            timer.Once(ThirdCheckDelay, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                ReplaceStarterItems(player);
            });
        }

        private void ReplaceStarterItems(BasePlayer player)
        {
            if (player == null || player.inventory == null)
                return;

            RemoveStarterItem(player.inventory.containerMain);
            RemoveStarterItem(player.inventory.containerBelt);
            RemoveStarterItem(player.inventory.containerWear);

            ItemContainer belt = player.inventory.containerBelt;
            if (belt == null)
                return;

            // Если IQKits уже выдал эти предметы, новых не создаём.
            Item hatchet = FindItem(player, "hatchet");
            Item pickaxe = FindItem(player, "pickaxe");

            // Если нужных инструментов нет, создаём их.
            if (hatchet == null)
                hatchet = ItemManager.CreateByName("hatchet", 1);

            if (pickaxe == null)
                pickaxe = ItemManager.CreateByName("pickaxe", 1);

            if (hatchet == null || pickaxe == null)
                return;

            // Переносим инструменты во временное место, только если они уже в поясе
            // в неправильных слотах. Никакие другие предметы не удаляем.
            MoveOutOfTargetSlot(belt, HatchetSlot, hatchet, player.inventory.containerMain);
            MoveOutOfTargetSlot(belt, PickaxeSlot, pickaxe, player.inventory.containerMain);

            // Если инструменты находятся в контейнере, освобождаем их от старого родителя.
            if (hatchet.parent != null && hatchet.parent != belt)
                hatchet.RemoveFromContainer();

            if (pickaxe.parent != null && pickaxe.parent != belt)
                pickaxe.RemoveFromContainer();

            // Ставим строго: 0 = топор, 1 = кирка.
            if (belt.GetSlot(HatchetSlot) == null)
                hatchet.MoveToContainer(belt, HatchetSlot, false);

            if (belt.GetSlot(PickaxeSlot) == null)
                pickaxe.MoveToContainer(belt, PickaxeSlot, false);

            // Если целевые слоты всё ещё заняты, не удаляем чужие предметы.
            // Оставляем инструменты там, где есть свободное место.
            if (belt.GetSlot(HatchetSlot) != hatchet)
                hatchet.MoveToContainer(belt, -1, false);

            if (belt.GetSlot(PickaxeSlot) != pickaxe)
                pickaxe.MoveToContainer(belt, -1, false);
        }

        private void RemoveStarterItem(ItemContainer container)
        {
            if (container == null || container.itemList == null)
                return;

            for (int i = container.itemList.Count - 1; i >= 0; i--)
            {
                Item item = container.itemList[i];

                if (item == null || item.info == null)
                    continue;

                string shortname = item.info.shortname;

                if (string.Equals(shortname, "rock", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shortname, "torch", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shortname, "stonehatchet", StringComparison.OrdinalIgnoreCase))
                {
                    item.Remove();
                }
            }
        }

        private void MoveOutOfTargetSlot(
            ItemContainer belt,
            int slot,
            Item wanted,
            ItemContainer main)
        {
            Item current = belt.GetSlot(slot);

            if (current == null || current == wanted)
                return;

            // Камень/факел к этому моменту уже удалены. Любой другой предмет кита
            // не удаляем — пытаемся перенести его в основной инвентарь.
            current.MoveToContainer(main, -1, false);
        }

        private Item FindItem(BasePlayer player, string shortname)
        {
            if (player == null || player.inventory == null)
                return null;

            Item item = FindInContainer(player.inventory.containerBelt, shortname);
            if (item != null)
                return item;

            item = FindInContainer(player.inventory.containerMain, shortname);
            if (item != null)
                return item;

            return FindInContainer(player.inventory.containerWear, shortname);
        }

        private Item FindInContainer(ItemContainer container, string shortname)
        {
            if (container == null || container.itemList == null)
                return null;

            foreach (Item item in container.itemList)
            {
                if (item == null || item.info == null)
                    continue;

                if (string.Equals(item.info.shortname, shortname, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }
    }
}
