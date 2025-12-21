using System;
using System.Collections.Generic;
using AutoArm.Helpers;

namespace AutoArm
{
    public static class TickScheduler
    {
        public enum EventType : byte
        {
            CooldownExpiry,
            DroppedItemExpiry,
            BlacklistExpiry,
            ForcedWeaponGraceCheck,
            SkillCacheExpiry,
            SimpleSidearmsValidation,
            ReservationExpiry,
            TempBlacklistExpiry,
            MessageCacheExpiry
        }

        public readonly struct ScheduledEvent
        {
            public readonly EventType Type;
            public readonly int PrimaryId;
            public readonly int SecondaryId;

            public ScheduledEvent(EventType type, int primaryId, int secondaryId = 0)
            {
                Type = type;
                PrimaryId = primaryId;
                SecondaryId = secondaryId;
            }
        }

        private static readonly Dictionary<int, List<ScheduledEvent>> schedule =
            new Dictionary<int, List<ScheduledEvent>>(256);

        // Reverse index for O(1) cancel
        private static readonly Dictionary<(EventType, int), HashSet<int>> reverseIndex =
            new Dictionary<(EventType, int), HashSet<int>>(256);

        public static Action<int> OnCooldownExpired;
        public static Action<int> OnDroppedItemExpired;
        public static Action<int, int> OnBlacklistExpired;
        public static Action<int> OnForcedWeaponGraceCheck;
        public static Action<int> OnSkillCacheExpired;
        public static Action<int, int> OnSimpleSidearmsExpired;
        public static Action<int, int> OnReservationExpired;
        public static Action<int, int> OnTempBlacklistExpired;
        public static Action<int, int> OnMessageCacheExpired;

        public static void Schedule(int tick, EventType type, int primaryId, int secondaryId = 0)
        {
            if (!schedule.TryGetValue(tick, out var list))
            {
                list = ListPool<ScheduledEvent>.Get(8);
                schedule[tick] = list;
            }
            list.Add(new ScheduledEvent(type, primaryId, secondaryId));

            var key = (type, primaryId);
            if (!reverseIndex.TryGetValue(key, out var ticks))
            {
                ticks = new HashSet<int>();
                reverseIndex[key] = ticks;
            }
            ticks.Add(tick);
        }

        public static void ProcessTick(int currentTick)
        {
            if (!schedule.TryGetValue(currentTick, out var events))
                return;

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                var key = (evt.Type, evt.PrimaryId);
                if (reverseIndex.TryGetValue(key, out var ticks))
                {
                    ticks.Remove(currentTick);
                    if (ticks.Count == 0)
                        reverseIndex.Remove(key);
                }
            }

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                switch (evt.Type)
                {
                    case EventType.CooldownExpiry:
                        OnCooldownExpired?.Invoke(evt.PrimaryId);
                        break;
                    case EventType.DroppedItemExpiry:
                        OnDroppedItemExpired?.Invoke(evt.PrimaryId);
                        break;
                    case EventType.BlacklistExpiry:
                        OnBlacklistExpired?.Invoke(evt.PrimaryId, evt.SecondaryId);
                        break;
                    case EventType.ForcedWeaponGraceCheck:
                        OnForcedWeaponGraceCheck?.Invoke(evt.PrimaryId);
                        break;
                    case EventType.SkillCacheExpiry:
                        OnSkillCacheExpired?.Invoke(evt.PrimaryId);
                        break;
                    case EventType.SimpleSidearmsValidation:
                        OnSimpleSidearmsExpired?.Invoke(evt.PrimaryId, evt.SecondaryId);
                        break;
                    case EventType.ReservationExpiry:
                        OnReservationExpired?.Invoke(evt.PrimaryId, evt.SecondaryId);
                        break;
                    case EventType.TempBlacklistExpiry:
                        OnTempBlacklistExpired?.Invoke(evt.PrimaryId, evt.SecondaryId);
                        break;
                    case EventType.MessageCacheExpiry:
                        OnMessageCacheExpired?.Invoke(evt.PrimaryId, evt.SecondaryId);
                        break;
                }
            }

            ListPool<ScheduledEvent>.Return(events);
            schedule.Remove(currentTick);
        }

        public static void Cancel(EventType type, int primaryId, int secondaryId = 0)
        {
            var key = (type, primaryId);
            if (!reverseIndex.TryGetValue(key, out var ticks))
                return;

            var emptyTicks = ListPool<int>.Get();

            foreach (var tick in ticks)
            {
                if (!schedule.TryGetValue(tick, out var list))
                    continue;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var evt = list[i];
                    if (evt.Type == type && evt.PrimaryId == primaryId &&
                        (secondaryId == 0 || evt.SecondaryId == secondaryId))
                    {
                        list.RemoveAt(i);
                    }
                }

                if (list.Count == 0)
                    emptyTicks.Add(tick);
            }

            for (int i = 0; i < emptyTicks.Count; i++)
            {
                int tick = emptyTicks[i];
                if (schedule.TryGetValue(tick, out var list))
                {
                    ListPool<ScheduledEvent>.Return(list);
                    schedule.Remove(tick);
                }
            }
            ListPool<int>.Return(emptyTicks);

            if (secondaryId == 0)
                reverseIndex.Remove(key);
        }

        public static void Reset()
        {
            foreach (var list in schedule.Values)
            {
                ListPool<ScheduledEvent>.Return(list);
            }
            schedule.Clear();
            reverseIndex.Clear();
        }

        public static int GetScheduledTickCount() => schedule.Count;
    }
}
