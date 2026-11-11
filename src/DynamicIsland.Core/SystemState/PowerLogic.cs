using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.SystemState;

public readonly record struct PowerReading(bool HasBattery, bool OnAcPower, int Percent);

public readonly record struct PowerAnnouncement(string Title, string Detail, int Percent, bool Charging);

public static class PowerLogic
{
    public static PowerReading Parse(byte acLine, byte batteryFlag, byte percent)
    {
        bool noBattery = (batteryFlag & 128) != 0 || percent == 255;
        int pct = percent <= 100 ? percent : -1;
        return new PowerReading(!noBattery, acLine == 1, pct);
    }

    public static PowerAnnouncement? Diff(PowerReading? previous, PowerReading current)
    {
        if (!current.HasBattery || current.Percent < 0)
            return null;
        if (previous is not PowerReading prev || !prev.HasBattery)
            return null;

        if (prev.OnAcPower != current.OnAcPower)
        {
            return current.OnAcPower
                ? new PowerAnnouncement("正在充电", $"{current.Percent}%", current.Percent, true)
                : new PowerAnnouncement("已断开电源", $"{current.Percent}%", current.Percent, false);
        }

        if (!current.OnAcPower && NoticePolicy.ShouldAnnounceBatteryPercent(prev.Percent, current.Percent))
        {
            string title = current.Percent <= 10 ? "电量不足" : "电池";
            return new PowerAnnouncement(title, $"{current.Percent}%", current.Percent, false);
        }

        return null;
    }
}
